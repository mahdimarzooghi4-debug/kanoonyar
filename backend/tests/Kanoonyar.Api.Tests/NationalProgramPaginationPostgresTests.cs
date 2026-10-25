using System.Globalization;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Kanoonyar.Api.Tests;

/// <summary>Actual S04 published-only pagination through independent SQL SELECT grants.</summary>
public sealed class NationalProgramPaginationPostgresTests
{
    private static string? AdminUrl => Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static async Task<JsonElement> Page(IConfiguration config, string? cursorId = null,
        int? cursorVersion = null)
    {
        var http = new DefaultHttpContext();
        if (cursorId is not null && cursorVersion.HasValue)
            http.Request.QueryString = new QueryString("?afterProgramId=" +
                Uri.EscapeDataString(cursorId) + "&afterVersion=" +
                cursorVersion.Value.ToString(CultureInfo.InvariantCulture));
        var result = await NationalProgramReadEndpoint.ListPublishedPage(
            http, config, CancellationToken.None);
        Assert.Equal(200, (result as IStatusCodeHttpResult)?.StatusCode);
        var value = Assert.IsAssignableFrom<IValueHttpResult>(result).Value;
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(value, JsonOptions));
        return json.RootElement.Clone();
    }

    [Theory]
    [InlineData("?afterProgramId=alpha")]
    [InlineData("?afterVersion=1")]
    [InlineData("?afterProgramId=alpha&afterVersion=0")]
    [InlineData("?afterProgramId=alpha&afterVersion=-1")]
    [InlineData("?afterProgramId=alpha&afterVersion=01")]
    [InlineData("?afterProgramId=alpha&afterVersion=999999999999999")]
    [InlineData("?afterProgramId=%20&afterVersion=1")]
    [InlineData("?afterProgramId=alpha&afterProgramId=beta&afterVersion=1")]
    [InlineData("?afterProgramId=alpha&afterVersion=1&afterVersion=2")]
    public async Task Malformed_cursor_is_bad_request_not_a_query_or_partial_result(string query)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(query);
        var reply = await NationalProgramReadEndpoint.ListPublishedPage(context,
            new ConfigurationBuilder().Build(), CancellationToken.None);
        Assert.Equal(400, (reply as IStatusCodeHttpResult)?.StatusCode);
    }

    [Fact]
    public async Task Over_five_hundred_genuine_published_versions_are_exhaustively_paged_by_original_key()
    {
        var required = Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_REQUIRED") == "1";
        if (required) Assert.False(string.IsNullOrWhiteSpace(AdminUrl));
        if (string.IsNullOrWhiteSpace(AdminUrl)) return;

        var id = "zzzz-s56-" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(AdminUrl))
        {
            await admin.OpenAsync();
            await using var tx = await admin.BeginTransactionAsync();
            await using var data = new NpgsqlCommand(
                """
                INSERT INTO programs.national_program_versions
                    (program_id,version,title,national_objectives,mandatory_requirements,
                     created_by_subject,created_at)
                SELECT @id, s, 'Synthetic published version '||s,
                    '["synthetic goal"]'::jsonb, '["synthetic mandatory"]'::jsonb,
                    's56-ci-fixture', now()
                FROM generate_series(1,502) AS s;
                INSERT INTO programs.national_program_publications
                    (program_id,version,publisher_subject,published_at)
                SELECT @id, s, 's56-ci-fixture', now()
                FROM generate_series(1,502) AS s;
                -- Deliberately NOT published: it must NEVER be exposed by pages.
                INSERT INTO programs.national_program_versions
                    (program_id,version,title,national_objectives,mandatory_requirements,
                     created_by_subject,created_at)
                VALUES (@id,503,'PRIVATE UNPUBLISHED VERSION',
                    '["should never appear"]'::jsonb,'[]'::jsonb,'s56-ci-fixture',now());
                """, admin, tx);
            data.Parameters.AddWithValue("id", id);
            await data.ExecuteNonQueryAsync();
            await using var grant = new NpgsqlCommand(
                """
                DO $role$ BEGIN
                  IF NOT EXISTS (SELECT 1 FROM pg_roles
                    WHERE rolname='kanoonyar_public_catalog_s56_ci') THEN
                    CREATE ROLE kanoonyar_public_catalog_s56_ci LOGIN NOINHERIT
                        NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION;
                  END IF;
                END;$role$;
                GRANT CONNECT ON DATABASE kanoonyar_draft_api_ci
                  TO kanoonyar_public_catalog_s56_ci;
                GRANT USAGE ON SCHEMA programs TO kanoonyar_public_catalog_s56_ci;
                GRANT SELECT ON programs.national_program_versions,
                   programs.national_program_publications TO kanoonyar_public_catalog_s56_ci;
                """, admin, tx);
            await grant.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }

        var readBuilder = new NpgsqlConnectionStringBuilder(AdminUrl)
        {
            Username = "kanoonyar_public_catalog_s56_ci", Pooling = false
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["PROGRAM_READ_DATABASE_URL"] = readBuilder.ConnectionString }
            ).Build();
        await using (var read = new NpgsqlConnection(readBuilder.ConnectionString))
        {
            await read.OpenAsync();
            await using var restricted = new NpgsqlCommand(
                """
                SELECT has_table_privilege(current_user,
                  'programs.national_program_versions','INSERT')
                OR has_table_privilege(current_user,
                  'programs.national_program_publications','INSERT')
                OR (SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname=current_user)
                """, read);
            Assert.False((bool)(await restricted.ExecuteScalarAsync())!);
        }

        var distinct = new HashSet<string>(StringComparer.Ordinal);
        var versions = new List<int>();
        var pageCount = 0;
        string? afterId = null;
        int? afterVersion = null;
        do
        {
            var response = await Page(config, afterId, afterVersion);
            pageCount++;
            Assert.True(pageCount <= 3, "Pagination loop may not repeat the same cursor");
            var rows = response.GetProperty("programs").EnumerateArray().ToArray();
            Assert.InRange(rows.Length, 1, 500);
            foreach (var row in rows)
            {
                var rowId = row.GetProperty("programId").GetString()!;
                var version = row.GetProperty("version").GetInt32();
                Assert.True(distinct.Add(rowId + ":" + version));
                Assert.NotEqual("PRIVATE UNPUBLISHED VERSION",row.GetProperty("title").GetString());
                if (rowId == id) versions.Add(version);
            }
            var next = response.GetProperty("nextCursor");
            if (next.ValueKind == JsonValueKind.Null)
            {
                afterId = null;
                afterVersion = null;
                break;
            }
            Assert.Equal(rows[^1].GetProperty("programId").GetString(),
                next.GetProperty("afterProgramId").GetString());
            Assert.Equal(rows[^1].GetProperty("version").GetInt32(),
                next.GetProperty("afterVersion").GetInt32());
            afterId = next.GetProperty("afterProgramId").GetString();
            afterVersion = next.GetProperty("afterVersion").GetInt32();
        } while (afterId is not null);
        Assert.Equal(2,pageCount);
        Assert.Equal(502,versions.Count);
        Assert.Equal(Enumerable.Range(1,502),versions);
        Assert.DoesNotContain(503,versions);
    }
}
