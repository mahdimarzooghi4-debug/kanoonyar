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
                AND NOT active_role.rolcreaterole AND NOT active_role.rolcreatedb
                AND NOT active_role.rolreplication
                AND NOT login_role.rolsuper AND NOT login_role.rolbypassrls
                AND NOT login_role.rolcreaterole AND NOT login_role.rolcreatedb
                AND NOT login_role.rolreplication AND login_role.rolcanlogin
                AND current_user=session_user
                AND draft.relowner <> active_role.oid
                AND membership.relowner <> active_role.oid
                AND draft.relrowsecurity AND draft.relforcerowsecurity
                AND membership.relrowsecurity AND membership.relforcerowsecurity
              FROM pg_roles active_role
              CROSS JOIN pg_roles login_role
              CROSS JOIN pg_class draft
              CROSS JOIN pg_class membership
              WHERE active_role.rolname=current_user AND login_role.rolname=session_user
                AND draft.oid='programs.center_plan_draft_revisions'::regclass
                AND membership.oid='centers.memberships'::regclass
            ), false)
            """, connection, transaction);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }
}
