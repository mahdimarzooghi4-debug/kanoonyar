using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

public sealed record LocalPlanDraftRevisionRequest(
    Guid DraftId, long ExpectedRevision, string ProgramId, int NationalVersion,
    string LocalTitle, string[] LocalActivities);

/// <summary>Must independently prove a live, revocable operation-specific grant, not a membership or role label.</summary>
public interface ILocalPlanDraftWriteAuthorization
{
    ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal principal, string centerId, Guid draftId,
        CancellationToken cancellationToken);
}

/// <summary>No approved grant policy exists: this is the ONLY registered runtime implementation.</summary>
public sealed class UnapprovedLocalPlanDraftWriteAuthorization : ILocalPlanDraftWriteAuthorization
{
    public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal principal, string centerId, Guid draftId,
        CancellationToken cancellationToken) => ValueTask.FromResult(false);
}

public static class LocalPlanDraftWriteEndpoint
{
    public static async Task<IResult> Append(ClaimsPrincipal user, IConfiguration configuration,
        ILocalPlanDraftWriteAuthorization authorization, string centerId,
        LocalPlanDraftRevisionRequest? request, CancellationToken cancellationToken)
    {
        if (user.Identity?.IsAuthenticated != true)
            return Results.Problem(statusCode: 401, title: "Authentication required");
        var subject = user.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(subject))
            return Results.Problem(statusCode: 403, title: "Subject claim required");
        if (string.IsNullOrWhiteSpace(centerId) || request is null || request.DraftId == Guid.Empty ||
            request.ExpectedRevision < 0 || string.IsNullOrWhiteSpace(request.ProgramId) ||
            request.NationalVersion <= 0 || string.IsNullOrWhiteSpace(request.LocalTitle) ||
            request.LocalActivities is null || request.LocalActivities.Any(a => string.IsNullOrWhiteSpace(a)))
            return Results.Problem(statusCode: 400, title: "Invalid local draft revision request");

        // Must be authorized BEFORE storage access. Membership is never an operation grant.
        bool allowed;
        try
        {
            allowed = await authorization.IsAuthorizedAsync(user, centerId, request.DraftId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            allowed = false;
        }
        if (!allowed)
            return Results.Problem(statusCode: 403, title: "Local draft write permission not granted");

        var connectionString = configuration["CENTER_DRAFT_WRITE_DATABASE_URL"];
        if (string.IsNullOrWhiteSpace(connectionString))
            return Results.Problem(statusCode: 503, title: "Draft write storage unavailable");

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            if (!await RestrictedDraftDatabaseRole.VerifyAsync(connection, transaction, cancellationToken))
                return Results.Problem(statusCode: 503, title: "Unsafe draft database role");

            // Live membership is additional evidence; it does not supply the missing grant.
            await using (var membership = new NpgsqlCommand(
                """
                SELECT EXISTS (
                  SELECT 1 FROM centers.memberships m
                  JOIN centers.registry c ON c.center_id=m.center_id
                  WHERE m.subject_id=@subject AND m.center_id=@center
                    AND m.is_active AND c.is_active
                )
                """, connection, transaction))
            {
                membership.Parameters.AddWithValue("subject", subject);
                membership.Parameters.AddWithValue("center", centerId);
                if (await membership.ExecuteScalarAsync(cancellationToken) is not true)
                    return Results.Problem(statusCode: 403, title: "Active center membership required");
            }
            // Actor is derived from the verified JWT, tenant is derived from the route after authorization.
            await using (var scope = new NpgsqlCommand(
                "SELECT set_config('kanoonyar.center_id', @center, true)", connection, transaction))
            {
                scope.Parameters.AddWithValue("center", centerId);
                await scope.ExecuteNonQueryAsync(cancellationToken);
            }
            await using var command = new NpgsqlCommand(
                """
                SELECT programs.append_local_plan_draft_revision(
                  @center, @draft, @expected, @program, @national_version,
                  @title, @activities, @actor, @audit, @event, @correlation)
                """, connection, transaction);
            command.Parameters.AddWithValue("center", centerId);
            command.Parameters.AddWithValue("draft", request.DraftId);
            command.Parameters.AddWithValue("expected", request.ExpectedRevision);
            command.Parameters.AddWithValue("program", request.ProgramId);
            command.Parameters.AddWithValue("national_version", request.NationalVersion);
            command.Parameters.AddWithValue("title", request.LocalTitle);
            command.Parameters.AddWithValue("activities", NpgsqlDbType.Jsonb,
                JsonSerializer.Serialize(request.LocalActivities));
            command.Parameters.AddWithValue("actor", subject);
            command.Parameters.AddWithValue("audit", Guid.NewGuid());
            command.Parameters.AddWithValue("event", Guid.NewGuid());
            command.Parameters.AddWithValue("correlation", Guid.NewGuid().ToString("N"));
            var revision = (long)(await command.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("Missing persisted revision"));
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(new { draftId = request.DraftId, revision,
                programId = request.ProgramId, nationalVersion = request.NationalVersion });
        }
        catch (PostgresException exception) when (exception.SqlState is "40001" or "23505" or "23514" or "23503")
        {
            return Results.Problem(statusCode: 409, title: "Draft revision conflict or invalid national version binding");
        }
        catch (PostgresException exception) when (exception.SqlState == "22023")
        {
            return Results.Problem(statusCode: 400, title: "Invalid draft revision command");
        }
        catch (NpgsqlException)
        {
            return Results.Problem(statusCode: 503, title: "Draft write storage unavailable");
        }
        catch (TimeoutException)
        {
            return Results.Problem(statusCode: 503, title: "Draft write storage unavailable");
        }
    }
}
