using Npgsql;

namespace Kanoonyar.Api.Security;

/// <summary>SQL login guard, not a replacement for authenticated operation grants.</summary>
public static class RestrictedLearningCommandRole
{
    public static async Task<bool> VerifyAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, CancellationToken ct)
    {
        if (!await RestrictedDraftDatabaseRole.VerifyAsync(connection,tx,ct))
            return false;
        await using var command=new NpgsqlCommand(
            """
            SELECT has_function_privilege(current_user,
              'learning.record_class_plan(text,uuid,text,text,uuid,uuid,text)','EXECUTE')
            AND has_function_privilege(current_user,
              'learning.record_class_session_plan(text,uuid,uuid,text,timestamptz,text,uuid,uuid,text)','EXECUTE')
            AND NOT (
              has_table_privilege(current_user,'learning.center_classes'::regclass,'INSERT')
              OR has_table_privilege(current_user,'learning.center_classes'::regclass,'UPDATE')
              OR has_table_privilege(current_user,'learning.center_classes'::regclass,'DELETE')
              OR has_table_privilege(current_user,'learning.class_sessions'::regclass,'INSERT')
              OR has_table_privilege(current_user,'learning.class_sessions'::regclass,'UPDATE')
              OR has_table_privilege(current_user,'learning.class_sessions'::regclass,'DELETE')
              OR has_table_privilege(current_user,'centers.memberships'::regclass,'INSERT')
              OR has_table_privilege(current_user,'centers.memberships'::regclass,'UPDATE')
              OR has_table_privilege(current_user,'centers.memberships'::regclass,'DELETE')
              OR has_table_privilege(current_user,
                (SELECT c.oid FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                 WHERE n.nspname='governance' AND c.relname='command_audit'),'INSERT')
              OR has_table_privilege(current_user,
                (SELECT c.oid FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                 WHERE n.nspname='governance' AND c.relname='outbox_events'),'INSERT')
              OR has_function_privilege(current_user,
                'programs.append_local_plan_draft_revision(text,uuid,bigint,text,integer,text,jsonb,text,uuid,uuid,text)',
                'EXECUTE')
              OR has_function_privilege(current_user,
                'programs.record_center_sourced_activity_report(uuid,text,uuid,uuid,text,uuid,text,text,timestamptz,text,uuid,uuid,text)',
                'EXECUTE')
              OR EXISTS (
                SELECT 1 FROM pg_roles candidate
                WHERE candidate.rolname<>current_user
                  AND pg_has_role(current_user,candidate.oid,'SET')
                  AND (
                    candidate.rolsuper OR candidate.rolbypassrls OR
                    candidate.rolcreaterole OR candidate.rolcreatedb OR candidate.rolreplication
                    OR candidate.oid=(SELECT relowner FROM pg_class
                        WHERE oid='learning.center_classes'::regclass)
                    OR candidate.oid=(SELECT relowner FROM pg_class
                        WHERE oid='learning.class_sessions'::regclass)
                    OR has_table_privilege(candidate.oid,'learning.center_classes'::regclass,'INSERT')
                    OR has_table_privilege(candidate.oid,'learning.class_sessions'::regclass,'INSERT')
                    OR has_table_privilege(candidate.oid,'centers.memberships'::regclass,'UPDATE')
                    OR has_function_privilege(candidate.oid,
                      'programs.append_local_plan_draft_revision(text,uuid,bigint,text,integer,text,jsonb,text,uuid,uuid,text)',
                      'EXECUTE')
                  )
              )
            )
            """,connection,tx);
        return await command.ExecuteScalarAsync(ct) is true;
    }
}
