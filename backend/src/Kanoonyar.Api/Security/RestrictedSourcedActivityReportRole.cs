using Npgsql;

namespace Kanoonyar.Api.Security;

/// <summary>Only a restricted SQL login may execute the one audited typed-source command.</summary>
public static class RestrictedSourcedActivityReportRole
{
    public static async Task<bool> VerifyAsync(NpgsqlConnection db,
        NpgsqlTransaction transaction, CancellationToken ct)
    {
        if (!await RestrictedDraftDatabaseRole.VerifyAsync(db,transaction,ct))
            return false;
        await using var cmd = new NpgsqlCommand(
            """
            SELECT has_function_privilege(current_user,
             'programs.record_center_sourced_activity_command(text,uuid,uuid,uuid,text,uuid,text,text,timestamptz,text,uuid,uuid,text)',
             'EXECUTE')
            AND NOT (
              has_table_privilege(current_user,'programs.center_activity_reports'::regclass,'INSERT')
              OR has_table_privilege(current_user,'programs.center_activity_reports'::regclass,'UPDATE')
              OR has_table_privilege(current_user,'programs.center_activity_reports'::regclass,'DELETE')
              OR has_table_privilege(current_user,'programs.activity_report_source_bindings'::regclass,'INSERT')
              OR has_table_privilege(current_user,'programs.activity_report_source_bindings'::regclass,'UPDATE')
              OR has_table_privilege(current_user,'programs.activity_report_source_bindings'::regclass,'DELETE')
              OR has_table_privilege(current_user,'governance.command_audit'::regclass,'INSERT')
              OR has_table_privilege(current_user,'governance.outbox_events'::regclass,'INSERT')
              OR has_table_privilege(current_user,'centers.memberships'::regclass,'INSERT')
              OR has_table_privilege(current_user,'centers.memberships'::regclass,'UPDATE')
              OR has_table_privilege(current_user,'centers.memberships'::regclass,'DELETE')
              OR has_table_privilege(current_user,'learning.class_sessions'::regclass,'INSERT')
              OR has_table_privilege(current_user,'events.center_events'::regclass,'INSERT')
              OR has_function_privilege(current_user,
               'programs.record_center_activity_report(uuid,text,uuid,uuid,text,text,text,text,timestamptz,text,uuid,uuid,text)','EXECUTE')
              OR has_function_privilege(current_user,
               'programs.record_center_sourced_activity_report(uuid,text,uuid,uuid,text,uuid,text,text,timestamptz,text,uuid,uuid,text)','EXECUTE')
              OR has_function_privilege(current_user,
               'programs.append_local_plan_draft_revision(text,uuid,bigint,text,integer,text,jsonb,text,uuid,uuid,text)','EXECUTE')
              OR has_function_privilege(current_user,
               'learning.record_class_plan(text,uuid,text,text,uuid,uuid,text)','EXECUTE')
              OR EXISTS (
                 SELECT 1 FROM pg_roles r
                 WHERE r.rolname<>current_user AND pg_has_role(current_user,r.oid,'SET')
                   AND (r.rolsuper OR r.rolbypassrls OR r.rolcreaterole OR r.rolcreatedb
                     OR r.rolreplication
                     OR has_table_privilege(r.oid,'programs.center_activity_reports'::regclass,'INSERT')
                     OR has_table_privilege(r.oid,'programs.activity_report_source_bindings'::regclass,'INSERT')
                     OR has_table_privilege(r.oid,'governance.command_audit'::regclass,'INSERT')
                     OR has_table_privilege(r.oid,'centers.memberships'::regclass,'UPDATE')
                     OR has_function_privilege(r.oid,
                       'programs.record_center_sourced_activity_report(uuid,text,uuid,uuid,text,uuid,text,text,timestamptz,text,uuid,uuid,text)','EXECUTE')
                    )
              )
            )
            """,db,transaction);
        return await cmd.ExecuteScalarAsync(ct) is true;
    }
}
