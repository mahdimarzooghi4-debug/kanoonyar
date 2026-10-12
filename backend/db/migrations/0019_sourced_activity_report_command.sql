-- Sprint 17: audited typed-source activity report command, never confirmed execution.
-- A NOLOGIN owner writes report+binding+audit+outbox through existing invoker
-- contracts in one transaction. Runtime app privileges and identity stay DENY.
BEGIN;
DO $role$
BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_sourced_report_command_owner') THEN
   CREATE ROLE kanoonyar_sourced_report_command_owner NOLOGIN NOINHERIT
     NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION;
 END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_sourced_report_command_owner'
  AND NOT rolcanlogin AND NOT rolsuper AND NOT rolbypassrls
  AND NOT rolcreaterole AND NOT rolcreatedb AND NOT rolreplication) THEN
   RAISE EXCEPTION 'Unsafe sourced report command owner';
 END IF;
END;$role$;

GRANT USAGE ON SCHEMA programs,learning,events,centers,governance
 TO kanoonyar_sourced_report_command_owner;
GRANT SELECT ON centers.memberships,centers.registry,
 learning.class_sessions,events.center_events,programs.center_plan_issuance_links
 TO kanoonyar_sourced_report_command_owner;
GRANT INSERT ON programs.center_activity_reports,
 programs.activity_report_source_bindings,governance.command_audit,governance.outbox_events
 TO kanoonyar_sourced_report_command_owner;
GRANT EXECUTE ON FUNCTION programs.record_center_activity_report(
 uuid,text,uuid,uuid,text,text,text,text,timestamptz,text,uuid,uuid,text)
 TO kanoonyar_sourced_report_command_owner;
GRANT EXECUTE ON FUNCTION programs.record_center_sourced_activity_report(
 uuid,text,uuid,uuid,text,uuid,text,text,timestamptz,text,uuid,uuid,text)
 TO kanoonyar_sourced_report_command_owner;

CREATE FUNCTION programs.record_center_sourced_activity_command(
 p_center text,p_report uuid,p_link uuid,p_draft uuid,
 p_kind text,p_source uuid,p_evidence text,p_summary text,
 p_claimed timestamptz,p_actor text,p_audit uuid,p_event uuid,p_correlation text
) RETURNS uuid LANGUAGE plpgsql SECURITY DEFINER
SET search_path=pg_catalog,pg_temp AS $fn$
BEGIN
 IF p_center IS NULL OR btrim(p_center)='' OR p_report IS NULL
  OR p_link IS NULL OR p_draft IS NULL OR p_source IS NULL
  OR p_kind NOT IN ('CLASS_SESSION','CENTER_EVENT') OR p_kind IS NULL
  OR p_evidence IS NULL OR btrim(p_evidence)=''
  OR p_summary IS NULL OR btrim(p_summary)=''
  OR p_actor IS NULL OR btrim(p_actor)=''
  OR p_audit IS NULL OR p_event IS NULL
  OR p_correlation IS NULL OR btrim(p_correlation)='' THEN
   RAISE EXCEPTION 'Invalid sourced report command' USING ERRCODE='22023';
 END IF;
 -- A custom GUC is defense-in-depth, never independent identity.
 IF p_center IS DISTINCT FROM NULLIF(current_setting('kanoonyar.center_id',true),'')
  OR NOT EXISTS(SELECT 1 FROM centers.memberships m JOIN centers.registry c
    ON c.center_id=m.center_id
    WHERE m.center_id=p_center AND m.subject_id=p_actor
      AND m.is_active AND c.is_active) THEN
   RAISE EXCEPTION 'Center scope or active membership absent' USING ERRCODE='42501';
 END IF;
 -- Exact owner and plan linkage; source and plan are never inferred from text.
 IF NOT EXISTS(SELECT 1 FROM programs.center_plan_issuance_links l
    WHERE l.center_id=p_center AND l.link_id=p_link AND l.draft_id=p_draft)
   OR (p_kind='CLASS_SESSION' AND NOT EXISTS(
      SELECT 1 FROM learning.class_sessions s WHERE s.center_id=p_center AND s.session_id=p_source))
   OR (p_kind='CENTER_EVENT' AND NOT EXISTS(
      SELECT 1 FROM events.center_events e WHERE e.center_id=p_center AND e.event_id=p_source)) THEN
   RAISE EXCEPTION 'Source or pinned plan missing in center' USING ERRCODE='23503';
 END IF;
 PERFORM programs.record_center_sourced_activity_report(
   p_report,p_center,p_link,p_draft,p_kind,p_source,p_evidence,p_summary,
   p_claimed,p_actor,p_audit,p_event,p_correlation);
 RETURN p_report;
END;$fn$;
ALTER FUNCTION programs.record_center_sourced_activity_command(
 text,uuid,uuid,uuid,text,uuid,text,text,timestamptz,text,uuid,uuid,text)
 OWNER TO kanoonyar_sourced_report_command_owner;
REVOKE ALL ON FUNCTION programs.record_center_sourced_activity_command(
 text,uuid,uuid,uuid,text,uuid,text,text,timestamptz,text,uuid,uuid,text) FROM PUBLIC;
COMMENT ON FUNCTION programs.record_center_sourced_activity_command(
 text,uuid,uuid,uuid,text,uuid,text,text,timestamptz,text,uuid,uuid,text)
 IS 'Unverified typed-owner activity report only; no live operation grants, participant data, execution verification or approved evidence.';
COMMIT;
