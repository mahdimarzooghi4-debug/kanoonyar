using Npgsql;
namespace Kanoonyar.Api.Security;
public static class RestrictedWorkSessionLinkRole
{
 private const string Fn="operations.append_work_card_session_link(text,uuid,uuid,text,uuid,uuid,text)";
 public static async Task<bool> WriterAsync(NpgsqlConnection db,NpgsqlTransaction tx,CancellationToken ct)
 {
  if(!await RestrictedDraftDatabaseRole.VerifyAsync(db,tx,ct))return false;
  await using var q=new NpgsqlCommand("""
   SELECT has_function_privilege(current_user,
    'operations.append_work_card_session_link(text,uuid,uuid,text,uuid,uuid,text)','EXECUTE')
    AND NOT (
     has_table_privilege(current_user,'operations.work_card_session_links'::regclass,'INSERT')
     OR has_table_privilege(current_user,'operations.work_card_session_links'::regclass,'UPDATE')
     OR has_table_privilege(current_user,'learning.class_sessions'::regclass,'INSERT')
     OR has_table_privilege(current_user,'governance.command_audit'::regclass,'INSERT')
     OR has_table_privilege(current_user,'governance.outbox_events'::regclass,'INSERT')
     OR EXISTS(SELECT 1 FROM pg_roles r WHERE r.rolname<>current_user
      AND pg_has_role(current_user,r.oid,'SET') AND (
       r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
       OR has_table_privilege(r.oid,'operations.work_card_session_links'::regclass,'INSERT')
       OR has_function_privilege(r.oid,
       'operations.append_work_card_session_link(text,uuid,uuid,text,uuid,uuid,text)','EXECUTE')))
    )
   """,db,tx);
  return await q.ExecuteScalarAsync(ct) is true;
 }
 public static async Task<bool> ReaderAsync(NpgsqlConnection db,NpgsqlTransaction tx,CancellationToken ct)
 {
  if(!await RestrictedCenterWorkCardRole.ReaderAsync(db,tx,ct))return false;
  await using var q=new NpgsqlCommand("""
   SELECT has_table_privilege(current_user,'operations.work_card_session_links'::regclass,'SELECT')
    AND has_table_privilege(current_user,'learning.class_sessions'::regclass,'SELECT')
    AND has_table_privilege(current_user,'learning.center_classes'::regclass,'SELECT')
    AND NOT (
     has_table_privilege(current_user,'operations.work_card_session_links'::regclass,'INSERT')
     OR has_table_privilege(current_user,'operations.work_card_session_links'::regclass,'UPDATE')
     OR has_table_privilege(current_user,'operations.work_card_session_links'::regclass,'DELETE')
     OR has_function_privilege(current_user,
       'operations.append_work_card_session_link(text,uuid,uuid,text,uuid,uuid,text)','EXECUTE')
     OR EXISTS(SELECT 1 FROM pg_roles r WHERE r.rolname<>current_user
      AND pg_has_role(current_user,r.oid,'SET') AND (
       r.rolsuper OR r.rolbypassrls
       OR has_table_privilege(r.oid,'operations.work_card_session_links'::regclass,'INSERT')
       OR has_function_privilege(r.oid,
        'operations.append_work_card_session_link(text,uuid,uuid,text,uuid,uuid,text)','EXECUTE')))
    )
   """,db,tx);
  return await q.ExecuteScalarAsync(ct) is true;
 }
}
