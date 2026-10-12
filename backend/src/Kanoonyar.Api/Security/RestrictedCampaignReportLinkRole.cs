using Npgsql;

namespace Kanoonyar.Api.Security;

public static class RestrictedCampaignReportLinkRole
{
 public static async Task<bool> WriterAsync(NpgsqlConnection db,NpgsqlTransaction tx,CancellationToken ct)
 {
  if(!await RestrictedDraftDatabaseRole.VerifyAsync(db,tx,ct))return false;
  await using var q=new NpgsqlCommand(
   """
   SELECT has_function_privilege(current_user,
     'campaigns.link_unverified_source_report(text,uuid,uuid,bigint,text,uuid,text,uuid,uuid,text)','EXECUTE')
   AND NOT (
    has_table_privilege(current_user,'campaigns.campaign_report_links'::regclass,'INSERT')
    OR has_table_privilege(current_user,'campaigns.campaign_report_links'::regclass,'UPDATE')
    OR has_table_privilege(current_user,'campaigns.campaign_report_links'::regclass,'DELETE')
    OR has_table_privilege(current_user,'programs.center_activity_reports'::regclass,'INSERT')
    OR has_table_privilege(current_user,'governance.command_audit'::regclass,'INSERT')
    OR has_table_privilege(current_user,'governance.outbox_events'::regclass,'INSERT')
    OR has_table_privilege(current_user,'centers.memberships'::regclass,'UPDATE')
    OR EXISTS(SELECT 1 FROM pg_roles r WHERE r.rolname<>current_user
      AND pg_has_role(current_user,r.oid,'SET') AND(
       r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
       OR has_table_privilege(r.oid,'campaigns.campaign_report_links'::regclass,'INSERT')
       OR has_table_privilege(r.oid,'governance.command_audit'::regclass,'INSERT')
       OR has_function_privilege(r.oid,
         'campaigns.link_unverified_source_report(text,uuid,uuid,bigint,text,uuid,text,uuid,uuid,text)','EXECUTE')))
   )
   """,db,tx);
  return await q.ExecuteScalarAsync(ct) is true;
 }
 public static async Task<bool> ReaderAsync(NpgsqlConnection db,NpgsqlTransaction tx,CancellationToken ct)
 {
  if(!await RestrictedDraftDatabaseRole.VerifyReadOnlyAsync(db,tx,ct))return false;
  await using var q=new NpgsqlCommand(
   """
   SELECT has_table_privilege(current_user,'campaigns.campaign_report_links'::regclass,'SELECT')
    AND has_table_privilege(current_user,'campaigns.center_campaigns'::regclass,'SELECT')
    AND has_table_privilege(current_user,'programs.center_activity_reports'::regclass,'SELECT')
    AND has_table_privilege(current_user,'programs.activity_report_source_bindings'::regclass,'SELECT')
    AND NOT(
     has_table_privilege(current_user,'campaigns.campaign_report_links'::regclass,'INSERT')
     OR has_table_privilege(current_user,'campaigns.campaign_report_links'::regclass,'UPDATE')
     OR has_table_privilege(current_user,'campaigns.campaign_report_links'::regclass,'DELETE')
     OR has_function_privilege(current_user,
       'campaigns.link_unverified_source_report(text,uuid,uuid,bigint,text,uuid,text,uuid,uuid,text)','EXECUTE')
     OR EXISTS(SELECT 1 FROM pg_roles r WHERE r.rolname<>current_user
      AND pg_has_role(current_user,r.oid,'SET') AND(
       r.rolsuper OR r.rolbypassrls OR
       has_table_privilege(r.oid,'campaigns.campaign_report_links'::regclass,'INSERT')
       OR has_function_privilege(r.oid,
       'campaigns.link_unverified_source_report(text,uuid,uuid,bigint,text,uuid,text,uuid,uuid,text)','EXECUTE')))
   )
   """,db,tx);
  return await q.ExecuteScalarAsync(ct) is true;
 }
}
