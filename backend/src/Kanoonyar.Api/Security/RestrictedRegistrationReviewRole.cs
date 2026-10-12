using Npgsql;

namespace Kanoonyar.Api.Security;

/// <summary>SQL role inspection, not a substitute for an independent human permission.</summary>
public static class RestrictedRegistrationReviewRole
{
 public static async Task<bool> VerifyWriterAsync(NpgsqlConnection db,NpgsqlTransaction tx,CancellationToken ct)
 {
  if(!await RestrictedDraftDatabaseRole.VerifyAsync(db,tx,ct))return false;
  await using var cmd=new NpgsqlCommand(
  """
  SELECT has_function_privilege(current_user,
   'learning.request_registration_human_review(text,uuid,uuid,uuid,text,text,uuid,uuid,text)','EXECUTE')
   AND NOT(
    has_table_privilege(current_user,'learning.registration_review_requests'::regclass,'INSERT')
    OR has_table_privilege(current_user,'learning.registration_review_requests'::regclass,'UPDATE')
    OR has_table_privilege(current_user,'learning.registration_review_requests'::regclass,'DELETE')
    OR has_table_privilege(current_user,'learning.class_registration_intents'::regclass,'INSERT')
    OR has_table_privilege(current_user,'learning.class_registration_intents'::regclass,'UPDATE')
    OR has_table_privilege(current_user,'governance.command_audit'::regclass,'INSERT')
    OR has_table_privilege(current_user,'governance.outbox_events'::regclass,'INSERT')
    OR has_table_privilege(current_user,'centers.memberships'::regclass,'UPDATE')
    OR has_function_privilege(current_user,
       'learning.record_class_registration_intent(text,uuid,uuid,uuid,text,uuid,uuid,text)','EXECUTE')
    OR has_function_privilege(current_user,
       'learning.append_class_capacity_revision(text,uuid,bigint,integer,text,uuid,uuid,text)','EXECUTE')
    OR EXISTS(SELECT 1 FROM pg_roles r WHERE r.rolname<>current_user
       AND pg_has_role(current_user,r.oid,'SET')
       AND (r.rolsuper OR r.rolbypassrls OR r.rolcreaterole OR r.rolcreatedb
         OR has_table_privilege(r.oid,'learning.registration_review_requests'::regclass,'INSERT')
         OR has_table_privilege(r.oid,'governance.command_audit'::regclass,'INSERT')
         OR has_table_privilege(r.oid,'centers.memberships'::regclass,'UPDATE')))
   )
  """,db,tx);
  return await cmd.ExecuteScalarAsync(ct) is true;
 }
 public static async Task<bool> VerifyReaderAsync(NpgsqlConnection db,NpgsqlTransaction tx,CancellationToken ct)
 {
  if(!await RestrictedClassIntakeRole.VerifyReadOnlyAsync(db,tx,ct))return false;
  await using var cmd=new NpgsqlCommand(
  """
  SELECT has_table_privilege(current_user,'learning.registration_review_requests'::regclass,'SELECT')
  AND NOT(
   has_table_privilege(current_user,'learning.registration_review_requests'::regclass,'INSERT')
   OR has_table_privilege(current_user,'learning.registration_review_requests'::regclass,'UPDATE')
   OR has_table_privilege(current_user,'learning.registration_review_requests'::regclass,'DELETE')
   OR has_function_privilege(current_user,
      'learning.request_registration_human_review(text,uuid,uuid,uuid,text,text,uuid,uuid,text)','EXECUTE')
   OR EXISTS(SELECT 1 FROM pg_roles r WHERE r.rolname<>current_user
     AND pg_has_role(current_user,r.oid,'SET')
     AND (r.rolsuper OR r.rolbypassrls
       OR has_table_privilege(r.oid,'learning.registration_review_requests'::regclass,'INSERT')
       OR has_function_privilege(r.oid,
        'learning.request_registration_human_review(text,uuid,uuid,uuid,text,text,uuid,uuid,text)','EXECUTE')))
  )
  """,db,tx);
  return await cmd.ExecuteScalarAsync(ct) is true;
 }
}
