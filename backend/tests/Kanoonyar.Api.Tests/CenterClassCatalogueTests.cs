using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class CenterClassCatalogueTests
{
    private static ClaimsPrincipal Actor(string? subject) =>
        new(new ClaimsIdentity(subject is null ? [] : [new Claim("sub", subject)], "fixture"));
    private static IConfiguration Config(string? reader = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["CENTER_DRAFT_READ_DATABASE_URL"] = reader
        }).Build();
    private static int? Status(IResult result) =>
        (result as IStatusCodeHttpResult)?.StatusCode;
    private sealed class Permit : ICenterClassCatalogueAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor, string center,
            CancellationToken ct) => ValueTask.FromResult(true);
    }
    private sealed class Broken : ICenterClassCatalogueAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor, string center,
            CancellationToken ct) => throw new InvalidOperationException("Grant unavailable");
    }

    [Fact]
    public async Task Directory_is_authenticated_cursor_validated_and_independently_default_denied()
    {
        Task<IResult> Read(ClaimsPrincipal p, ICenterClassCatalogueAuthorization grant,
            string center = "center-a", Guid? cursor = null, IConfiguration? config = null) =>
            CenterClassCatalogueEndpoint.List(p, config ?? Config(), grant, center, cursor, default);
        Assert.Equal(401, Status(await Read(new ClaimsPrincipal(), new Permit())));
        Assert.Equal(403, Status(await Read(Actor(null), new Permit())));
        Assert.Equal(400, Status(await Read(Actor("actor-a"), new Permit(), center: " ")));
        Assert.Equal(400, Status(await Read(Actor("actor-a"), new Permit(), cursor: Guid.Empty)));
        Assert.Equal(403, Status(await Read(Actor("actor-a"), new UnapprovedCenterClassCatalogueAuthorization())));
        Assert.Equal(403, Status(await Read(Actor("actor-a"), new Broken())));
        Assert.Equal(503, Status(await Read(Actor("actor-a"), new Permit())));
    }

    [Fact]
    public async Task Original_class_owner_directory_lists_sessionless_classes_with_restricted_sql()
    {
        var required = Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_REQUIRED") == "1";
        var admin = Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");
        var reader = Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_READ_DATABASE_URL");
        if (required)
        {
            Assert.False(string.IsNullOrWhiteSpace(admin));
            Assert.False(string.IsNullOrWhiteSpace(reader));
        }
        if (string.IsNullOrWhiteSpace(admin) || string.IsNullOrWhiteSpace(reader)) return;

        var center = "catalogue-" + Guid.NewGuid().ToString("N");
        var ids = Enumerable.Range(0,22).Select(_=>Guid.NewGuid()).ToArray();
        await using (var db = new NpgsqlConnection(admin))
        {
            await db.OpenAsync();
            await using (var init = new NpgsqlCommand(
                """
                INSERT INTO centers.registry(center_id,display_name,is_active,registered_by_subject,registered_at)
                VALUES(@center,'Synthetic catalogue isolated center',true,'fixture',now());
                INSERT INTO centers.memberships(center_id,subject_id,role_code,is_active,recorded_by_subject,recorded_at)
                VALUES(@center,'actor-a','fixture-member',true,'fixture',now());
                """,db))
            {
                init.Parameters.AddWithValue("center",center);
                await init.ExecuteNonQueryAsync();
            }
            foreach (var id in ids)
            {
                await using var insert = new NpgsqlCommand(
                    "INSERT INTO learning.center_classes(center_id,class_id,title) VALUES(@center,@id,'Sessionless fixture')",db);
                insert.Parameters.AddWithValue("center",center);
                insert.Parameters.AddWithValue("id",id);
                await insert.ExecuteNonQueryAsync();
            }
            // None of these classes has an entry in learning.class_sessions.
        }
        Task<IResult> Query(string subject, string scope, Guid? before=null,
            ICenterClassCatalogueAuthorization? grant=null, IConfiguration? config=null) =>
            CenterClassCatalogueEndpoint.List(Actor(subject),config??Config(reader),
                grant??new Permit(),scope,before,default);
        static (Guid[] Ids, Guid? Cursor) Page(IResult result)
        {
            Assert.Equal(200,Status(result));
            using var doc=JsonDocument.Parse(JsonSerializer.Serialize(
                Assert.IsAssignableFrom<IValueHttpResult>(result).Value));
            var root=doc.RootElement;
            var records=root.GetProperty("classes").EnumerateArray().ToArray();
            Assert.All(records,x=>Assert.Equal("Sessionless fixture",x.GetProperty("title").GetString()));
            var cursor=root.GetProperty("nextBeforeClassId");
            return (records.Select(x=>x.GetProperty("classId").GetGuid()).ToArray(),
                cursor.ValueKind==JsonValueKind.Null?null:cursor.GetGuid());
        }
        var p1=Page(await Query("actor-a",center));
        Assert.Equal(20,p1.Ids.Length);
        Assert.NotNull(p1.Cursor);
        Assert.Equal(p1.Ids[^1],p1.Cursor);
        var p2=Page(await Query("actor-a",center,p1.Cursor));
        Assert.Equal(2,p2.Ids.Length);
        Assert.Null(p2.Cursor);
        Assert.Equal(22,p1.Ids.Concat(p2.Ids).Distinct().Count());
        Assert.True(ids.ToHashSet().SetEquals(p1.Ids.Concat(p2.Ids)));
        Assert.Empty(Page(await Query("actor-revoked",center)).Ids);
        Assert.Empty(Page(await Query("actor-a","center-b")).Ids);
        Assert.Equal(403,Status(await Query("actor-a",center,grant:new UnapprovedCenterClassCatalogueAuthorization())));
        Assert.Equal(503,Status(await Query("actor-a",center,config:Config(admin))));
    }
}
