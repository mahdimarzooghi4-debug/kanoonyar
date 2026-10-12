using Npgsql;

namespace Kanoonyar.Api.Security;

/// <summary>
/// Privilege inspection only; authenticated actor/resource permission is separate.
/// A verified SQL login must execute audited commands, never insert claims directly.
/// </summary>
public static class RestrictedSessionClaimCommandRole
{
    public static async Task<bool> VerifyAsync(NpgsqlConnection db, NpgsqlTransaction tx,
        CancellationToken ct)
    {
        if (!await RestrictedLearningCommandRole.VerifyAsync(db,tx,ct)) return false;
        await using var cmd = new NpgsqlCommand(
            """
            SELECT
              has_function_privilege(current_user,
                'learning.record_session_occurrence_claim(text,uuid,uuid,text,text,timestamptz,timestamptz,text,uuid,uuid,text)',
                'EXECUTE')
              AND has_function_privilege(current_user,
                'learning.record_session_attendance_claim(text,uuid,uuid,uuid,text,text,text,uuid,uuid,text)',
                'EXECUTE')
              AND NOT(
                has_table_privilege(current_user,'learning.session_occurrence_reports'::regclass,'INSERT')
                OR has_table_privilege(current_user,'learning.session_occurrence_reports'::regclass,'UPDATE')
                OR has_table_privilege(current_user,'learning.session_occurrence_reports'::regclass,'DELETE')
                OR has_table_privilege(current_user,'learning.session_attendance_claims'::regclass,'INSERT')
                OR has_table_privilege(current_user,'learning.session_attendance_claims'::regclass,'UPDATE')
                OR has_table_privilege(current_user,'learning.session_attendance_claims'::regclass,'DELETE')
                OR EXISTS(SELECT 1 FROM pg_roles r
                   WHERE r.rolname<>current_user
                     AND pg_has_role(current_user,r.oid,'SET')
                     AND (
                       r.rolsuper OR r.rolbypassrls OR r.rolcreaterole
                       OR has_table_privilege(r.oid,'learning.session_occurrence_reports'::regclass,'INSERT')
                       OR has_table_privilege(r.oid,'learning.session_attendance_claims'::regclass,'INSERT')
                     ))
              )
            """,db,tx);
        return await cmd.ExecuteScalarAsync(ct) is true;
    }
}
