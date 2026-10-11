using Npgsql;

namespace Kanoonyar.Api.Programs;

/// <summary>Published national definitions only; no unpublished draft exposure or write authority.</summary>
public static class NationalProgramReadEndpoint
{
    public static async Task<IResult> ListPublished(IConfiguration configuration, CancellationToken cancellationToken)
    {
        var connectionString = configuration["PROGRAM_READ_DATABASE_URL"];
        if (string.IsNullOrWhiteSpace(connectionString))
            return Results.Problem(statusCode: 503, title: "National program catalog unavailable");
        try
        {
            var programs = new List<object>();
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                """
                SELECT v.program_id, v.version, v.title, v.national_objectives::text,
                       v.mandatory_requirements::text
                FROM programs.national_program_publications AS p
                JOIN programs.national_program_versions AS v
                  ON v.program_id = p.program_id AND v.version = p.version
                ORDER BY v.program_id, v.version
                LIMIT 500
                """, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                programs.Add(new { programId = reader.GetString(0), version = reader.GetInt32(1),
                    title = reader.GetString(2), nationalObjectives = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(reader.GetString(3)),
                    mandatoryRequirements = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(reader.GetString(4)) });
            return Results.Ok(new { programs });
        }
        catch (NpgsqlException)
        {
            return Results.Problem(statusCode: 503, title: "National program catalog unavailable");
        }
        catch (TimeoutException)
        {
            return Results.Problem(statusCode: 503, title: "National program catalog unavailable");
        }
    }
}
