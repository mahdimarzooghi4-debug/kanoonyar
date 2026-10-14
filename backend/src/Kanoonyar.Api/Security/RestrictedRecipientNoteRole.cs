using Npgsql;
namespace Kanoonyar.Api.Security;
public static class RestrictedRecipientNoteRole
{
 public static async Task<bool> WriterAsync(NpgsqlConnection db,NpgsqlTransaction tx,CancellationToken ct)
 {
  if(!await RestrictedDraftDatabaseRole.VerifyAsync(db,tx,ct))return false;
  await using var q=new NpgsqlCommand("""
   SELECT has_function_privilege(current_user,
     'collaboration.append_recipient_private_note(uuid,uuid,text,text,text,uuid,uuid,text)','EXECUTE')
    AND NOT (
     has_table_privilege(current_user,'collaboration.recipient_private_notes'::regclass,'SELECT')
     OR has_table_privilege(current_user,'collaboration.recipient_private_notes'::regclass,'INSERT')
     OR has_table_privilege(current_user,'collaboration.recipient_private_notes'::regclass,'UPDATE')
     OR has_table_privilege(current_user,'collaboration.recipient_private_notes'::regclass,'DELETE')
     OR has_table_privilege(current_user,'governance.command_audit'::regclass,'INSERT')
     OR has_table_privilege(current_user,'governance.outbox_events'::regclass,'INSERT')
     OR EXISTS(SELECT 1 FROM pg_roles r WHERE r.rolname<>current_user
      AND pg_has_role(current_user,r.oid,'SET') AND (
       r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
       OR has_table_privilege(r.oid,'collaboration.recipient_private_notes'::regclass,'INSERT')
       OR has_function_privilege(r.oid,
        'collaboration.append_recipient_private_note(uuid,uuid,text,text,text,uuid,uuid,text)','EXECUTE')))
    )
   """,db,tx);
  return await q.ExecuteScalarAsync(ct) is true;
 }
 public static async Task<bool> ReaderAsync(NpgsqlConnection db,NpgsqlTransaction tx,CancellationToken ct)
 {
  if(!await RestrictedDraftDatabaseRole.VerifyReadOnlyAsync(db,tx,ct))return false;
  await using var q=new NpgsqlCommand("""
   SELECT has_column_privilege(current_user,'collaboration.recipient_private_notes'::regclass,'note_id','SELECT')
    AND has_column_privilege(current_user,'collaboration.recipient_private_notes'::regclass,'proposal_id','SELECT')
    AND has_column_privilege(current_user,'collaboration.recipient_private_notes'::regclass,'target_center_id','SELECT')
    AND has_column_privilege(current_user,'collaboration.recipient_private_notes'::regclass,'note_text','SELECT')
    AND has_column_privilege(current_user,'collaboration.recipient_private_notes'::regclass,'recorded_at','SELECT')
    AND NOT (
     has_column_privilege(current_user,'collaboration.recipient_private_notes'::regclass,'recorded_by_subject','SELECT')
     OR has_table_privilege(current_user,'collaboration.recipient_private_notes'::regclass,'INSERT')
     OR has_table_privilege(current_user,'collaboration.recipient_private_notes'::regclass,'UPDATE')
     OR has_table_privilege(current_user,'collaboration.recipient_private_notes'::regclass,'DELETE')
     OR has_function_privilege(current_user,
      'collaboration.append_recipient_private_note(uuid,uuid,text,text,text,uuid,uuid,text)','EXECUTE')
     OR EXISTS(SELECT 1 FROM pg_roles r WHERE r.rolname<>current_user
      AND pg_has_role(current_user,r.oid,'SET') AND (
       r.rolsuper OR r.rolbypassrls
       OR has_table_privilege(r.oid,'collaboration.recipient_private_notes'::regclass,'INSERT')
       OR has_function_privilege(r.oid,
        'collaboration.append_recipient_private_note(uuid,uuid,text,text,text,uuid,uuid,text)','EXECUTE')))
    )
   """,db,tx);
  return await q.ExecuteScalarAsync(ct) is true;
 }
}
