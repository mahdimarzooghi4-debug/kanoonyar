using Npgsql;
namespace Kanoonyar.Api.Security;
public static class RestrictedCenterCampaignRole
{
 private const string Execute="campaigns.append_center_campaign(text,uuid,bigint,text,uuid,bigint,text,text,text,jsonb,text,uuid,uuid,text)";
 public static async Task<bool> WriterAsync(NpgsqlConnection db,NpgsqlTransaction tx,CancellationToken ct)
 {
  if(!await RestrictedDraftDatabaseRole.VerifyAsync(db,tx,ct))return false;
  await using var q=new NpgsqlCommand(
   """
   SELECT has_function_privilege(current_user,
    'campaigns.append_center_campaign(text,uuid,bigint,text,uuid,bigint,text,text,text,jsonb,text,uuid,uuid,text)','EXECUTE')
   AND NOT(
     has_table_privilege(current_user,'campaigns.center_campaigns'::regclass,'INSERT')
     OR has_table_privilege(current_user,'campaigns.center_campaign_revisions'::regclass,'INSERT')
     OR has_table_privilege(current_user,'campaigns.center_campaign_revisions'::regclass,'UPDATE')
     OR has_table_privilege(current_user,'campaigns.center_campaign_revisions'::regclass,'DELETE')
     OR has_table_privilege(current_user,'programs.center_plan_draft_revisions'::regclass,'INSERT')
     OR has_table_privilege(current_user,'governance.command_audit'::regclass,'INSERT')
     OR has_table_privilege(current_user,'governance.outbox_events'::regclass,'INSERT')
     OR has_table_privilege(current_user,'centers.memberships'::regclass,'UPDATE')
     OR EXISTS(SELECT 1 FROM pg_roles r WHERE r.rolname<>current_user
       AND pg_has_role(current_user,r.oid,'SET') AND(
       r.rolsuper OR r.rolbypassrls OR r.rolcreaterole OR r.rolcreatedb
       OR has_table_privilege(r.oid,'campaigns.center_campaign_revisions'::regclass,'INSERT')
       OR has_table_privilege(r.oid,'governance.command_audit'::regclass,'INSERT')
       OR has_function_privilege(r.oid,
         'campaigns.append_center_campaign(text,uuid,bigint,text,uuid,bigint,text,text,text,jsonb,text,uuid,uuid,text)','EXECUTE')))
   )
   """,db,tx);
  return await q.ExecuteScalarAsync(ct) is true;
 }
 public static async Task<bool> ReaderAsync(NpgsqlConnection db,NpgsqlTransaction tx,CancellationToken ct)
 {
  if(!await RestrictedDraftDatabaseRole.VerifyReadOnlyAsync(db,tx,ct))return false;
  await using var q=new NpgsqlCommand(
   """
   SELECT has_table_privilege(current_user,'campaigns.center_campaigns'::regclass,'SELECT')
   AND has_table_privilege(current_user,'campaigns.center_campaign_revisions'::regclass,'SELECT')
   AND NOT(
     has_table_privilege(current_user,'campaigns.center_campaigns'::regclass,'INSERT')
     OR has_table_privilege(current_user,'campaigns.center_campaign_revisions'::regclass,'INSERT')
     OR has_table_privilege(current_user,'campaigns.center_campaign_revisions'::regclass,'UPDATE')
     OR has_function_privilege(current_user,
       'campaigns.append_center_campaign(text,uuid,bigint,text,uuid,bigint,text,text,text,jsonb,text,uuid,uuid,text)','EXECUTE')
     OR EXISTS(SELECT 1 FROM pg_roles r WHERE r.rolname<>current_user
       AND pg_has_role(current_user,r.oid,'SET') AND(
       r.rolsuper OR r.rolbypassrls
       OR has_table_privilege(r.oid,'campaigns.center_campaign_revisions'::regclass,'INSERT')
       OR has_function_privilege(r.oid,
        'campaigns.append_center_campaign(text,uuid,bigint,text,uuid,bigint,text,text,text,jsonb,text,uuid,uuid,text)','EXECUTE')))
   )
   """,db,tx);
  return await q.ExecuteScalarAsync(ct) is true;
 }
}
