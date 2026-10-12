-- Sprint 18: capacity planning and unconfirmed registration-intent intake.
-- No enrollment confirmation, seat occupation, public portal, consent or eligibility decision.
BEGIN;

CREATE TABLE learning.class_capacity_revisions(
 center_id text NOT NULL,
 class_id uuid NOT NULL,
 revision bigint NOT NULL CHECK(revision>0),
 capacity integer NOT NULL CHECK(capacity>0),
 actor_subject text NOT NULL CHECK(length(btrim(actor_subject))>0),
 recorded_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
 PRIMARY KEY(center_id,class_id,revision),
 FOREIGN KEY(center_id,class_id) REFERENCES learning.center_classes(center_id,class_id) ON DELETE RESTRICT
);
CREATE INDEX class_capacity_latest_idx
 ON learning.class_capacity_revisions(center_id,class_id,revision DESC);

CREATE TABLE learning.class_registration_intents(
 center_id text NOT NULL,
 request_id uuid NOT NULL,
 class_id uuid NOT NULL,
 applicant_ref uuid NOT NULL,
 capacity_revision bigint NOT NULL,
 state text NOT NULL DEFAULT 'REQUESTED' CHECK(state='REQUESTED'),
 submitted_by_subject text NOT NULL CHECK(length(btrim(submitted_by_subject))>0),
 recorded_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
 PRIMARY KEY(center_id,request_id),
 UNIQUE(center_id,class_id,applicant_ref),
 FOREIGN KEY(center_id,class_id,capacity_revision)
  REFERENCES learning.class_capacity_revisions(center_id,class_id,revision) ON DELETE RESTRICT
);
CREATE INDEX class_registration_intents_class_idx
 ON learning.class_registration_intents(center_id,class_id,recorded_at DESC,request_id DESC);

CREATE TRIGGER class_capacity_append_only BEFORE UPDATE OR DELETE
 ON learning.class_capacity_revisions FOR EACH ROW
 EXECUTE FUNCTION programs.reject_draft_revision_mutation();
CREATE TRIGGER class_registration_intent_append_only BEFORE UPDATE OR DELETE
 ON learning.class_registration_intents FOR EACH ROW
 EXECUTE FUNCTION programs.reject_draft_revision_mutation();

REVOKE ALL ON TABLE learning.class_capacity_revisions,learning.class_registration_intents FROM PUBLIC;
ALTER TABLE learning.class_capacity_revisions ENABLE ROW LEVEL SECURITY;
ALTER TABLE learning.class_capacity_revisions FORCE ROW LEVEL SECURITY;
ALTER TABLE learning.class_registration_intents ENABLE ROW LEVEL SECURITY;
ALTER TABLE learning.class_registration_intents FORCE ROW LEVEL SECURITY;
CREATE POLICY class_capacity_center_scope ON learning.class_capacity_revisions
 USING(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));
CREATE POLICY class_registration_center_scope ON learning.class_registration_intents
 USING(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));

DO $role$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_capacity_intake_owner') THEN
   CREATE ROLE kanoonyar_capacity_intake_owner NOLOGIN NOINHERIT
    NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION;
 END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_capacity_intake_owner'
  AND NOT rolcanlogin AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreaterole AND NOT rolcreatedb
  AND NOT rolreplication) THEN RAISE EXCEPTION 'Unsafe intake owner'; END IF;
END;$role$;
GRANT USAGE ON SCHEMA learning,centers,governance TO kanoonyar_capacity_intake_owner;
GRANT SELECT ON centers.memberships,centers.registry,learning.center_classes
 TO kanoonyar_capacity_intake_owner;
GRANT SELECT,INSERT ON learning.class_capacity_revisions TO kanoonyar_capacity_intake_owner;
GRANT INSERT ON learning.class_registration_intents TO kanoonyar_capacity_intake_owner;
GRANT INSERT ON governance.command_audit,governance.outbox_events TO kanoonyar_capacity_intake_owner;

CREATE FUNCTION learning.append_class_capacity_revision(
 p_center text,p_class uuid,p_expected bigint,p_capacity integer,p_actor text,
 p_audit uuid,p_event uuid,p_correlation text)
RETURNS bigint LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $fn$
DECLARE v_latest bigint;
BEGIN
 IF p_center IS NULL OR btrim(p_center)='' OR p_class IS NULL
   OR p_expected IS NULL OR p_expected<0 OR p_capacity IS NULL OR p_capacity<=0
   OR p_actor IS NULL OR btrim(p_actor)='' OR p_audit IS NULL OR p_event IS NULL
   OR p_correlation IS NULL OR btrim(p_correlation)='' THEN
   RAISE EXCEPTION 'Invalid capacity revision' USING ERRCODE='22023';
 END IF;
 IF p_center IS DISTINCT FROM NULLIF(current_setting('kanoonyar.center_id',true),'')
   OR NOT EXISTS(SELECT 1 FROM centers.memberships m JOIN centers.registry c
    ON c.center_id=m.center_id WHERE m.center_id=p_center AND m.subject_id=p_actor
    AND m.is_active AND c.is_active) THEN
   RAISE EXCEPTION 'Center scope or active membership absent' USING ERRCODE='42501';
 END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('class-capacity:'||p_center||':'||p_class::text,0));
 IF NOT EXISTS(SELECT 1 FROM learning.center_classes c
     WHERE c.center_id=p_center AND c.class_id=p_class) THEN
   RAISE EXCEPTION 'Unknown class' USING ERRCODE='23503';
 END IF;
 SELECT COALESCE(max(revision),0) INTO v_latest
  FROM learning.class_capacity_revisions WHERE center_id=p_center AND class_id=p_class;
 IF v_latest<>p_expected THEN
   RAISE EXCEPTION 'Stale class capacity revision' USING ERRCODE='40001';
 END IF;
 INSERT INTO learning.class_capacity_revisions(center_id,class_id,revision,capacity,actor_subject)
 VALUES(p_center,p_class,v_latest+1,p_capacity,p_actor);
 INSERT INTO governance.command_audit(audit_id,occurred_at,actor_subject_id,center_id,
   action_code,target_type,target_id,correlation_id,metadata)
 VALUES(p_audit,transaction_timestamp(),p_actor,p_center,'CLASS_CAPACITY_REVISION_APPENDED',
   'class_capacity_revision',p_class::text,p_correlation,
   jsonb_build_object('revision',v_latest+1,'capacity',p_capacity));
 INSERT INTO governance.outbox_events(event_id,audit_id,event_type,aggregate_type,
   aggregate_id,occurred_at,payload)
 VALUES(p_event,p_audit,'ClassCapacityRevisionAppended','class_capacity_revision',
   p_class::text,transaction_timestamp(),
   jsonb_build_object('center_id',p_center,'class_id',p_class,'revision',v_latest+1,
     'configured_capacity',p_capacity,'confirmed_enrollment_count',null));
 RETURN v_latest+1;
END;$fn$;
ALTER FUNCTION learning.append_class_capacity_revision(text,uuid,bigint,integer,text,uuid,uuid,text)
 OWNER TO kanoonyar_capacity_intake_owner;
REVOKE ALL ON FUNCTION learning.append_class_capacity_revision(text,uuid,bigint,integer,text,uuid,uuid,text) FROM PUBLIC;

CREATE FUNCTION learning.record_class_registration_intent(
 p_center text,p_request uuid,p_class uuid,p_applicant uuid,p_actor text,
 p_audit uuid,p_event uuid,p_correlation text)
RETURNS bigint LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $fn$
DECLARE v_revision bigint;
BEGIN
 IF p_center IS NULL OR btrim(p_center)='' OR p_request IS NULL
   OR p_class IS NULL OR p_applicant IS NULL OR p_actor IS NULL OR btrim(p_actor)=''
   OR p_audit IS NULL OR p_event IS NULL OR p_correlation IS NULL
   OR btrim(p_correlation)='' THEN
   RAISE EXCEPTION 'Invalid registration request' USING ERRCODE='22023';
 END IF;
 IF p_center IS DISTINCT FROM NULLIF(current_setting('kanoonyar.center_id',true),'')
   OR NOT EXISTS(SELECT 1 FROM centers.memberships m JOIN centers.registry c
    ON c.center_id=m.center_id WHERE m.center_id=p_center AND m.subject_id=p_actor
    AND m.is_active AND c.is_active) THEN
   RAISE EXCEPTION 'Center scope or active membership absent' USING ERRCODE='42501';
 END IF;
 -- Pin a real, explicitly configured capacity revision but do NOT take a seat.
 PERFORM pg_advisory_xact_lock(hashtextextended('class-capacity:'||p_center||':'||p_class::text,0));
 SELECT max(revision) INTO v_revision FROM learning.class_capacity_revisions
   WHERE center_id=p_center AND class_id=p_class;
 IF v_revision IS NULL THEN
   RAISE EXCEPTION 'Class capacity not configured' USING ERRCODE='23514';
 END IF;
 INSERT INTO learning.class_registration_intents(
  center_id,request_id,class_id,applicant_ref,capacity_revision,state,submitted_by_subject)
 VALUES(p_center,p_request,p_class,p_applicant,v_revision,'REQUESTED',p_actor);
 INSERT INTO governance.command_audit(audit_id,occurred_at,actor_subject_id,center_id,
  action_code,target_type,target_id,correlation_id,metadata)
 VALUES(p_audit,transaction_timestamp(),p_actor,p_center,'CLASS_REGISTRATION_INTENT_REQUESTED',
  'class_registration_intent',p_request::text,p_correlation,
  jsonb_build_object('class_id',p_class,'capacity_revision',v_revision,'state','REQUESTED'));
 INSERT INTO governance.outbox_events(event_id,audit_id,event_type,aggregate_type,
  aggregate_id,occurred_at,payload)
 VALUES(p_event,p_audit,'ClassRegistrationIntentRequested','class_registration_intent',
  p_request::text,transaction_timestamp(),jsonb_build_object(
  'center_id',p_center,'class_id',p_class,'request_id',p_request,
  'capacity_revision',v_revision,'state','REQUESTED','seat_reserved',false));
 RETURN v_revision;
END;$fn$;
ALTER FUNCTION learning.record_class_registration_intent(text,uuid,uuid,uuid,text,uuid,uuid,text)
 OWNER TO kanoonyar_capacity_intake_owner;
REVOKE ALL ON FUNCTION learning.record_class_registration_intent(text,uuid,uuid,uuid,text,uuid,uuid,text) FROM PUBLIC;
COMMIT;
