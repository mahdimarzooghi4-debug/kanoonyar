using Npgsql;
namespace Kanoonyar.Api.Security;
/// <summary>Restricted SQL role checks supplement independent OIDC and operation grants.</summary>
public static class RestrictedSessionPreparationRole
{
 private const string FunctionName="learning.append_session_preparation(text,uuid,uuid,bigint,text,text,text,text,uuid,uuid,text)";
 public static async Task<bool> WriterAsync(NpgsqlConnection db,NpgsqlTransaction tx,CancellationToken ct)
 {
  if(!await RestrictedDraftDatabaseRole.VerifyAsync(db,tx,ct))return false;
  await using var cmd=new NpgsqlCommand("""
   SELECT has_function_privilege(current_user,
      'learning.append_session_preparation(text,uuid,uuid,bigint,text,text,text,text,uuid,uuid,text)','EXECUTE')
    AND NOT (
      has_table_privilege(current_user,'learning.session_preparations'::regclass,'INSERT')
      OR has_table_privilege(current_user,'learning.session_preparation_revisions'::regclass,'INSERT')
      OR has_table_privilege(current_user,'learning.session_preparation_revisions'::regclass,'UPDATE')
      OR has_table_privilege(current_user,'learning.class_sessions'::regclass,'INSERT')
      OR has_table_privilege(current_user,'governance.command_audit'::regclass,'INSERT')
      OR has_table_privilege(current_user,'governance.outbox_events'::regclass,'INSERT')
      OR EXISTS(SELECT 1 FROM pg_roles r WHERE r.rolname<>current_user
       AND pg_has_role(current_user,r.oid,'SET') AND (
        r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
        OR has_table_privilege(r.oid,'learning.session_preparation_revisions'::regclass,'INSERT')
        OR has_function_privilege(r.oid,
         'learning.append_session_preparation(text,uuid,uuid,bigint,text,text,text,text,uuid,uuid,text)','EXECUTE')))
    )
   """,db,tx);
  return await cmd.ExecuteScalarAsync(ct) is true;
 }
 public static async Task<bool> ReaderAsync(NpgsqlConnection db,NpgsqlTransaction tx,CancellationToken ct)
 {
  if(!await RestrictedDraftDatabaseRole.VerifyReadOnlyAsync(db,tx,ct))return false;
  await using var cmd=new NpgsqlCommand("""
   SELECT has_table_privilege(current_user,'learning.session_preparations'::regclass,'SELECT')
    AND has_table_privilege(current_user,'learning.session_preparation_revisions'::regclass,'SELECT')
    AND NOT (
      has_table_privilege(current_user,'learning.session_preparations'::regclass,'INSERT')
      OR has_table_privilege(current_user,'learning.session_preparation_revisions'::regclass,'INSERT')
      OR has_table_privilege(current_user,'learning.session_preparation_revisions'::regclass,'UPDATE')
      OR has_table_privilege(current_user,'learning.session_preparation_revisions'::regclass,'DELETE')
      OR has_function_privilege(current_user,
       'learning.append_session_preparation(text,uuid,uuid,bigint,text,text,text,text,uuid,uuid,text)','EXECUTE')
      OR EXISTS(SELECT 1 FROM pg_roles r WHERE r.rolname<>current_user
       AND pg_has_role(current_user,r.oid,'SET') AND (
        r.rolsuper OR r.rolbypassrls
        OR has_table_privilege(r.oid,'learning.session_preparation_revisions'::regclass,'INSERT')
        OR has_function_privilege(r.oid,
         'learning.append_session_preparation(text,uuid,uuid,bigint,text,text,text,text,uuid,uuid,text)','EXECUTE')))
    )
   """,db,tx);
  return await cmd.ExecuteScalarAsync(ct) is true;
 }
}
