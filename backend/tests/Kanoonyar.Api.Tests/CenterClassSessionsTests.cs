using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class CenterClassSessionsTests
{
    private static ClaimsPrincipal Actor(string? subject) =>
        new(new ClaimsIdentity(subject is null ? [] : [new Claim("sub", subject)], "fixture"));
    private static IConfiguration Config(string? read = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["CENTER_DRAFT_READ_DATABASE_URL"] = read
        }).Build();
    private static int? Status(IResult result) => (result as IStatusCodeHttpResult)?.StatusCode;
    private sealed class Permit : ICenterSourceCatalogReadAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal p, string c,
            CancellationToken ct) => ValueTask.FromResult(true);
    }
    private sealed class Broken : ICenterSourceCatalogReadAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal p, string c,
            CancellationToken ct) => throw new InvalidOperationException("Independent grant unavailable");
    }

    [Fact]
    public async Task Class_scoped_source_requires_signed_subject_valid_cursor_and_independent_grant()
    {
        var cls = Guid.NewGuid();
        Task<IResult> Read(ClaimsPrincipal actor, ICenterSourceCatalogReadAuthorization grant,
            Guid? before = null, Guid? classId = null, string center = "center-a",
            IConfiguration? config = null) => CenterClassSessionsEndpoint.List(
                actor,config??Config(),grant,center,classId??cls,before,default);
        Assert.Equal(401,Status(await Read(new ClaimsPrincipal(),new Permit())));
        Assert.Equal(403,Status(await Read(Actor(null),new Permit())));
        Assert.Equal(400,Status(await Read(Actor("actor-a"),new Permit(),before:Guid.Empty)));
        Assert.Equal(400,Status(await Read(Actor("actor-a"),new Permit(),classId:Guid.Empty)));
        Assert.Equal(400,Status(await Read(Actor("actor-a"),new Permit(),center:" ")));
        Assert.Equal(403,Status(await Read(Actor("actor-a"),
            new UnapprovedCenterSourceCatalogReadAuthorization())));
        Assert.Equal(403,Status(await Read(Actor("actor-a"),new Broken())));
        Assert.Equal(503,Status(await Read(Actor("actor-a"),new Permit())));
    }

    [Fact]
    public async Task Restricted_postgres_returns_all_actual_sessions_of_only_that_class_in_true_pages()
    {
        var required = Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_REQUIRED") == "1";
        var admin=Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");
        var read=Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_READ_DATABASE_URL");
        if(required){Assert.False(string.IsNullOrWhiteSpace(admin));Assert.False(string.IsNullOrWhiteSpace(read));}
        if(string.IsNullOrWhiteSpace(admin)||string.IsNullOrWhiteSpace(read))return;

        var center="scope-"+Guid.NewGuid().ToString("N");
        var cls=Guid.NewGuid();var decoy=Guid.NewGuid();var sessionless=Guid.NewGuid();
        var target=Enumerable.Range(1,22).Select(i=>
            Guid.Parse($"10000000-0000-4000-8000-{i:000000000000}")).ToArray();
        var unrelated=Enumerable.Range(1,24).Select(i=>
            Guid.Parse($"f0000000-0000-4000-8000-{i:000000000000}")).ToArray();
        await using(var db=new NpgsqlConnection(admin))
        {
            await db.OpenAsync();
            await using(var setup=new NpgsqlCommand(
                """
                INSERT INTO centers.registry(center_id,display_name,is_active,registered_by_subject,registered_at)
                VALUES(@center,'S47 isolated source',true,'fixture',now());
                INSERT INTO centers.memberships(center_id,subject_id,role_code,is_active,recorded_by_subject,recorded_at)
                VALUES(@center,'actor-a','fixture-member',true,'fixture',now());
                """,db))
            {
                setup.Parameters.AddWithValue("center",center);
                await setup.ExecuteNonQueryAsync();
            }
            foreach(var entry in new[]{(cls,"Target class"),(decoy,"Unrelated class"),(sessionless,"Zero sessions")})
            {
                await using var insert=new NpgsqlCommand(
                    "INSERT INTO learning.center_classes(center_id,class_id,title) VALUES(@center,@id,@title)",db);
                insert.Parameters.AddWithValue("center",center);
                insert.Parameters.AddWithValue("id",entry.Item1);
                insert.Parameters.AddWithValue("title",entry.Item2);
                await insert.ExecuteNonQueryAsync();
            }
            foreach(var entry in target.Select(x=>(x,cls,"Target session"))
                .Concat(unrelated.Select(x=>(x,decoy,"Decoy session"))))
            {
                await using var insert=new NpgsqlCommand(
                    """
                    INSERT INTO learning.class_sessions(center_id,session_id,class_id,session_title,scheduled_at)
                    VALUES(@center,@id,@class,@title,NULL)
                    """,db);
                insert.Parameters.AddWithValue("center",center);
                insert.Parameters.AddWithValue("id",entry.Item1);
                insert.Parameters.AddWithValue("class",entry.Item2);
                insert.Parameters.AddWithValue("title",entry.Item3);
                await insert.ExecuteNonQueryAsync();
            }
        }
        Task<IResult> Read(string actor,string scope,Guid classId,Guid? cursor=null,
            ICenterSourceCatalogReadAuthorization? grant=null,IConfiguration? configuration=null)=>
            CenterClassSessionsEndpoint.List(Actor(actor),configuration??Config(read),
                grant??new Permit(),scope,classId,cursor,default);
        static (Guid[] Ids,Guid? Cursor) Page(IResult result,Guid classId,string title)
        {
            Assert.Equal(200,Status(result));
            using var doc=JsonDocument.Parse(JsonSerializer.Serialize(
                Assert.IsAssignableFrom<IValueHttpResult>(result).Value));
            var root=doc.RootElement;
            Assert.Equal("CLASS_SESSION",root.GetProperty("sourceKind").GetString());
            Assert.Equal(classId,root.GetProperty("classId").GetGuid());
            Assert.Equal(title,root.GetProperty("classTitle").GetString());
            var records=root.GetProperty("records").EnumerateArray().ToArray();
            Assert.All(records,row=>{
                Assert.Equal(classId,row.GetProperty("classId").GetGuid());
                Assert.Equal(title,row.GetProperty("classTitle").GetString());
                Assert.Equal(JsonValueKind.Null,row.GetProperty("scheduledAt").ValueKind);
            });
            var c=root.GetProperty("nextBeforeSourceId");
            return(records.Select(x=>x.GetProperty("sourceId").GetGuid()).ToArray(),
                c.ValueKind==JsonValueKind.Null?null:c.GetGuid());
        }
        var first=Page(await Read("actor-a",center,cls),cls,"Target class");
        Assert.Equal(20,first.Ids.Length);Assert.Equal(first.Ids[^1],first.Cursor);
        var second=Page(await Read("actor-a",center,cls,first.Cursor),cls,"Target class");
        Assert.Equal(2,second.Ids.Length);Assert.Null(second.Cursor);
        Assert.True(target.ToHashSet().SetEquals(first.Ids.Concat(second.Ids)));
        var empty=Page(await Read("actor-a",center,sessionless),sessionless,"Zero sessions");
        Assert.Empty(empty.Ids);Assert.Null(empty.Cursor);
        Assert.Equal(404,Status(await Read("actor-revoked",center,cls)));
        Assert.Equal(404,Status(await Read("actor-a","center-b",cls)));
        Assert.Equal(404,Status(await Read("actor-a",center,Guid.NewGuid())));
        Assert.Equal(403,Status(await Read("actor-a",center,cls,grant:
            new UnapprovedCenterSourceCatalogReadAuthorization())));
        Assert.Equal(503,Status(await Read("actor-a",center,cls,configuration:Config(admin))));
    }
}
