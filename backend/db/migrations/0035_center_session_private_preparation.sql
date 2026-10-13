-- Sprint 35 — Private session preparation, no lesson publication, attendance or assessment.
BEGIN;
CREATE TABLE learning.session_preparations (
 center_id text NOT NULL,
 session_id uuid NOT NULL,
 class_id uuid NOT NULL,
 created_by_subject text NOT NULL CHECK(length(btrim(created_by_subject))>0),
 recorded_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
 PRIMARY KEY(center_id,session_id),
 FOREIGN KEY(center_id,session_id) REFERENCES learning.class_sessions(center_id,session_id)
  ON DELETE RESTRICT,
 FOREIGN KEY(center_id,class_id) REFERENCES learning.center_classes(center_id,class_id)
  ON DELETE RESTRICT
);
CREATE TABLE learning.session_preparation_revisions (
 center_id text NOT NULL,
 session_id uuid NOT NULL,
 revision bigint NOT NULL CHECK(revision>0),
 preparation_title text NOT NULL CHECK(length(btrim(preparation_title)) BETWEEN 1 AND 160),
 teaching_outline text NOT NULL CHECK(length(teaching_outline)<=4000),
 materials_note text NOT NULL CHECK(length(materials_note)<=2000),
 changed_by_subject text NOT NULL CHECK(length(btrim(changed_by_subject))>0),
 recorded_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
 PRIMARY KEY(center_id,session_id,revision),
 FOREIGN KEY(center_id,session_id)
  REFERENCES learning.session_preparations(center_id,session_id) ON DELETE RESTRICT
);
CREATE TRIGGER session_preparation_identity_immutable BEFORE UPDATE OR DELETE
 ON learning.session_preparations FOR EACH ROW
 EXECUTE FUNCTION programs.reject_draft_revision_mutation();
CREATE TRIGGER session_preparation_revision_immutable BEFORE UPDATE OR DELETE
 ON learning.session_preparation_revisions FOR EACH ROW
 EXECUTE FUNCTION programs.reject_draft_revision_mutation();
ALTER TABLE learning.session_preparations ENABLE ROW LEVEL SECURITY;
ALTER TABLE learning.session_preparations FORCE ROW LEVEL SECURITY;
ALTER TABLE learning.session_preparation_revisions ENABLE ROW LEVEL SECURITY;
ALTER TABLE learning.session_preparation_revisions FORCE ROW LEVEL SECURITY;
CREATE POLICY session_preparation_center_scope ON learning.session_preparations
 USING(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));
CREATE POLICY session_preparation_revision_center_scope ON learning.session_preparation_revisions
 USING(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));
REVOKE ALL ON learning.session_preparations,learning.session_preparation_revisions FROM PUBLIC;
DO $role$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_session_prep_owner') THEN
  CREATE ROLE kanoonyar_session_prep_owner NOLOGIN NOINHERIT NOSUPERUSER
   NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION;
 END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_session_prep_owner'
  AND NOT rolcanlogin AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb
  AND NOT rolcreaterole AND NOT rolreplication) THEN
  RAISE EXCEPTION 'Unsafe session preparation owner';
 END IF;
END;$role$;
GRANT USAGE ON SCHEMA learning,centers,governance TO kanoonyar_session_prep_owner;
GRANT SELECT ON learning.center_classes,learning.class_sessions,centers.registry,centers.memberships
 TO kanoonyar_session_prep_owner;
GRANT SELECT,INSERT ON learning.session_preparations,learning.session_preparation_revisions
 TO kanoonyar_session_prep_owner;
GRANT INSERT ON governance.command_audit,governance.outbox_events
 TO kanoonyar_session_prep_owner;
CREATE FUNCTION learning.append_session_preparation(
 p_center text,p_session uuid,p_class uuid,p_expected bigint,
 p_title text,p_outline text,p_materials text,p_actor text,
 p_audit uuid,p_event uuid,p_correlation text
) RETURNS bigint LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $fn$
DECLARE v_identity learning.session_preparations%ROWTYPE;v_previous bigint;v_next bigint;
BEGIN
 IF p_center IS NULL OR btrim(p_center)='' OR p_session IS NULL
  OR p_session='00000000-0000-0000-0000-000000000000'::uuid
  OR p_class IS NULL OR p_class='00000000-0000-0000-0000-000000000000'::uuid
  OR p_expected IS NULL OR p_expected<0 OR p_title IS NULL
  OR length(btrim(p_title)) NOT BETWEEN 1 AND 160
  OR p_outline IS NULL OR length(p_outline)>4000
  OR p_materials IS NULL OR length(p_materials)>2000
  OR p_actor IS NULL OR btrim(p_actor)='' OR p_audit IS NULL
  OR p_event IS NULL OR p_correlation IS NULL OR btrim(p_correlation)='' THEN
  RAISE EXCEPTION 'Invalid private session preparation' USING ERRCODE='22023';
 END IF;
 IF p_center IS DISTINCT FROM NULLIF(current_setting('kanoonyar.center_id',true),'')
  OR NOT EXISTS(SELECT 1 FROM centers.memberships m
   JOIN centers.registry c ON c.center_id=m.center_id
   WHERE m.center_id=p_center AND m.subject_id=p_actor AND m.is_active AND c.is_active) THEN
  RAISE EXCEPTION 'Center scope or active membership absent' USING ERRCODE='42501';
 END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('session-prep:'||p_center||':'||p_session::text,0));
 IF NOT EXISTS(SELECT 1 FROM learning.class_sessions s
  WHERE s.center_id=p_center AND s.session_id=p_session AND s.class_id=p_class) THEN
  RAISE EXCEPTION 'Exact same-center class session source absent' USING ERRCODE='23503';
 END IF;
 SELECT * INTO v_identity FROM learning.session_preparations
  WHERE center_id=p_center AND session_id=p_session;
 IF p_expected=0 THEN
  IF FOUND THEN RAISE EXCEPTION 'Preparation already exists' USING ERRCODE='40001'; END IF;
  INSERT INTO learning.session_preparations(center_id,session_id,class_id,created_by_subject)
  VALUES(p_center,p_session,p_class,p_actor);
  v_previous:=0;
 ELSE
  IF NOT FOUND OR v_identity.class_id<>p_class THEN
   RAISE EXCEPTION 'Immutable session class association mismatch' USING ERRCODE='23503';
  END IF;
  SELECT COALESCE(max(revision),0) INTO v_previous
  FROM learning.session_preparation_revisions
  WHERE center_id=p_center AND session_id=p_session;
 END IF;
 IF v_previous<>p_expected THEN
  RAISE EXCEPTION 'Stale session preparation revision' USING ERRCODE='40001'; END IF;
 v_next:=v_previous+1;
 INSERT INTO learning.session_preparation_revisions(center_id,session_id,revision,
  preparation_title,teaching_outline,materials_note,changed_by_subject)
 VALUES(p_center,p_session,v_next,btrim(p_title),p_outline,p_materials,p_actor);
 INSERT INTO governance.command_audit(audit_id,occurred_at,actor_subject_id,center_id,
  action_code,target_type,target_id,correlation_id,metadata)
 VALUES(p_audit,transaction_timestamp(),p_actor,p_center,
  'SESSION_PREPARATION_REVISION_APPENDED','class_session_preparation',p_session::text,
  p_correlation,jsonb_build_object('revision',v_next,'class_id',p_class,
  'visibility','PRIVATE_PREPARATION','delivery_verified',false));
 INSERT INTO governance.outbox_events(event_id,audit_id,event_type,aggregate_type,
  aggregate_id,occurred_at,payload)
 VALUES(p_event,p_audit,'SessionPreparationRevisionAppended','class_session_preparation',
  p_session::text,transaction_timestamp(),jsonb_build_object(
   'center_id',p_center,'session_id',p_session,'class_id',p_class,
   'revision',v_next,'visibility','PRIVATE_PREPARATION','delivery_verified',false,
   'is_published',false,'is_assessed',false));
 RETURN v_next;
END;$fn$;
ALTER FUNCTION learning.append_session_preparation(
 text,uuid,uuid,bigint,text,text,text,text,uuid,uuid,text)
 OWNER TO kanoonyar_session_prep_owner;
REVOKE ALL ON FUNCTION learning.append_session_preparation(
 text,uuid,uuid,bigint,text,text,text,text,uuid,uuid,text) FROM PUBLIC;
COMMENT ON TABLE learning.session_preparation_revisions IS
 'Internal educator-authored private planning only; never a delivered session, student task, or grade';
COMMIT;
