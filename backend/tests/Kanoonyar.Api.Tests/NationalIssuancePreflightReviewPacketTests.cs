using Kanoonyar.Api.Programs;
using Npgsql;
using Xunit;
using PacketBoundary = Kanoonyar.Api.Programs.NationalIssuancePreflightReviewPacket;
using Preflight = Kanoonyar.Api.Programs.NationalIssuanceBatchPreflight;

namespace Kanoonyar.Api.Tests;

public sealed class NationalIssuancePreflightReviewPacketTests
{
    private static Preflight.Observation Source(
        IReadOnlyList<Preflight.CenterObservation>? centers = null) => new(
        "original-national-program", 2, true,
        centers ?? new[]
        {
            new Preflight.CenterObservation("center-a", true, true, false),
            new Preflight.CenterObservation("center-b", true, false, true)
        },
        IssuesAnyCommand: false, HumanIssuanceAuthorizationVerified: false);

    [Fact]
    public void Same_exact_version_and_ordered_evidence_yield_repeatable_stable_digest()
    {
        var original = PacketBoundary.Create(Source());
        var repeated = PacketBoundary.Create(Source());
        Assert.Equal("KANOONYAR_S59_ISSUANCE_PREFLIGHT_V1", original.FormatVersion);
        Assert.Equal(64, original.DigestSha256.Length);
        Assert.Equal(original.DigestSha256, repeated.DigestSha256);
        Assert.True(PacketBoundary.HasUnmodifiedEvidence(original));
        Assert.False(original.HumanReviewRecorded);
        Assert.False(original.IssuanceAuthorized);
        Assert.False(original.OriginalObservation.HumanIssuanceAuthorizationVerified);
        Assert.False(original.OriginalObservation.IssuesAnyCommand);
    }

    [Fact]
    public void Digest_binds_every_original_source_field_and_exact_selection_order()
    {
        var original = PacketBoundary.Create(Source());
        Assert.NotEqual(original.DigestSha256,
            PacketBoundary.Create(Source() with { ProgramId = "other-program" }).DigestSha256);
        Assert.NotEqual(original.DigestSha256,
            PacketBoundary.Create(Source() with { NationalVersion = 1 }).DigestSha256);
        Assert.NotEqual(original.DigestSha256,
            PacketBoundary.Create(Source() with { ExactPublicationExists = false,
                Centers = new[] {
                    new Preflight.CenterObservation("center-a",true,true,false),
                    new Preflight.CenterObservation("center-b",true,false,false)
                } }).DigestSha256);
        var rows = Source().Centers.ToArray();
        Assert.NotEqual(original.DigestSha256,
            PacketBoundary.Create(Source(new[] {rows[1],rows[0]})).DigestSha256);
        Assert.NotEqual(original.DigestSha256,
            PacketBoundary.Create(Source(new[] {rows[0] with { RegistryActive = false }, rows[1]})).DigestSha256);
        Assert.NotEqual(original.DigestSha256,
            PacketBoundary.Create(Source(new[] {rows[0] with { ExactIssuanceExists = true }, rows[1]})).DigestSha256);
        Assert.NotEqual(original.DigestSha256,
            PacketBoundary.Create(Source(new[] { rows[0], rows[1] with { RegistryExists = false,
                RegistryActive = false, ExactIssuanceExists = false } })).DigestSha256);
    }

    [Fact]
    public void Original_packet_freezes_selection_and_mutation_is_detected()
    {
        var mutable = new List<Preflight.CenterObservation> {
            new("center-a",true,true,false),new("center-b",true,false,true)
        };
        var packet = PacketBoundary.Create(Source(mutable));
        mutable[0] = new("attacker-center",true,true,false);
        mutable.Clear();
        Assert.True(PacketBoundary.HasUnmodifiedEvidence(packet));
        Assert.Equal(new[] {"center-a","center-b"},
            packet.OriginalObservation.Centers.Select(c=>c.CenterId));
        var forged = packet with { OriginalObservation = packet.OriginalObservation with {
            NationalVersion = 1 } };
        Assert.False(PacketBoundary.HasUnmodifiedEvidence(forged));
        Assert.False(PacketBoundary.HasUnmodifiedEvidence(packet with { DigestSha256 = new string('A',64) }));
        Assert.False(PacketBoundary.HasUnmodifiedEvidence(packet with { DigestSha256 = "invalid" }));
        Assert.False(PacketBoundary.HasUnmodifiedEvidence(packet with { FormatVersion = "OTHER" }));
        Assert.False(PacketBoundary.HasUnmodifiedEvidence(packet with { IssuanceAuthorized = true }));
        Assert.False(PacketBoundary.HasUnmodifiedEvidence(packet with { HumanReviewRecorded = true }));
        Assert.False(PacketBoundary.HasUnmodifiedEvidence(null));
    }

    [Fact]
    public void Impossible_unapproved_or_ambiguous_evidence_is_rejected()
    {
        var original = Source();
        Assert.Throws<ArgumentException>(() => PacketBoundary.Create(null));
        Assert.Throws<ArgumentException>(() => PacketBoundary.Create(original with {
            IssuesAnyCommand = true }));
        Assert.Throws<ArgumentException>(() => PacketBoundary.Create(original with {
            HumanIssuanceAuthorizationVerified = true }));
        Assert.Throws<ArgumentException>(() => PacketBoundary.Create(original with {
            NationalVersion = 0 }));
        Assert.Throws<ArgumentException>(() => PacketBoundary.Create(original with {
            Centers = Array.Empty<Preflight.CenterObservation>() }));
        Assert.Throws<ArgumentException>(() => PacketBoundary.Create(original with {
            Centers = new[] {
                new Preflight.CenterObservation("center-a",true,true,false),
                new Preflight.CenterObservation("center-a",true,true,false)
            } }));
        Assert.Throws<ArgumentException>(() => PacketBoundary.Create(original with {
            Centers = new[] {
                new Preflight.CenterObservation("missing",false,true,false)
            } }));
        Assert.Throws<ArgumentException>(() => PacketBoundary.Create(original with {
            Centers = new[] {
                new Preflight.CenterObservation("missing",false,false,true)
            } }));
        Assert.Throws<ArgumentException>(() => PacketBoundary.Create(original with {
            ProgramId = " forged-source" }));
    }

    [Fact]
    public async Task Altered_packet_does_not_trigger_even_a_database_read()
    {
        var source = PacketBoundary.Create(Source());
        await using var closed = new NpgsqlConnection();
        await Assert.ThrowsAsync<ArgumentException>(()=>PacketBoundary.RecheckAsync(
            closed, source with {DigestSha256 = new string('0',64)},
            CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(()=>PacketBoundary.RecheckAsync(
            closed, source with {IssuanceAuthorized = true}, CancellationToken.None));
    }

    [Fact]
    public void Eighty_center_evidence_has_unique_digests_for_changes_in_each_original_center()
    {
        var rows = Enumerable.Range(0,80)
            .Select(i=>new Preflight.CenterObservation("synthetic-center-"+i,true,true,false))
            .ToArray();
        var baseline = PacketBoundary.Create(Source(rows));
        for(var i=0;i<rows.Length;i++)
        {
            var changed = rows.ToArray();
            changed[i] = changed[i] with {RegistryActive = false};
            var packet = PacketBoundary.Create(Source(changed));
            Assert.NotEqual(baseline.DigestSha256, packet.DigestSha256);
            Assert.True(PacketBoundary.HasUnmodifiedEvidence(packet));
            Assert.False(packet.IssuanceAuthorized);
        }
    }
}
