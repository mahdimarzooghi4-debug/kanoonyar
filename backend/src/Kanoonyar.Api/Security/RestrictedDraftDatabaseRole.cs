using Npgsql;

namespace Kanoonyar.Api.Security;

/// <summary>Defense in depth: refuse privileged DB sessions. RLS alone is not user authorization.</summary>
public static class RestrictedDraftDatabaseRole
{
    /// <summary>
    /// Read connections must not be able to mutate draft, membership, audit or outbox
    /// data, execute the draft command, or SET ROLE to an owning/privileged role.
    /// This is a DB-capability check, not a user authorization or GUC trust proof.
    /// </summary>
    /// <summary>Command login must ONLY execute the audited append function, never directly
    /// mutate draft/audit/outbox tables or SET ROLE to a stronger identity.</summary>
    public static async Task<bool> VerifyWriteCommandOnlyAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        if (!await VerifyAsync(connection, transaction, cancellationToken))
            return false;
        await using var command = new NpgsqlCommand(
            """
            SELECT
              has_function_privilege(current_user,
                'programs.append_local_plan_draft_revision(text,uuid,bigint,text,integer,text,jsonb,text,uuid,uuid,text)',
                'EXECUTE')
              AND NOT (
                has_table_privilege(current_user, 'programs.center_plan_draft_revisions'::regclass, 'INSERT')
                OR has_table_privilege(current_user, 'programs.center_plan_draft_revisions'::regclass, 'UPDATE')
                OR has_table_privilege(current_user, 'programs.center_plan_draft_revisions'::regclass, 'DELETE')
                OR has_table_privilege(current_user, 'programs.center_plan_draft_revisions'::regclass, 'SELECT')
                OR has_table_privilege(current_user,
                    (SELECT c.oid FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                     WHERE n.nspname='governance' AND c.relname='command_audit'), 'INSERT')
                OR has_table_privilege(current_user,
                    (SELECT c.oid FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                     WHERE n.nspname='governance' AND c.relname='outbox_events'), 'INSERT')
                OR has_table_privilege(current_user, 'centers.memberships'::regclass, 'INSERT')
                OR has_table_privilege(current_user, 'centers.memberships'::regclass, 'UPDATE')
                OR has_table_privilege(current_user, 'centers.memberships'::regclass, 'DELETE')
                OR EXISTS (
                  SELECT 1 FROM pg_roles candidate
                  WHERE candidate.rolname <> current_user
                    AND pg_has_role(current_user, candidate.oid, 'SET')
                    AND (
                      candidate.rolsuper OR candidate.rolbypassrls
                      OR candidate.rolcreaterole OR candidate.rolcreatedb
                      OR candidate.rolreplication
                      OR candidate.oid=(SELECT relowner FROM pg_class
                        WHERE oid='programs.center_plan_draft_revisions'::regclass)
                      OR candidate.oid=(SELECT relowner FROM pg_class
                        WHERE oid='centers.memberships'::regclass)
                      OR has_table_privilege(candidate.oid,
                        'programs.center_plan_draft_revisions'::regclass,'INSERT')
                    )
                )
              )
            """, connection, transaction);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    public static async Task<bool> VerifyReadOnlyAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        if (!await VerifyAsync(connection, transaction, cancellationToken))
            return false;

        await using var check = new NpgsqlCommand(
            """
            SELECT NOT (
              has_table_privilege(current_user, 'programs.center_plan_draft_revisions', 'INSERT')
              OR has_table_privilege(current_user, 'programs.center_plan_draft_revisions', 'UPDATE')
              OR has_table_privilege(current_user, 'programs.center_plan_draft_revisions', 'DELETE')
              OR has_table_privilege(current_user,
                   'programs.center_plan_issuance_links'::regclass, 'INSERT')
              OR has_table_privilege(current_user,
                   'programs.center_plan_issuance_links'::regclass, 'UPDATE')
              OR has_table_privilege(current_user,
                   'programs.center_plan_issuance_links'::regclass, 'DELETE')
              OR has_table_privilege(current_user, 'centers.memberships', 'INSERT')
              OR has_table_privilege(current_user, 'centers.memberships', 'UPDATE')
              OR has_table_privilege(current_user, 'centers.memberships', 'DELETE')
              OR has_table_privilege(current_user, (SELECT cls.oid FROM pg_class cls JOIN pg_namespace ns ON ns.oid = cls.relnamespace
                 WHERE ns.nspname = 'governance' AND cls.relname = 'command_audit'), 'INSERT')
              OR has_table_privilege(current_user, (SELECT cls.oid FROM pg_class cls JOIN pg_namespace ns ON ns.oid = cls.relnamespace
                 WHERE ns.nspname = 'governance' AND cls.relname = 'outbox_events'), 'INSERT')
              OR has_function_privilege(current_user,
                'programs.append_local_plan_draft_revision(text,uuid,bigint,text,integer,text,jsonb,text,uuid,uuid,text)',
                'EXECUTE')
              OR pg_has_role(current_user,
                (SELECT relowner FROM pg_class
                 WHERE oid='programs.center_plan_draft_revisions'::regclass), 'SET')
              OR pg_has_role(current_user,
                (SELECT relowner FROM pg_class
                 WHERE oid='centers.memberships'::regclass), 'SET')
              -- A safe-looking login can still become a writer through SET ROLE.
              -- Check the complete effective SET graph, not merely the active role.
              OR EXISTS (
                SELECT 1 FROM pg_roles candidate
                WHERE candidate.rolname <> current_user
                  AND pg_has_role(current_user, candidate.oid, 'SET')
                  AND (
                    candidate.rolsuper OR candidate.rolbypassrls OR candidate.rolcreaterole
                    OR candidate.rolcreatedb OR candidate.rolreplication
                    OR candidate.oid=(SELECT relowner FROM pg_class
                                      WHERE oid='programs.center_plan_draft_revisions'::regclass)
                    OR candidate.oid=(SELECT relowner FROM pg_class
                                      WHERE oid='centers.memberships'::regclass)
                    OR has_table_privilege(candidate.oid,
                         'programs.center_plan_draft_revisions'::regclass, 'INSERT')
                    OR has_table_privilege(candidate.oid,
                         'centers.memberships'::regclass, 'UPDATE')
                    OR has_table_privilege(candidate.oid,
                         (SELECT cls.oid FROM pg_class cls JOIN pg_namespace ns
                          ON ns.oid=cls.relnamespace
                          WHERE ns.nspname='governance' AND cls.relname='command_audit'), 'INSERT')
                    OR has_table_privilege(candidate.oid,
                         (SELECT cls.oid FROM pg_class cls JOIN pg_namespace ns
                          ON ns.oid=cls.relnamespace
                          WHERE ns.nspname='governance' AND cls.relname='outbox_events'), 'INSERT')
                    OR has_function_privilege(candidate.oid,
                         'programs.append_local_plan_draft_revision(text,uuid,bigint,text,integer,text,jsonb,text,uuid,uuid,text)',
                         'EXECUTE')
                  )
              )
            )
            """, connection, transaction);
        return await check.ExecuteScalarAsync(cancellationToken) is true;
    }

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
