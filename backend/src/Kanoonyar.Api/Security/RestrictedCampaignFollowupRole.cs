using Npgsql;
namespace Kanoonyar.Api.Security;

public static class RestrictedCampaignFollowupRole
{
 private const string Signature="campaigns.append_inspection_followup(text,uuid,uuid,uuid,uuid,uuid,text,text,text,uuid,uuid,text)";
 public static async Task<bool> WriterAsync(NpgsqlConnection db,NpgsqlTransaction tx,CancellationToken ct)
 {
  if(!await RestrictedDraftDatabaseRole.VerifyAsync(db,tx,ct))return false;
  await using var q=new NpgsqlCommand(
   """
   SELECT has_function_privilege(current_user,
     'campaigns.append_inspection_followup(text,uuid,uuid,uuid,uuid,uuid,text,text,text,uuid,uuid,text)','EXECUTE')
   AND NOT(
    has_table_privilege(current_user,'campaigns.inspection_followup_requests'::regclass,'INSERT')
    OR has_table_privilege(current_user,'campaigns.inspection_followup_responses'::regclass,'INSERT')
    OR has_table_privilege(current_user,'campaigns.inspection_followup_requests'::regclass,'UPDATE')
    OR has_table_privilege(current_user,'campaigns.inspection_followup_responses'::regclass,'DELETE')
    OR has_table_privilege(current_user,'governance.command_audit'::regclass,'INSERT')
    OR has_table_privilege(current_user,'governance.outbox_events'::regclass,'INSERT')
    OR has_table_privilege(current_user,'centers.memberships'::regclass,'UPDATE')
    OR EXISTS(SELECT 1 FROM pg_roles r WHERE r.rolname<>current_user
     AND pg_has_role(current_user,r.oid,'SET') AND(
      r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
      OR has_table_privilege(r.oid,'campaigns.inspection_followup_requests'::regclass,'INSERT')
      OR has_table_privilege(r.oid,'campaigns.inspection_followup_responses'::regclass,'INSERT')
      OR has_table_privilege(r.oid,'governance.command_audit'::regclass,'INSERT')
      OR has_function_privilege(r.oid,
       'campaigns.append_inspection_followup(text,uuid,uuid,uuid,uuid,uuid,text,text,text,uuid,uuid,text)','EXECUTE')))
   )
   """,db,tx);
  return await q.ExecuteScalarAsync(ct) is true;
 }
 public static async Task<bool> ReaderAsync(NpgsqlConnection db,NpgsqlTransaction tx,CancellationToken ct)
 {
  if(!await RestrictedDraftDatabaseRole.VerifyReadOnlyAsync(db,tx,ct))return false;
  await using var q=new NpgsqlCommand(
   """
   SELECT has_table_privilege(current_user,'campaigns.campaign_report_inspection_notes'::regclass,'SELECT')
    AND has_table_privilege(current_user,'campaigns.inspection_followup_requests'::regclass,'SELECT')
    AND has_table_privilege(current_user,'campaigns.inspection_followup_responses'::regclass,'SELECT')
    AND NOT(
     has_table_privilege(current_user,'campaigns.inspection_followup_requests'::regclass,'INSERT')
     OR has_table_privilege(current_user,'campaigns.inspection_followup_responses'::regclass,'INSERT')
     OR has_table_privilege(current_user,'campaigns.inspection_followup_requests'::regclass,'UPDATE')
     OR has_table_privilege(current_user,'campaigns.inspection_followup_responses'::regclass,'DELETE')
     OR has_function_privilege(current_user,
       'campaigns.append_inspection_followup(text,uuid,uuid,uuid,uuid,uuid,text,text,text,uuid,uuid,text)','EXECUTE')
     OR EXISTS(SELECT 1 FROM pg_roles r WHERE r.rolname<>current_user
      AND pg_has_role(current_user,r.oid,'SET') AND(
       r.rolsuper OR r.rolbypassrls OR
       has_table_privilege(r.oid,'campaigns.inspection_followup_requests'::regclass,'INSERT')
       OR has_table_privilege(r.oid,'campaigns.inspection_followup_responses'::regclass,'INSERT')
       OR has_function_privilege(r.oid,
         'campaigns.append_inspection_followup(text,uuid,uuid,uuid,uuid,uuid,text,text,text,uuid,uuid,text)','EXECUTE')))
   )
   """,db,tx);
  return await q.ExecuteScalarAsync(ct) is true;
 }
}
