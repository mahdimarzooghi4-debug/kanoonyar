using Npgsql;
namespace Kanoonyar.Api.Security;
/// <summary>SQL capabilities only. App must separately verify live center membership and operation grant.</summary>
public static class RestrictedCollaborationRole
{
 public static async Task<bool> WriterAsync(NpgsqlConnection db,NpgsqlTransaction tx,CancellationToken ct)
 {
  if(!await RestrictedDraftDatabaseRole.VerifyAsync(db,tx,ct))return false;
  await using var q=new NpgsqlCommand("""
   SELECT has_function_privilege(current_user,
    'collaboration.record_center_proposal(uuid,text,text,text,text,text,uuid,uuid,text)','EXECUTE')
    AND NOT (
     has_table_privilege(current_user,'collaboration.center_proposals'::regclass,'INSERT')
     OR has_table_privilege(current_user,'collaboration.center_proposals'::regclass,'SELECT')
     OR has_table_privilege(current_user,'collaboration.center_proposals'::regclass,'UPDATE')
     OR has_table_privilege(current_user,'collaboration.center_proposals'::regclass,'DELETE')
     OR has_table_privilege(current_user,'governance.command_audit'::regclass,'INSERT')
     OR has_table_privilege(current_user,'governance.outbox_events'::regclass,'INSERT')
     OR EXISTS(SELECT 1 FROM pg_roles r WHERE r.rolname<>current_user
       AND pg_has_role(current_user,r.oid,'SET') AND (
        r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
        OR has_table_privilege(r.oid,'collaboration.center_proposals'::regclass,'INSERT')
        OR has_function_privilege(r.oid,
         'collaboration.record_center_proposal(uuid,text,text,text,text,text,uuid,uuid,text)','EXECUTE')))
    )
   """,db,tx);
  return await q.ExecuteScalarAsync(ct) is true;
 }
 public static async Task<bool> ReaderAsync(NpgsqlConnection db,NpgsqlTransaction tx,CancellationToken ct)
 {
  if(!await RestrictedDraftDatabaseRole.VerifyReadOnlyAsync(db,tx,ct))return false;
  await using var q=new NpgsqlCommand("""
   SELECT has_column_privilege(current_user,'collaboration.center_proposals'::regclass,'proposal_id','SELECT')
    AND has_column_privilege(current_user,'collaboration.center_proposals'::regclass,'source_center_id','SELECT')
    AND has_column_privilege(current_user,'collaboration.center_proposals'::regclass,'target_center_id','SELECT')
    AND has_column_privilege(current_user,'collaboration.center_proposals'::regclass,'title','SELECT')
    AND has_column_privilege(current_user,'collaboration.center_proposals'::regclass,'summary','SELECT')
    AND has_column_privilege(current_user,'collaboration.center_proposals'::regclass,'recorded_at','SELECT')
    AND NOT (
     has_column_privilege(current_user,'collaboration.center_proposals'::regclass,'created_by_subject','SELECT')
     OR has_table_privilege(current_user,'collaboration.center_proposals'::regclass,'INSERT')
     OR has_table_privilege(current_user,'collaboration.center_proposals'::regclass,'UPDATE')
     OR has_table_privilege(current_user,'collaboration.center_proposals'::regclass,'DELETE')
     OR has_function_privilege(current_user,
      'collaboration.record_center_proposal(uuid,text,text,text,text,text,uuid,uuid,text)','EXECUTE')
     OR EXISTS(SELECT 1 FROM pg_roles r WHERE r.rolname<>current_user
      AND pg_has_role(current_user,r.oid,'SET') AND (
       r.rolsuper OR r.rolbypassrls
       OR has_table_privilege(r.oid,'collaboration.center_proposals'::regclass,'INSERT')
       OR has_function_privilege(r.oid,
        'collaboration.record_center_proposal(uuid,text,text,text,text,text,uuid,uuid,text)','EXECUTE')))
    )
   """,db,tx);
  return await q.ExecuteScalarAsync(ct) is true;
 }
}
