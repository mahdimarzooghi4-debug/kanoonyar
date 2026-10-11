using System.Security.Claims;
using Npgsql;

namespace Kanoonyar.Api.Security;

/// <summary>Read-only evidence; membership never implies an operational permission.</summary>
public static class CenterMembershipEndpoint
{
    public static async Task<IResult> GetMine(ClaimsPrincipal user, IConfiguration configuration, CancellationToken cancellationToken)
    {
        if (user.Identity?.IsAuthenticated != true)
            return Results.Problem(statusCode: 401, title: "Authentication required");
        var subject = user.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(subject))
            return Results.Problem(statusCode: 403, title: "Subject claim required");
        var connectionString = configuration["CENTER_READ_DATABASE_URL"];
        if (string.IsNullOrWhiteSpace(connectionString))
            return Results.Problem(statusCode: 503, title: "Center membership storage unavailable");

        var memberships = new List<object>();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT center_id, display_name, role_code FROM centers.list_active_memberships_for_subject(@subject)", connection);
        command.Parameters.AddWithValue("subject", subject);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            memberships.Add(new { centerId = reader.GetString(0), displayName = reader.GetString(1), roleCode = reader.GetString(2) });
        return Results.Ok(new { memberships });
    }
}
