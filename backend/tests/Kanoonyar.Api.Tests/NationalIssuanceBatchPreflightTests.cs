using Kanoonyar.Api.Programs;
using Npgsql;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class NationalIssuanceBatchPreflightTests
{
    private static readonly string? AdminUrl =
        Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");

    [Theory]
    [InlineData(null, 1)]
    [InlineData("", 1)]
    [InlineData(" ", 1)]
    [InlineData("source-id", 0)]
    [InlineData("source-id", -1)]
    [InlineData(" source-id", 1)]
    public async Task Missing_or_ambiguous_source_identity_fails_before_database_access(
        string? programId, int version)
    {
        await using var closed = new NpgsqlConnection();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            NationalIssuanceBatchPreflight.InspectAsync(closed, programId!, version,
                new[] { "center-a" }, CancellationToken.None));
    }

    [Fact]
    public async Task Selection_must_be_explicit_distinct_exact_and_nonempty()
    {
        await using var closed = new NpgsqlConnection();
        foreach (var ids in new[]
        {
            Array.Empty<string>(),
            new[] { "center-a", "center-a" },
            new[] { "center-a", " " },
            new[] { " center-a" },
            new[] { "center-a\n" }
        })
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                NationalIssuanceBatchPreflight.InspectAsync(closed, "program-1", 1, ids,
                    CancellationToken.None));
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NationalIssuanceBatchPreflight.InspectAsync(closed, "program-1", 1,
                new[] { "center-a" }, CancellationToken.None));
    }

    [Fact]
    public async Task Eighty_explicit_synthetic_centers_and_pinned_versions_are_only_observed_not_issued()
    {
        var required = Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_REQUIRED") == "1";
        if (required) Assert.False(string.IsNullOrWhiteSpace(AdminUrl));
        if (string.IsNullOrWhiteSpace(AdminUrl)) return;

        var prefix = "s58-" + Guid.NewGuid().ToString("N") + "-";
        var programId = "s58-program-" + Guid.NewGuid().ToString("N");
        var centers = Enumerable.Range(0, 80)
            .Select(i => prefix + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

        await using var db = new NpgsqlConnection(AdminUrl);
        await db.OpenAsync();
        await using (var seed = await db.BeginTransactionAsync())
        {
            await using var fixtures = new NpgsqlCommand(
                """
                INSERT INTO centers.registry
                  (center_id,display_name,is_active,registered_by_subject,registered_at)
                SELECT @prefix || lpad(i::text,2,'0'), 'Synthetic center '||i,
                       i <> 3, 's58-fixture', now()
                FROM generate_series(0,79) AS i;
                INSERT INTO programs.national_program_versions
                  (program_id,version,title,national_objectives,mandatory_requirements,
                   created_by_subject,created_at)
                SELECT @program, i, 'Synthetic national version '||i,
                       '["source goal"]'::jsonb, '["source requirement"]'::jsonb,
                       's58-fixture',now()
                FROM generate_series(1,3) AS i;
                INSERT INTO programs.national_program_publications
                  (program_id,version,publisher_subject,published_at)
                SELECT @program, i, 's58-fixture',now()
                FROM generate_series(1,2) AS i;
                -- Only center 0 has already been issued national version 2.
                -- Center 1 instead has historical issuance of version 1.
                INSERT INTO programs.national_program_issuances
                  (issuance_id,center_id,program_id,national_version,
                   issued_by_subject,issued_at)
                VALUES(gen_random_uuid(),@prefix||'00',@program,2,'s58-fixture',now()),
                      (gen_random_uuid(),@prefix||'01',@program,1,'s58-fixture',now());
                """, db, seed);
            fixtures.Parameters.AddWithValue("prefix", prefix);
            fixtures.Parameters.AddWithValue("program", programId);
            await fixtures.ExecuteNonQueryAsync();
            await seed.CommitAsync();
        }

        async Task<long> CountIssuances()
        {
            await using var count = new NpgsqlCommand(
                "SELECT count(*) FROM programs.national_program_issuances WHERE program_id=@program",
                db);
            count.Parameters.AddWithValue("program", programId);
            return (long)(await count.ExecuteScalarAsync())!;
        }
        Assert.Equal(2L, await CountIssuances());

        // An explicitly selected order is preserved; no request expands to
        // "all centers" by default and no center is inferred from an actor.
        var requested = centers.Reverse().ToArray();
        var observed = await NationalIssuanceBatchPreflight.InspectAsync(
            db, programId, 2, requested, CancellationToken.None);
        Assert.True(observed.ExactPublicationExists);
        Assert.Equal(programId, observed.ProgramId);
        Assert.Equal(2, observed.NationalVersion);
        Assert.False(observed.IssuesAnyCommand);
        Assert.False(observed.HumanIssuanceAuthorizationVerified);
        Assert.Equal(80, observed.Centers.Count);
        Assert.Equal(requested, observed.Centers.Select(x => x.CenterId));
        Assert.All(observed.Centers, c => Assert.True(c.RegistryExists));
        Assert.Equal(79, observed.Centers.Count(c => c.RegistryActive));
        Assert.False(observed.Centers.Single(x => x.CenterId == centers[3]).RegistryActive);
        Assert.Single(observed.Centers.Where(c => c.ExactIssuanceExists));
        Assert.True(observed.Centers.Single(x => x.CenterId == centers[0]).ExactIssuanceExists);
        Assert.False(observed.Centers.Single(x => x.CenterId == centers[1]).ExactIssuanceExists);

        var pinnedOld = await NationalIssuanceBatchPreflight.InspectAsync(db,
            programId, 1, new[] { centers[0], centers[1] }, CancellationToken.None);
        Assert.True(pinnedOld.ExactPublicationExists);
        Assert.False(pinnedOld.Centers[0].ExactIssuanceExists);
        Assert.True(pinnedOld.Centers[1].ExactIssuanceExists);
        Assert.False(pinnedOld.HumanIssuanceAuthorizationVerified);

        // The original unpublished version is NOT promoted to issuance and
        // existing issued version 2 is NOT silently considered issuance of v3.
        var unpublished = await NationalIssuanceBatchPreflight.InspectAsync(
            db, programId, 3, new[] { centers[0], centers[1] }, CancellationToken.None);
        Assert.False(unpublished.ExactPublicationExists);
        Assert.All(unpublished.Centers, c => Assert.False(c.ExactIssuanceExists));
        var unknown = await NationalIssuanceBatchPreflight.InspectAsync(db,
            programId, 2, new[] { "nonexistent-" + prefix, centers[2] }, CancellationToken.None);
        Assert.False(unknown.Centers[0].RegistryExists);
        Assert.False(unknown.Centers[0].RegistryActive);
        Assert.False(unknown.Centers[0].ExactIssuanceExists);
        Assert.True(unknown.Centers[1].RegistryExists);

        // Every preflight read is strictly non-command-bearing.
        Assert.Equal(2L, await CountIssuances());
        await using var audit = new NpgsqlCommand(
            """
            SELECT count(*) FROM governance.command_audit
            WHERE action_code='NATIONAL_PROGRAM_ISSUANCE_RECORDED'
              AND metadata->>'program_id'=@program
            """, db);
        audit.Parameters.AddWithValue("program", programId);
        Assert.Equal(0L, (long)(await audit.ExecuteScalarAsync())!);

        // Current state always takes precedence on a subsequent preflight.
        await using (var deactivate = new NpgsqlCommand(
            "UPDATE centers.registry SET is_active=false WHERE center_id=@id", db))
        {
            deactivate.Parameters.AddWithValue("id", centers[2]);
            Assert.Equal(1, await deactivate.ExecuteNonQueryAsync());
        }
        var repeated = await NationalIssuanceBatchPreflight.InspectAsync(
            db, programId, 2, new[] { centers[2] }, CancellationToken.None);
        Assert.False(repeated.Centers[0].RegistryActive);
        Assert.False(repeated.HumanIssuanceAuthorizationVerified);
        Assert.Equal(2L, await CountIssuances());
    }
}
