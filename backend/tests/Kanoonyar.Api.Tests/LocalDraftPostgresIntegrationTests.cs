using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Kanoonyar.Api.Tests;

/// <summary>
/// Real handler -> Npgsql -> disposable PostgreSQL integration. The bypassing permission
/// adapters are test-only and never registered in the application or production.
/// This suite does NOT claim real OIDC/browser authorization or production readiness.
/// </summary>
public sealed class LocalDraftPostgresIntegrationTests
{
    private static readonly ILocalPlanDraftWriteAuthorization TestWriter = new TestOnlyWriteGrant();
    private static readonly ILocalPlanDraftReadAuthorization TestReader = new TestOnlyReadGrant();

    private static string? RuntimeConnection =>
        Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_DATABASE_URL");
    private static string? AdminConnection =>
        Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");

    private static bool Enabled()
    {
        var required = Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_REQUIRED") == "1";
        if (required)
        {
            Assert.False(string.IsNullOrWhiteSpace(RuntimeConnection));
            Assert.False(string.IsNullOrWhiteSpace(AdminConnection));
        }
        return !string.IsNullOrWhiteSpace(RuntimeConnection) && !string.IsNullOrWhiteSpace(AdminConnection);
    }

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CENTER_DRAFT_WRITE_DATABASE_URL"] = RuntimeConnection,
            ["CENTER_DRAFT_READ_DATABASE_URL"] = RuntimeConnection
        }).Build();

    private static ClaimsPrincipal Principal(string subject) =>
        new(new ClaimsIdentity(new[] { new Claim("sub", subject) }, "integration-fixture"));

    private static LocalPlanDraftRevisionRequest Request(Guid draftId, long expectedRevision = 0) =>
        new(draftId, expectedRevision, "national-1", 1, "Local plan", ["Activity"]);

    private static async Task<IResult> Write(Guid draftId, string subject = "actor-a",
        string center = "center-a", long expectedRevision = 0, bool useDefaultDeny = false,
        IConfiguration? configuration = null) =>
        await LocalPlanDraftWriteEndpoint.Append(
            Principal(subject), configuration ?? Configuration(),
            useDefaultDeny ? new UnapprovedLocalPlanDraftWriteAuthorization() : TestWriter,
            center, Request(draftId, expectedRevision), CancellationToken.None);

    private static async Task<IResult> Read(string subject = "actor-a",
        string center = "center-a", bool useDefaultDeny = false) =>
        await LocalPlanDraftReadEndpoint.ListMine(Principal(subject), Configuration(),
            useDefaultDeny ? new UnapprovedLocalPlanDraftReadAuthorization() : TestReader,
            center, CancellationToken.None);

    private static int? Status(IResult result) => (result as IStatusCodeHttpResult)?.StatusCode;

    private static async Task<(long Revisions, long Audits, long Events)> Counts(Guid draft)
    {
        await using var connection = new NpgsqlConnection(AdminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT
              (SELECT count(*) FROM programs.center_plan_draft_revisions WHERE draft_id=@id),
              (SELECT count(*) FROM governance.command_audit WHERE target_id=@target),
              (SELECT count(*) FROM governance.outbox_events WHERE aggregate_id=@target)
            """, connection);
        command.Parameters.AddWithValue("id", draft);
        command.Parameters.AddWithValue("target", draft.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }

    private static JsonElement Drafts(IResult result)
    {
        Assert.Equal(200, Status(result));
        var payload = Assert.IsAssignableFrom<IValueHttpResult>(result).Value;
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        return document.RootElement.GetProperty("drafts").Clone();
    }

    [Fact]
    public async Task Real_limited_role_roundtrip_is_versioned_and_audited()
    {
        if (!Enabled()) return;
        var draft = Guid.NewGuid();
        Assert.Equal(200, Status(await Write(draft)));
        var stored = Drafts(await Read());
        Assert.Contains(stored.EnumerateArray(), item =>
            item.GetProperty("draftId").GetGuid() == draft &&
            item.GetProperty("revision").GetInt64() == 1);
        Assert.Equal(403, Status(await Read(useDefaultDeny: true)));

        Assert.Equal(200, Status(await Write(draft, expectedRevision: 1)));
        Assert.Equal(409, Status(await Write(draft, expectedRevision: 1)));
        Assert.Equal((2L, 2L, 2L), await Counts(draft));
    }

    [Fact]
    public async Task Independent_permission_and_live_membership_prevent_cross_center_and_revoked_writes()
    {
        if (!Enabled()) return;
        var draft = Guid.NewGuid();
        Assert.Equal(403, Status(await Write(draft, useDefaultDeny: true)));
        Assert.Equal(403, Status(await Write(draft, subject: "actor-b")));
        Assert.Equal(403, Status(await Write(draft, subject: "actor-revoked")));
        Assert.Equal(403, Status(await Write(draft, center: "center-b")));
        Assert.Equal((0L, 0L, 0L), await Counts(draft));
    }

    [Fact]
    public async Task Read_requires_independent_permission_and_live_center_membership()
    {
        if (!Enabled()) return;
        var draft = Guid.NewGuid();
        Assert.Equal(200, Status(await Write(draft)));
        Assert.Equal(403, Status(await Read(useDefaultDeny: true)));
        Assert.Empty(Drafts(await Read(subject: "actor-b")).EnumerateArray());
        Assert.Empty(Drafts(await Read(subject: "actor-revoked")).EnumerateArray());
        Assert.Empty(Drafts(await Read(center: "center-b")).EnumerateArray());
        Assert.Contains(Drafts(await Read()).EnumerateArray(), item =>
            item.GetProperty("draftId").GetGuid() == draft);
    }

    [Fact]
    public async Task Simultaneous_expected_revision_zero_accepts_only_one_with_matching_audit()
    {
        if (!Enabled()) return;
        var draft = Guid.NewGuid();
        var results = await Task.WhenAll(Write(draft), Write(draft));
        Assert.Equal(1, results.Count(result => Status(result) == 200));
        Assert.Equal(1, results.Count(result => Status(result) == 409));
        Assert.Equal((1L, 1L, 1L), await Counts(draft));
    }

    [Fact]
    public async Task Privileged_database_connection_is_refused_before_any_command()
    {
        if (!Enabled()) return;
        var draft = Guid.NewGuid();
        var privileged = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["CENTER_DRAFT_WRITE_DATABASE_URL"] = AdminConnection }).Build();
        Assert.Equal(503, Status(await Write(draft, configuration: privileged)));
        Assert.Equal((0L, 0L, 0L), await Counts(draft));
    }

    private sealed class TestOnlyWriteGrant : ILocalPlanDraftWriteAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal principal, string centerId,
            Guid draftId, CancellationToken ct) => ValueTask.FromResult(true);
    }
    private sealed class TestOnlyReadGrant : ILocalPlanDraftReadAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal principal, string centerId,
            CancellationToken ct) => ValueTask.FromResult(true);
    }
}
