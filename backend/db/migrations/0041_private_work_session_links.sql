-- Sprint 41: optional explicit private planning reference, not observed activity.
BEGIN;
CREATE TABLE operations.work_card_session_links (
 center_id text NOT NULL,
 work_card_id uuid NOT NULL,
 session_id uuid NOT NULL,
 recorded_by_subject text NOT NULL CHECK(length(btrim(recorded_by_subject))>0),
 recorded_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
 PRIMARY KEY(center_id,work_card_id,session_id),
 FOREIGN KEY(center_id,work_card_id)
  REFERENCES operations.center_work_cards(center_id,work_card_id) ON DELETE RESTRICT,
 FOREIGN KEY(center_id,session_id)
  REFERENCES learning.class_sessions(center_id,session_id) ON DELETE RESTRICT
);
CREATE INDEX work_card_session_links_cursor
 ON operations.work_card_session_links(center_id,work_card_id,session_id DESC);
CREATE TRIGGER work_card_session_link_immutable BEFORE UPDATE OR DELETE
 ON operations.work_card_session_links FOR EACH ROW
 EXECUTE FUNCTION programs.reject_draft_revision_mutation();
ALTER TABLE operations.work_card_session_links ENABLE ROW LEVEL SECURITY;
ALTER TABLE operations.work_card_session_links FORCE ROW LEVEL SECURITY;
CREATE POLICY work_card_session_link_scope ON operations.work_card_session_links
 USING(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));
REVOKE ALL ON operations.work_card_session_links FROM PUBLIC;

DO $role$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_work_session_link_owner') THEN
  CREATE ROLE kanoonyar_work_session_link_owner NOLOGIN NOINHERIT
   NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION;
 END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_work_session_link_owner'
  AND NOT rolcanlogin AND NOT rolsuper AND NOT rolbypassrls
  AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication) THEN
  RAISE EXCEPTION 'Unsafe work session link owner';
 END IF;
END;$role$;
GRANT USAGE ON SCHEMA operations,learning,centers,governance,programs
 TO kanoonyar_work_session_link_owner;
GRANT SELECT ON centers.memberships,centers.registry,operations.center_work_cards,
 learning.class_sessions TO kanoonyar_work_session_link_owner;
GRANT SELECT,INSERT ON operations.work_card_session_links
 TO kanoonyar_work_session_link_owner;
GRANT INSERT ON governance.command_audit,governance.outbox_events
 TO kanoonyar_work_session_link_owner;
CREATE FUNCTION operations.append_work_card_session_link(
 p_center text,p_card uuid,p_session uuid,p_actor text,
 p_audit uuid,p_event uuid,p_correlation text
) RETURNS uuid LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $fn$
BEGIN
 IF p_center IS NULL OR btrim(p_center)='' OR p_card IS NULL OR
  p_card='00000000-0000-0000-0000-000000000000'::uuid OR p_session IS NULL OR
  p_session='00000000-0000-0000-0000-000000000000'::uuid OR
  p_actor IS NULL OR btrim(p_actor)='' OR p_audit IS NULL OR p_event IS NULL OR
  p_correlation IS NULL OR btrim(p_correlation)='' THEN
  RAISE EXCEPTION 'Invalid private session link command' USING ERRCODE='22023';
 END IF;
 IF p_center IS DISTINCT FROM NULLIF(current_setting('kanoonyar.center_id',true),'')
  OR NOT EXISTS(SELECT 1 FROM centers.memberships m JOIN centers.registry c
   ON c.center_id=m.center_id WHERE m.center_id=p_center AND
    m.subject_id=p_actor AND m.is_active AND c.is_active) THEN
  RAISE EXCEPTION 'Center scope or active membership absent' USING ERRCODE='42501';
 END IF;
 IF NOT EXISTS(SELECT 1 FROM operations.center_work_cards c
  WHERE c.center_id=p_center AND c.work_card_id=p_card)
  OR NOT EXISTS(SELECT 1 FROM learning.class_sessions s
   WHERE s.center_id=p_center AND s.session_id=p_session) THEN
  RAISE EXCEPTION 'Original same-center work-card/session absent'
   USING ERRCODE='23503';
 END IF;
 INSERT INTO operations.work_card_session_links
  (center_id,work_card_id,session_id,recorded_by_subject)
 VALUES(p_center,p_card,p_session,p_actor);
 INSERT INTO governance.command_audit(audit_id,occurred_at,actor_subject_id,
  center_id,action_code,target_type,target_id,correlation_id,metadata)
 VALUES(p_audit,transaction_timestamp(),p_actor,p_center,
  'PRIVATE_WORK_SESSION_LINK_APPENDED','work_card_session_link',p_card::text,
  p_correlation,jsonb_build_object('session_id',p_session,
    'visibility','PRIVATE_PLANNING','is_executed',false));
 INSERT INTO governance.outbox_events(event_id,audit_id,event_type,aggregate_type,
  aggregate_id,occurred_at,payload)
 VALUES(p_event,p_audit,'PrivateWorkSessionLinkAppended','work_card_session_link',
  p_card::text,transaction_timestamp(),jsonb_build_object('center_id',p_center,
   'work_card_id',p_card,'session_id',p_session,
   'visibility','PRIVATE_PLANNING','is_executed',false,
   'is_attendance_verified',false));
 RETURN p_session;
END;$fn$;
ALTER FUNCTION operations.append_work_card_session_link(
 text,uuid,uuid,text,uuid,uuid,text) OWNER TO kanoonyar_work_session_link_owner;
REVOKE ALL ON FUNCTION operations.append_work_card_session_link(
 text,uuid,uuid,text,uuid,uuid,text) FROM PUBLIC;
COMMENT ON TABLE operations.work_card_session_links IS
 'Immutable optional private planning references: no class delivery/attendance/program compliance or publication.';
COMMIT;
