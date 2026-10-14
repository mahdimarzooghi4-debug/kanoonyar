using System.Globalization;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

/// <summary>
/// Only original immutable published versions, paged by the original composite
/// (program_id, version) key. No center drafts, private issuance or user data.
/// No offset pagination and no invented "total count" from a partial response.
/// </summary>
public static class NationalProgramReadEndpoint
{
    private const int PageSize = 500;
    private sealed record Published(string ProgramId, int Version, string Title,
        JsonElement NationalObjectives, JsonElement MandatoryRequirements);
    private sealed record Cursor(string AfterProgramId, int AfterVersion);

    // Backwards-compatible direct handler contract used by existing tests.
    public static Task<IResult> ListPublished(IConfiguration configuration,
        CancellationToken cancellationToken) =>
        ReadPage(configuration, null, cancellationToken);

    public static Task<IResult> ListPublishedPage(HttpContext context,
        IConfiguration configuration, CancellationToken cancellationToken)
    {
        var q = context.Request.Query;
        var hasId = q.ContainsKey("afterProgramId");
        var hasVersion = q.ContainsKey("afterVersion");
        if (hasId != hasVersion) return Task.FromResult<IResult>(Results.BadRequest());
        if (!hasId) return ReadPage(configuration, null, cancellationToken);
        if (q["afterProgramId"].Count != 1 || q["afterVersion"].Count != 1)
            return Task.FromResult<IResult>(Results.BadRequest());

        var id = q["afterProgramId"].ToString();
        var versionText = q["afterVersion"].ToString();
        if (string.IsNullOrWhiteSpace(id) || id != id.Trim() || id.Length > 256 ||
            id.Any(char.IsControl) ||
            !int.TryParse(versionText, NumberStyles.None, CultureInfo.InvariantCulture,
                out var version) || version <= 0 ||
            versionText != version.ToString(CultureInfo.InvariantCulture))
            return Task.FromResult<IResult>(Results.BadRequest());
        return ReadPage(configuration, new Cursor(id, version), cancellationToken);
    }

    /// <summary>
    /// Source-pinned public version lookup for a shareable audience permalink.
    /// This is NOT a general program-definition read: publication is mandatory.
    /// Unpublished, nonexistent and inaccessible versions are indistinguishable (404).
    /// </summary>
    public static async Task<IResult> ReadPublishedVersion(HttpContext context,
        IConfiguration configuration, CancellationToken cancellationToken)
    {
        var query = context.Request.Query;
        if (query.Count != 2 || query["programId"].Count != 1 ||
            query["version"].Count != 1)
            return Results.BadRequest();
        var id = query["programId"].ToString();
        var rawVersion = query["version"].ToString();
        if (string.IsNullOrWhiteSpace(id) || id != id.Trim() ||
            id.Length > 256 || id.Any(char.IsControl) ||
            !int.TryParse(rawVersion, NumberStyles.None, CultureInfo.InvariantCulture,
                out var version) || version <= 0 ||
            rawVersion != version.ToString(CultureInfo.InvariantCulture))
            return Results.BadRequest();

        var url = configuration["PROGRAM_READ_DATABASE_URL"];
        if (string.IsNullOrWhiteSpace(url))
            return Results.Problem(statusCode: 503, title: "Published program version unavailable");
        try
        {
            await using var connection = new NpgsqlConnection(url);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                """
                SELECT v.program_id,v.version,v.title,v.national_objectives::text,
                  v.mandatory_requirements::text
                FROM programs.national_program_publications AS p
                INNER JOIN programs.national_program_versions AS v
                  ON v.program_id=p.program_id AND v.version=p.version
                WHERE p.program_id=@program_id AND p.version=@version
                LIMIT 1
                """, connection);
            command.Parameters.Add("program_id", NpgsqlDbType.Text).Value = id;
            command.Parameters.Add("version", NpgsqlDbType.Integer).Value = version;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return Results.NotFound();
            return Results.Ok(new Published(reader.GetString(0), reader.GetInt32(1),
                reader.GetString(2),
                JsonSerializer.Deserialize<JsonElement>(reader.GetString(3)),
                JsonSerializer.Deserialize<JsonElement>(reader.GetString(4))));
        }
        catch (NpgsqlException)
        {
            return Results.Problem(statusCode: 503, title: "Published program version unavailable");
        }
        catch (TimeoutException)
        {
            return Results.Problem(statusCode: 503, title: "Published program version unavailable");
        }
    }

    private static async Task<IResult> ReadPage(IConfiguration configuration, Cursor? cursor,
        CancellationToken cancellationToken)
    {
        var connectionString = configuration["PROGRAM_READ_DATABASE_URL"];
        if (string.IsNullOrWhiteSpace(connectionString))
            return Results.Problem(statusCode: 503, title: "National program catalog unavailable");

        try
        {
            var rows = new List<Published>(PageSize + 1);
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            // Both variants are fixed SQL. No cursor value is interpolated.
            var sql = cursor is null
                ? """
                  SELECT v.program_id,v.version,v.title,v.national_objectives::text,
                    v.mandatory_requirements::text
                  FROM programs.national_program_publications AS p
                  JOIN programs.national_program_versions AS v
                    ON v.program_id=p.program_id AND v.version=p.version
                  ORDER BY v.program_id,v.version LIMIT 501
                  """
                : """
                  SELECT v.program_id,v.version,v.title,v.national_objectives::text,
                    v.mandatory_requirements::text
                  FROM programs.national_program_publications AS p
                  JOIN programs.national_program_versions AS v
                    ON v.program_id=p.program_id AND v.version=p.version
                  WHERE (v.program_id,v.version) > (@program_id,@version)
                  ORDER BY v.program_id,v.version LIMIT 501
                  """;
            await using var command = new NpgsqlCommand(sql, connection);
            if (cursor is not null)
            {
                command.Parameters.Add("program_id", NpgsqlDbType.Text).Value = cursor.AfterProgramId;
                command.Parameters.Add("version", NpgsqlDbType.Integer).Value = cursor.AfterVersion;
            }
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                rows.Add(new Published(reader.GetString(0), reader.GetInt32(1),
                    reader.GetString(2), JsonSerializer.Deserialize<JsonElement>(reader.GetString(3)),
                    JsonSerializer.Deserialize<JsonElement>(reader.GetString(4))));
            var hasMore = rows.Count > PageSize;
            var page = hasMore ? rows.Take(PageSize).ToArray() : rows.ToArray();
            Cursor? next = hasMore
                ? new Cursor(page[^1].ProgramId, page[^1].Version) : null;
            return Results.Ok(new { programs = page, nextCursor = next });
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
