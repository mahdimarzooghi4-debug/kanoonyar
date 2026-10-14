using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace Kanoonyar.Api.Programs;

/// <summary>
/// S59: versioned, integrity-checkable structural preflight review packet.
/// The SHA-256 digest is NOT a signature, trusted attestation, permission,
/// proof of human approval or authorization to issue. No runtime DI or route.
/// </summary>
public static class NationalIssuancePreflightReviewPacket
{
    private const string Format = "KANOONYAR_S59_ISSUANCE_PREFLIGHT_V1";
    private static readonly StringComparer Exact = StringComparer.Ordinal;

    public sealed record Packet(
        string FormatVersion,
        string DigestSha256,
        NationalIssuanceBatchPreflight.Observation OriginalObservation,
        bool HumanReviewRecorded,
        bool IssuanceAuthorized);

    public sealed record Recheck(
        bool SourceUnchanged,
        Packet CurrentPacket,
        bool HumanReviewRecorded,
        bool IssuanceAuthorized);

    private static NationalIssuanceBatchPreflight.Observation Freeze(
        NationalIssuanceBatchPreflight.Observation? source)
    {
        if (source is null || string.IsNullOrWhiteSpace(source.ProgramId) ||
            source.ProgramId != source.ProgramId.Trim() ||
            source.ProgramId.Length > 256 || source.ProgramId.Any(char.IsControl) ||
            source.NationalVersion <= 0 || source.Centers is null ||
            source.Centers.Count == 0 || source.IssuesAnyCommand ||
            source.HumanIssuanceAuthorizationVerified)
            throw new ArgumentException("Invalid or authorizing issuance preflight observation.");

        var centers = source.Centers.ToArray();
        var seen = new HashSet<string>(Exact);
        foreach (var center in centers)
        {
            if (center is null || string.IsNullOrWhiteSpace(center.CenterId) ||
                center.CenterId != center.CenterId.Trim() ||
                center.CenterId.Length > 256 || center.CenterId.Any(char.IsControl) ||
                !seen.Add(center.CenterId) ||
                (!center.RegistryExists && (center.RegistryActive || center.ExactIssuanceExists)) ||
                (center.ExactIssuanceExists && !source.ExactPublicationExists))
                throw new ArgumentException("Impossible, duplicated or ambiguous original-center evidence.");
        }

        // Defensive copy avoids a mutable caller-provided list silently changing
        // the reviewed center selection after its digest has been calculated.
        return new NationalIssuanceBatchPreflight.Observation(
            source.ProgramId, source.NationalVersion, source.ExactPublicationExists,
            Array.AsReadOnly(centers),
            IssuesAnyCommand: false,
            HumanIssuanceAuthorizationVerified: false);
    }

    private static byte[] CanonicalBytes(NationalIssuanceBatchPreflight.Observation snapshot)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            // BinaryWriter writes deterministic length-prefixed UTF-8 strings,
            // little-endian integers and boolean bytes, avoiding delimiter ambiguities.
            writer.Write(Format);
            writer.Write(snapshot.ProgramId);
            writer.Write(snapshot.NationalVersion);
            writer.Write(snapshot.ExactPublicationExists);
            writer.Write(snapshot.Centers.Count);
            foreach (var c in snapshot.Centers)
            {
                writer.Write(c.CenterId);
                writer.Write(c.RegistryExists);
                writer.Write(c.RegistryActive);
                writer.Write(c.ExactIssuanceExists);
            }
            writer.Write(false); // no issue command or human authorization
            writer.Write(false);
        }
        return stream.ToArray();
    }

    public static Packet Create(NationalIssuanceBatchPreflight.Observation? source)
    {
        var snapshot = Freeze(source);
        var digest = Convert.ToHexString(SHA256.HashData(CanonicalBytes(snapshot)));
        return new Packet(Format, digest, snapshot,
            HumanReviewRecorded: false, IssuanceAuthorized: false);
    }

    /// <summary>
    /// Detect accidental/tampered evidence changes before re-reading SQL.
    /// This detects changes to the packet, but does not authenticate the
    /// origin of either its inputs or the database read credentials.
    /// </summary>
    public static bool HasUnmodifiedEvidence(Packet? packet)
    {
        if (packet is null || packet.FormatVersion != Format ||
            packet.HumanReviewRecorded || packet.IssuanceAuthorized ||
            packet.DigestSha256 is null || packet.DigestSha256.Length != 64)
            return false;
        try
        {
            var recomputed = Create(packet.OriginalObservation);
            var expected = Convert.FromHexString(recomputed.DigestSha256);
            var supplied = Convert.FromHexString(packet.DigestSha256);
            return CryptographicOperations.FixedTimeEquals(expected, supplied);
        }
        catch (Exception e) when (e is ArgumentException or FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Re-inspect the *same exact* original (program,version,ordered center IDs)
    /// and detect source drift. Unchanged evidence is NOT permission to issue:
    /// any later issuance must independently authenticate a human and recheck
    /// current facts and authority within its own serializable command.
    /// </summary>
    public static async Task<Recheck> RecheckAsync(
        NpgsqlConnection connection, Packet packet, CancellationToken cancellationToken)
    {
        if (!HasUnmodifiedEvidence(packet))
            throw new ArgumentException("Review evidence checksum or structure is invalid.");
        var original = packet.OriginalObservation;
        var current = await NationalIssuanceBatchPreflight.InspectAsync(
            connection, original.ProgramId, original.NationalVersion,
            original.Centers.Select(c => c.CenterId).ToArray(), cancellationToken);
        var currentPacket = Create(current);
        var unchanged = CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(packet.DigestSha256),
            Convert.FromHexString(currentPacket.DigestSha256));
        return new Recheck(unchanged, currentPacket,
            HumanReviewRecorded: false, IssuanceAuthorized: false);
    }
}
