-- Sprint 19: request-only human-review requirements. No positive eligibility,
-- consent/guardian verdict, enrollment or seat reservation is possible here.
BEGIN;
CREATE TABLE learning.registration_review_requests(
 center_id text NOT NULL,
 review_request_id uuid NOT NULL,
 registration_request_id uuid NOT NULL,
 review_topic text NOT NULL CHECK(review_topic IN(
  'IDENTITY_AUTHORITY','ELIGIBILITY_CRITERIA','CONSENT_AUTHORITY','ADMISSION_POLICY')),
 review_status text NOT NULL DEFAULT 'REVIEW_REQUIRED'
   CHECK(review_status='REVIEW_REQUIRED'),
 raised_by_subject text NOT NULL CHECK(length(btrim(raised_by_subject))>0),
 recorded_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
 PRIMARY KEY(center_id,review_request_id),
 UNIQUE(center_id,registration_request_id,review_topic),
 CONSTRAINT review_exact_registration_fk FOREIGN KEY(center_id,registration_request_id)
  REFERENCES learning.class_registration_intents(center_id,request_id) ON DELETE RESTRICT
);
CREATE INDEX registration_review_parent_idx
 ON learning.registration_review_requests(center_id,registration_request_id,recorded_at);
CREATE TRIGGER registration_review_append_only BEFORE UPDATE OR DELETE
 ON learning.registration_review_requests FOR EACH ROW
 EXECUTE FUNCTION programs.reject_draft_revision_mutation();
REVOKE ALL ON learning.registration_review_requests FROM PUBLIC;
ALTER TABLE learning.registration_review_requests ENABLE ROW LEVEL SECURITY;
ALTER TABLE learning.registration_review_requests FORCE ROW LEVEL SECURITY;
CREATE POLICY review_center_scope ON learning.registration_review_requests
 USING(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));

DO $roles$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_registration_review_owner') THEN
   CREATE ROLE kanoonyar_registration_review_owner NOLOGIN NOINHERIT NOSUPERUSER
    NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION;
 END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_registration_review_owner'
  AND NOT rolcanlogin AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb
  AND NOT rolcreaterole AND NOT rolreplication) THEN
    RAISE EXCEPTION 'Unsafe registration review function owner'; END IF;
END;$roles$;
GRANT USAGE ON SCHEMA learning,centers,governance TO kanoonyar_registration_review_owner;
GRANT SELECT ON learning.class_registration_intents,centers.memberships,centers.registry
 TO kanoonyar_registration_review_owner;
GRANT INSERT ON learning.registration_review_requests,
 governance.command_audit,governance.outbox_events TO kanoonyar_registration_review_owner;

CREATE FUNCTION learning.request_registration_human_review(
 p_center text,p_class uuid,p_request uuid,p_review uuid,p_topic text,
 p_actor text,p_audit uuid,p_event uuid,p_correlation text
) RETURNS uuid LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $fn$
BEGIN
 IF p_center IS NULL OR btrim(p_center)='' OR p_class IS NULL OR p_request IS NULL
   OR p_review IS NULL OR p_topic IS NULL OR p_topic NOT IN(
      'IDENTITY_AUTHORITY','ELIGIBILITY_CRITERIA','CONSENT_AUTHORITY','ADMISSION_POLICY')
   OR p_actor IS NULL OR btrim(p_actor)='' OR p_audit IS NULL OR p_event IS NULL
   OR p_correlation IS NULL OR btrim(p_correlation)='' THEN
   RAISE EXCEPTION 'Invalid registration human-review request' USING ERRCODE='22023';
 END IF;
 IF p_center IS DISTINCT FROM NULLIF(current_setting('kanoonyar.center_id',true),'')
   OR NOT EXISTS(SELECT 1 FROM centers.memberships m JOIN centers.registry c
       ON c.center_id=m.center_id
       WHERE m.center_id=p_center AND m.subject_id=p_actor AND m.is_active AND c.is_active) THEN
   RAISE EXCEPTION 'Center scope or active membership absent' USING ERRCODE='42501';
 END IF;
 IF NOT EXISTS(SELECT 1 FROM learning.class_registration_intents i
      WHERE i.center_id=p_center AND i.class_id=p_class
        AND i.request_id=p_request AND i.state='REQUESTED') THEN
   RAISE EXCEPTION 'Unconfirmed exact registration request absent' USING ERRCODE='23503';
 END IF;
 INSERT INTO learning.registration_review_requests(
  center_id,review_request_id,registration_request_id,review_topic,review_status,raised_by_subject)
 VALUES(p_center,p_review,p_request,p_topic,'REVIEW_REQUIRED',p_actor);
 INSERT INTO governance.command_audit(
  audit_id,occurred_at,actor_subject_id,center_id,action_code,target_type,target_id,
  correlation_id,metadata)
 VALUES(p_audit,transaction_timestamp(),p_actor,p_center,
  'REGISTRATION_HUMAN_REVIEW_REQUESTED','registration_review_request',p_review::text,
  p_correlation,jsonb_build_object('registration_request_id',p_request,
     'class_id',p_class,'review_topic',p_topic,'review_status','REVIEW_REQUIRED'));
 INSERT INTO governance.outbox_events(
  event_id,audit_id,event_type,aggregate_type,aggregate_id,occurred_at,payload)
 VALUES(p_event,p_audit,'RegistrationHumanReviewRequested',
  'registration_review_request',p_review::text,transaction_timestamp(),
  jsonb_build_object('center_id',p_center,'class_id',p_class,
   'registration_request_id',p_request,'review_request_id',p_review,
   'review_topic',p_topic,'review_status','REVIEW_REQUIRED',
   'enrollment_decision',null,'seat_reserved',false));
 RETURN p_review;
END;$fn$;
ALTER FUNCTION learning.request_registration_human_review(
 text,uuid,uuid,uuid,text,text,uuid,uuid,text)
 OWNER TO kanoonyar_registration_review_owner;
REVOKE ALL ON FUNCTION learning.request_registration_human_review(
 text,uuid,uuid,uuid,text,text,uuid,uuid,text) FROM PUBLIC;
COMMENT ON TABLE learning.registration_review_requests IS
 'A request for human verification, not evidence, consent, eligibility decision, applicant identity proof, enrollment or seat.';
COMMIT;
