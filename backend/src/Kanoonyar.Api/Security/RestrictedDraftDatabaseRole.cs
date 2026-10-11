using Npgsql;

namespace Kanoonyar.Api.Security;

/// <summary>Defense in depth: refuse privileged DB sessions. RLS alone is not user authorization.</summary>
public static class RestrictedDraftDatabaseRole
{
    public static async Task<bool> VerifyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT COALESCE((
              SELECT NOT active_role.rolsuper AND NOT active_role.rolbypassrls
                AND NOT login_role.rolsuper AND NOT login_role.rolbypassrls
                AND draft.relrowsecurity AND draft.relforcerowsecurity
              FROM pg_roles active_role
              CROSS JOIN pg_roles login_role
              CROSS JOIN pg_class draft
              WHERE active_role.rolname=current_user AND login_role.rolname=session_user
                AND draft.oid='programs.center_plan_draft_revisions'::regclass
            ), false)
            """, connection, transaction);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }
}
