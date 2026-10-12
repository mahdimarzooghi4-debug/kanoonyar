using Npgsql;

namespace Kanoonyar.Api.Security;

/// <summary>Database capability guard; does not replace independently authorized user grants.</summary>
public static class RestrictedCenterSiteDraftRole
{
 public static async Task<bool> WriterAsync(NpgsqlConnection db,NpgsqlTransaction tx,CancellationToken ct)
 {
  if(!await RestrictedDraftDatabaseRole.VerifyAsync(db,tx,ct))return false;
  await using var cmd=new NpgsqlCommand(
  """
  SELECT has_function_privilege(current_user,
    'web.append_center_page_draft(text,uuid,bigint,text,text,text,text,uuid,uuid,text)','EXECUTE')
  AND NOT(
   has_table_privilege(current_user,'web.center_pages'::regclass,'INSERT')
   OR has_table_privilege(current_user,'web.center_pages'::regclass,'UPDATE')
   OR has_table_privilege(current_user,'web.center_pages'::regclass,'DELETE')
   OR has_table_privilege(current_user,'web.page_draft_revisions'::regclass,'INSERT')
   OR has_table_privilege(current_user,'web.page_draft_revisions'::regclass,'UPDATE')
   OR has_table_privilege(current_user,'web.page_draft_revisions'::regclass,'DELETE')
   OR has_table_privilege(current_user,'governance.command_audit'::regclass,'INSERT')
   OR has_table_privilege(current_user,'governance.outbox_events'::regclass,'INSERT')
   OR has_table_privilege(current_user,'centers.memberships'::regclass,'UPDATE')
   OR has_function_privilege(current_user,
     'programs.append_local_plan_draft_revision(text,uuid,bigint,text,integer,text,jsonb,text,uuid,uuid,text)','EXECUTE')
   OR EXISTS (SELECT 1 FROM pg_roles r WHERE r.rolname<>current_user
     AND pg_has_role(current_user,r.oid,'SET') AND(
       r.rolsuper OR r.rolbypassrls OR r.rolcreaterole OR r.rolcreatedb OR r.rolreplication
       OR has_table_privilege(r.oid,'web.page_draft_revisions'::regclass,'INSERT')
       OR has_table_privilege(r.oid,'governance.command_audit'::regclass,'INSERT')
       OR has_function_privilege(r.oid,
         'web.append_center_page_draft(text,uuid,bigint,text,text,text,text,uuid,uuid,text)','EXECUTE')))
  )
  """,db,tx);
  return await cmd.ExecuteScalarAsync(ct) is true;
 }
 public static async Task<bool> ReaderAsync(NpgsqlConnection db,NpgsqlTransaction tx,CancellationToken ct)
 {
  if(!await RestrictedDraftDatabaseRole.VerifyReadOnlyAsync(db,tx,ct))return false;
  await using var cmd=new NpgsqlCommand(
  """
  SELECT has_table_privilege(current_user,'web.center_pages'::regclass,'SELECT')
    AND has_table_privilege(current_user,'web.page_draft_revisions'::regclass,'SELECT')
    AND NOT(
     has_table_privilege(current_user,'web.center_pages'::regclass,'INSERT')
     OR has_table_privilege(current_user,'web.page_draft_revisions'::regclass,'INSERT')
     OR has_table_privilege(current_user,'web.page_draft_revisions'::regclass,'UPDATE')
     OR has_table_privilege(current_user,'web.page_draft_revisions'::regclass,'DELETE')
     OR has_function_privilege(current_user,
       'web.append_center_page_draft(text,uuid,bigint,text,text,text,text,uuid,uuid,text)','EXECUTE')
     OR EXISTS(SELECT 1 FROM pg_roles r WHERE r.rolname<>current_user
       AND pg_has_role(current_user,r.oid,'SET') AND(
        r.rolsuper OR r.rolbypassrls OR has_table_privilege(r.oid,
           'web.page_draft_revisions'::regclass,'INSERT')
        OR has_function_privilege(r.oid,
         'web.append_center_page_draft(text,uuid,bigint,text,text,text,text,uuid,uuid,text)','EXECUTE')))
    )
  """,db,tx);
  return await cmd.ExecuteScalarAsync(ct) is true;
 }
}
