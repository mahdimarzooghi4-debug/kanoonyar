using System.Data;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

/// <summary>
/// S58: restricted to an offline headquarters-review preparation workflow.
/// NOT an endpoint, NOT registered in DI and NOT a grant/issuance operation.
/// A preflight result is structural observation, NEVER authorization to issue.
/// The caller must supply an independently permitted DB connection; no
/// Production read privilege is granted by this component.
/// </summary>
public static class NationalIssuanceBatchPreflight
{
    public sealed record CenterObservation(
        string CenterId,
        bool RegistryExists,
        bool RegistryActive,
        bool ExactIssuanceExists);

    public sealed record Observation(
        string ProgramId,
        int NationalVersion,
        bool ExactPublicationExists,
        IReadOnlyList<CenterObservation> Centers,
        bool IssuesAnyCommand,
        bool HumanIssuanceAuthorizationVerified);

    private static string[] ValidateExplicitSelection(
        string? programId, int nationalVersion, IReadOnlyCollection<string>? centerIds)
    {
        if (string.IsNullOrWhiteSpace(programId) || programId != programId.Trim() ||
            programId.Length > 256 || programId.Any(char.IsControl) ||
            nationalVersion <= 0 || centerIds is null || centerIds.Count == 0)
            throw new ArgumentException("A pinned program version and nonempty explicit center selection are required.");

        var selected = centerIds.ToArray();
        if (selected.Any(c => string.IsNullOrWhiteSpace(c) || c != c.Trim() ||
                              c.Length > 256 || c.Any(char.IsControl)) ||
            selected.Distinct(StringComparer.Ordinal).Count() != selected.Length)
            throw new ArgumentException("Center selection must contain distinct, exact nonblank source IDs.");
        return selected;
    }

    /// <summary>
    /// Reads only original publication, center registry and issuance data under
    /// one repeatable-read, explicitly READ ONLY transaction. Caller-supplied
    /// privileges are not widened. The snapshot becomes stale immediately after
    /// return; no code may use it as a substitute for a later authorized write
    /// transaction's fresh publication, grant and issuance checks.
    /// </summary>
    public static async Task<Observation> InspectAsync(
        NpgsqlConnection connection,
        string programId,
        int nationalVersion,
        IReadOnlyCollection<string> centerIds,
        CancellationToken cancellationToken)
    {
        var centers = ValidateExplicitSelection(programId, nationalVersion, centerIds);
        if (connection.State != ConnectionState.Open)
            throw new InvalidOperationException("The permitted inspection connection must already be open.");

        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);
        try
        {
            await using (var readonlyCommand = new NpgsqlCommand(
                "SET TRANSACTION READ ONLY", connection, transaction))
                await readonlyCommand.ExecuteNonQueryAsync(cancellationToken);

            bool published;
            await using (var publication = new NpgsqlCommand(
                """
                SELECT EXISTS (
                  SELECT 1 FROM programs.national_program_publications
                  WHERE program_id=@program_id AND version=@version
                )
                """, connection, transaction))
            {
                publication.Parameters.Add("program_id", NpgsqlDbType.Text).Value = programId;
                publication.Parameters.Add("version", NpgsqlDbType.Integer).Value = nationalVersion;
                published = (bool)(await publication.ExecuteScalarAsync(cancellationToken)
                    ?? throw new InvalidOperationException("Publication lookup returned no value."));
            }

            var observations = new List<CenterObservation>(centers.Length);
            await using (var command = new NpgsqlCommand(
                """
                SELECT requested.center_id, r.center_id IS NOT NULL,
                       COALESCE(r.is_active,false),
                       EXISTS (
                         SELECT 1 FROM programs.national_program_issuances i
                         WHERE i.center_id=requested.center_id
                           AND i.program_id=@program_id AND i.national_version=@version
                       )
                FROM unnest(@center_ids::text[]) WITH ORDINALITY AS requested(center_id, ordinal)
                LEFT JOIN centers.registry r ON r.center_id=requested.center_id
                ORDER BY requested.ordinal
                """, connection, transaction))
            {
                command.Parameters.Add("center_ids", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = centers;
                command.Parameters.Add("program_id", NpgsqlDbType.Text).Value = programId;
                command.Parameters.Add("version", NpgsqlDbType.Integer).Value = nationalVersion;
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    observations.Add(new CenterObservation(
                        reader.GetString(0), reader.GetBoolean(1),
                        reader.GetBoolean(2), reader.GetBoolean(3)));
            }
            if (observations.Count != centers.Length ||
                observations.Where((o, i) => o.CenterId != centers[i]).Any())
                throw new InvalidOperationException("Preflight source response did not match the exact requested centers.");

            await transaction.CommitAsync(cancellationToken);
            return new Observation(
                programId, nationalVersion, published,
                Array.AsReadOnly(observations.ToArray()),
                IssuesAnyCommand: false,
                HumanIssuanceAuthorizationVerified: false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
