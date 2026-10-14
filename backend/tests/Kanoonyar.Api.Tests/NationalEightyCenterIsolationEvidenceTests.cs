using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Kanoonyar.Api.Tests;

/// <summary>
/// Synthetic 80-center national-scale tenant isolation evidence on disposable PostgreSQL.
/// Deliberately not an acceptance, performance or real-center onboarding rehearsal.
/// </summary>
public sealed class NationalEightyCenterIsolationEvidenceTests
{
    private const int CenterCount = 80;
    private sealed record SyntheticCenter(string CenterId, string Subject, Guid ClassId,
        Guid SessionId, string ClassTitle, string SessionTitle);

    private sealed class ClassPermit : ICenterClassCatalogueAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal principal, string centerId,
            CancellationToken cancellationToken) => ValueTask.FromResult(true);
    }
    private sealed class SessionPermit : ICenterSourceCatalogReadAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal principal, string centerId,
            CancellationToken cancellationToken) => ValueTask.FromResult(true);
    }

    private static ClaimsPrincipal Principal(string subject) =>
        new(new ClaimsIdentity([new Claim("sub", subject)], "synthetic-center-isolation"));

    private static IConfiguration Config(string? read) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CENTER_DRAFT_READ_DATABASE_URL"] = read
        }).Build();

    private static int? Code(IResult result) => (result as IStatusCodeHttpResult)?.StatusCode;

    private static JsonElement Ok(IResult result)
    {
        Assert.Equal(200, Code(result));
        var value = Assert.IsAssignableFrom<IValueHttpResult>(result).Value;
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return json.RootElement.Clone();
    }

    private static async Task<IResult> Classes(SyntheticCenter c, IConfiguration config,
        string? actor = null, string? scope = null, bool deny = false) =>
        await CenterClassCatalogueEndpoint.List(Principal(actor ?? c.Subject), config,
            deny ? new UnapprovedCenterClassCatalogueAuthorization() : new ClassPermit(),
            scope ?? c.CenterId, null, CancellationToken.None);

    private static async Task<IResult> Sessions(SyntheticCenter c, IConfiguration config,
        string? actor = null, string? scope = null, Guid? classId = null, bool deny = false) =>
        await CenterClassSessionsEndpoint.List(Principal(actor ?? c.Subject), config,
            deny ? new UnapprovedCenterSourceCatalogReadAuthorization() : new SessionPermit(),
            scope ?? c.CenterId, classId ?? c.ClassId, null, CancellationToken.None);

    [Fact]
    public async Task Eighty_synthetic_centers_keep_original_classes_and_sessions_isolated()
    {
        // This is a required gate in CI's real restricted-PostgreSQL job.
        // Local developer runs may omit the integration database.
        var required = Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_REQUIRED") == "1";
        var admin = Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");
        var read = Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_READ_DATABASE_URL");
        if (required)
        {
            Assert.False(string.IsNullOrWhiteSpace(admin));
            Assert.False(string.IsNullOrWhiteSpace(read));
        }
        if (string.IsNullOrWhiteSpace(admin) || string.IsNullOrWhiteSpace(read)) return;
        var tenantBatch = Guid.NewGuid().ToString("N");
        var centers = Enumerable.Range(0, CenterCount).Select(i => new SyntheticCenter(
            $"national-ci-{tenantBatch}-{i:00}",
            $"national-ci-actor-{tenantBatch}-{i:00}",
            Guid.NewGuid(), Guid.NewGuid(),
            $"Synthetic source class {tenantBatch} #{i:00}",
            $"Synthetic source session {tenantBatch} #{i:00}")).ToArray();

        // All fixtures are synthetic, inserted only via the CI's disposable admin database.
        await using (var db = new NpgsqlConnection(admin))
        {
            await db.OpenAsync();
            await using var tx = await db.BeginTransactionAsync();
            foreach (var c in centers)
            {
                await using var registry = new NpgsqlCommand(
                    """
                    INSERT INTO centers.registry(center_id,display_name,is_active,
                      registered_by_subject,registered_at)
                    VALUES(@center,'Synthetic 80-center CI only',true,'fixture',now());
                    INSERT INTO centers.memberships(center_id,subject_id,role_code,is_active,
                      recorded_by_subject,recorded_at)
                    VALUES(@center,@actor,'fixture-only',true,'fixture',now());
                    INSERT INTO learning.center_classes(center_id,class_id,title)
                    VALUES(@center,@class,@title);
                    INSERT INTO learning.class_sessions(center_id,session_id,class_id,session_title,scheduled_at)
                    VALUES(@center,@session,@class,@sessionTitle,NULL);
                    """, db, tx);
                registry.Parameters.AddWithValue("center", c.CenterId);
                registry.Parameters.AddWithValue("actor", c.Subject);
                registry.Parameters.AddWithValue("class", c.ClassId);
                registry.Parameters.AddWithValue("title", c.ClassTitle);
                registry.Parameters.AddWithValue("session", c.SessionId);
                registry.Parameters.AddWithValue("sessionTitle", c.SessionTitle);
                await registry.ExecuteNonQueryAsync();
            }
            // Explicit two-center membership for one synthetic actor does not merge tenant data.
            await using (var dual = new NpgsqlCommand(
                """
                INSERT INTO centers.memberships(center_id,subject_id,role_code,is_active,
                  recorded_by_subject,recorded_at)
                VALUES(@first,@actor,'fixture-only',true,'fixture',now()),
                      (@second,@actor,'fixture-only',true,'fixture',now());
                """, db, tx))
            {
                dual.Parameters.AddWithValue("first", centers[0].CenterId);
                dual.Parameters.AddWithValue("second", centers[1].CenterId);
                dual.Parameters.AddWithValue("actor", $"national-ci-dual-{tenantBatch}");
                await dual.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
        }

        var config = Config(read);
        var allSeenClasses = new HashSet<Guid>();
        var allSeenSessions = new HashSet<Guid>();
        for (var i = 0; i < CenterCount; i++)
        {
            var owner = centers[i];
            var neighbor = centers[(i + 1) % CenterCount];

            var classReply = Ok(await Classes(owner, config));
            var rows = classReply.GetProperty("classes").EnumerateArray().ToArray();
            Assert.Single(rows);
            Assert.Equal(owner.ClassId, rows[0].GetProperty("classId").GetGuid());
            Assert.Equal(owner.ClassTitle, rows[0].GetProperty("title").GetString());
            Assert.Equal(JsonValueKind.Null,
                classReply.GetProperty("nextBeforeClassId").ValueKind);
            Assert.True(allSeenClasses.Add(owner.ClassId));

            var sessionReply = Ok(await Sessions(owner, config));
            Assert.Equal("CLASS_SESSION", sessionReply.GetProperty("sourceKind").GetString());
            Assert.Equal(owner.ClassId, sessionReply.GetProperty("classId").GetGuid());
            Assert.Equal(owner.ClassTitle, sessionReply.GetProperty("classTitle").GetString());
            var originalSessions = sessionReply.GetProperty("records").EnumerateArray().ToArray();
            Assert.Single(originalSessions);
            Assert.Equal(owner.SessionId,
                originalSessions[0].GetProperty("sourceId").GetGuid());
            Assert.Equal(owner.ClassId,
                originalSessions[0].GetProperty("classId").GetGuid());
            Assert.Equal(owner.SessionTitle,
                originalSessions[0].GetProperty("title").GetString());
            Assert.Equal(JsonValueKind.Null,
                originalSessions[0].GetProperty("scheduledAt").ValueKind);
            Assert.True(allSeenSessions.Add(owner.SessionId));

            // Requesting the neighbour's center with the original actor cannot read its data.
            Assert.Empty(Ok(await Classes(owner, config, scope: neighbor.CenterId))
                .GetProperty("classes").EnumerateArray());
            Assert.Equal(404, Code(await Sessions(owner, config,
                scope: neighbor.CenterId, classId: neighbor.ClassId)));
        }
        Assert.Equal(CenterCount, allSeenClasses.Count);
        Assert.Equal(CenterCount, allSeenSessions.Count);

        // An actor with legitimate memberships in both centers still sees only each
        // requested center, not a union of data from both (nor the other 78).
        var dualSubject = $"national-ci-dual-{tenantBatch}";
        foreach (var c in centers.Take(2))
        {
            var classes = Ok(await Classes(c, config, actor: dualSubject))
                .GetProperty("classes").EnumerateArray().ToArray();
            Assert.Single(classes);
            Assert.Equal(c.ClassId, classes[0].GetProperty("classId").GetGuid());
            var sessions = Ok(await Sessions(c, config, actor: dualSubject))
                .GetProperty("records").EnumerateArray().ToArray();
            Assert.Single(sessions);
            Assert.Equal(c.SessionId, sessions[0].GetProperty("sourceId").GetGuid());
        }
        Assert.Empty(Ok(await Classes(centers[2], config, actor: dualSubject))
            .GetProperty("classes").EnumerateArray());
        Assert.Equal(404, Code(await Sessions(centers[2], config, actor: dualSubject)));

        // A valid fixture membership alone never grants the independent operations.
        Assert.Equal(403, Code(await Classes(centers[0], config, deny: true)));
        Assert.Equal(403, Code(await Sessions(centers[0], config, deny: true)));
        Assert.Equal(503, Code(await Classes(centers[0], Config(admin))));
        Assert.Equal(503, Code(await Sessions(centers[0], Config(admin))));

        // Actual DB revocation/inactivation is reflected immediately by later reads.
        await using (var db = new NpgsqlConnection(admin))
        {
            await db.OpenAsync();
            await using var tx = await db.BeginTransactionAsync();
            await using var revoke = new NpgsqlCommand(
                """
                UPDATE centers.memberships SET is_active=false
                WHERE center_id=@revokedCenter AND subject_id=@revokedActor;
                UPDATE centers.registry SET is_active=false
                WHERE center_id=@inactiveCenter;
                """, db, tx);
            revoke.Parameters.AddWithValue("revokedCenter", centers[2].CenterId);
            revoke.Parameters.AddWithValue("revokedActor", centers[2].Subject);
            revoke.Parameters.AddWithValue("inactiveCenter", centers[3].CenterId);
            await revoke.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }
        foreach (var c in centers.Skip(2).Take(2))
        {
            Assert.Empty(Ok(await Classes(c, config)).GetProperty("classes").EnumerateArray());
            Assert.Equal(404, Code(await Sessions(c, config)));
        }
        // Revocation and inactivation did not rewrite another center's original state.
        Assert.Single(Ok(await Classes(centers[4], config))
            .GetProperty("classes").EnumerateArray());
        Assert.Single(Ok(await Sessions(centers[4], config))
            .GetProperty("records").EnumerateArray());
    }
}
