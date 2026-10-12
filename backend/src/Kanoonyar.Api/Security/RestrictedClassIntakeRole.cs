using Npgsql;
using Kanoonyar.Api.Security;
namespace Kanoonyar.Api.Security;

/// <summary>Dedicated EXECUTE-only class capacity/request login, never human authorization.</summary>
public static class RestrictedClassIntakeRole
{
    /// <summary>Reader must see class-owned intake rows but have no intake mutation power.</summary>
    public static async Task<bool> VerifyReadOnlyAsync(NpgsqlConnection db,
        NpgsqlTransaction tx,CancellationToken ct)
    {
        if(!await RestrictedDraftDatabaseRole.VerifyReadOnlyAsync(db,tx,ct))return false;
        await using var check=new NpgsqlCommand(
            """
            SELECT
              has_table_privilege(current_user,'learning.class_capacity_revisions'::regclass,'SELECT')
              AND has_table_privilege(current_user,'learning.class_registration_intents'::regclass,'SELECT')
              AND NOT(
                 has_table_privilege(current_user,'learning.class_capacity_revisions'::regclass,'INSERT')
                 OR has_table_privilege(current_user,'learning.class_capacity_revisions'::regclass,'UPDATE')
                 OR has_table_privilege(current_user,'learning.class_capacity_revisions'::regclass,'DELETE')
                 OR has_table_privilege(current_user,'learning.class_registration_intents'::regclass,'INSERT')
                 OR has_table_privilege(current_user,'learning.class_registration_intents'::regclass,'UPDATE')
                 OR has_table_privilege(current_user,'learning.class_registration_intents'::regclass,'DELETE')
                 OR has_function_privilege(current_user,
                   'learning.append_class_capacity_revision(text,uuid,bigint,integer,text,uuid,uuid,text)','EXECUTE')
                 OR has_function_privilege(current_user,
                   'learning.record_class_registration_intent(text,uuid,uuid,uuid,text,uuid,uuid,text)','EXECUTE')
                 OR EXISTS(SELECT 1 FROM pg_roles r WHERE r.rolname<>current_user
                     AND pg_has_role(current_user,r.oid,'SET') AND(
                       r.rolsuper OR r.rolbypassrls
                       OR has_table_privilege(r.oid,'learning.class_capacity_revisions'::regclass,'INSERT')
                       OR has_table_privilege(r.oid,'learning.class_registration_intents'::regclass,'INSERT')
                       OR has_function_privilege(r.oid,
                         'learning.record_class_registration_intent(text,uuid,uuid,uuid,text,uuid,uuid,text)','EXECUTE')))
              )
            """,db,tx);
        return await check.ExecuteScalarAsync(ct) is true;
    }

    public static async Task<bool> VerifyAsync(NpgsqlConnection db,NpgsqlTransaction tx,CancellationToken ct)
    {
        if(!await RestrictedDraftDatabaseRole.VerifyAsync(db,tx,ct))return false;
        await using var cmd=new NpgsqlCommand(
            """
            SELECT
              has_function_privilege(current_user,
               'learning.append_class_capacity_revision(text,uuid,bigint,integer,text,uuid,uuid,text)','EXECUTE')
              AND has_function_privilege(current_user,
               'learning.record_class_registration_intent(text,uuid,uuid,uuid,text,uuid,uuid,text)','EXECUTE')
              AND NOT(
                has_table_privilege(current_user,'learning.class_capacity_revisions'::regclass,'INSERT')
                OR has_table_privilege(current_user,'learning.class_capacity_revisions'::regclass,'UPDATE')
                OR has_table_privilege(current_user,'learning.class_capacity_revisions'::regclass,'DELETE')
                OR has_table_privilege(current_user,'learning.class_registration_intents'::regclass,'INSERT')
                OR has_table_privilege(current_user,'learning.class_registration_intents'::regclass,'UPDATE')
                OR has_table_privilege(current_user,'learning.class_registration_intents'::regclass,'DELETE')
                OR has_table_privilege(current_user,'governance.command_audit'::regclass,'INSERT')
                OR has_table_privilege(current_user,'governance.outbox_events'::regclass,'INSERT')
                OR has_table_privilege(current_user,'centers.memberships'::regclass,'UPDATE')
                OR has_function_privilege(current_user,
                   'learning.record_class_plan(text,uuid,text,text,uuid,uuid,text)','EXECUTE')
                OR has_function_privilege(current_user,
                   'programs.append_local_plan_draft_revision(text,uuid,bigint,text,integer,text,jsonb,text,uuid,uuid,text)','EXECUTE')
                OR EXISTS (SELECT 1 FROM pg_roles r
                  WHERE r.rolname<>current_user AND pg_has_role(current_user,r.oid,'SET')
                   AND (r.rolsuper OR r.rolbypassrls OR r.rolcreaterole OR r.rolcreatedb OR r.rolreplication
                    OR has_table_privilege(r.oid,'learning.class_capacity_revisions'::regclass,'INSERT')
                    OR has_table_privilege(r.oid,'learning.class_registration_intents'::regclass,'INSERT')
                    OR has_table_privilege(r.oid,'governance.command_audit'::regclass,'INSERT')
                    OR has_table_privilege(r.oid,'centers.memberships'::regclass,'UPDATE')))
              )
            """,db,tx);
        return await cmd.ExecuteScalarAsync(ct) is true;
    }
}
