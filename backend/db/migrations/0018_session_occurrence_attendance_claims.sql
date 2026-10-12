-- Sprint 14: immutable, UNVERIFIED assertions about a planned session.
-- Occurrence evidence and attendance claim are NOT verified delivery or identity.
-- No actual participant PII, registration or unapproved public permission is added.
BEGIN;
CREATE TABLE learning.session_occurrence_reports (
  center_id text NOT NULL,
  occurrence_report_id uuid NOT NULL,
  session_id uuid NOT NULL,
  reported_note text NOT NULL CHECK(length(btrim(reported_note))>0),
  evidence_ref text NULL CHECK(evidence_ref IS NULL OR length(btrim(evidence_ref))>0),
  claimed_started_at timestamptz NULL,
  claimed_ended_at timestamptz NULL,
  actor_subject text NOT NULL CHECK(length(btrim(actor_subject))>0),
  recorded_at timestamptz NOT NULL,
  CONSTRAINT claimed_time_order CHECK(
    claimed_started_at IS NULL OR claimed_ended_at IS NULL
    OR claimed_ended_at>=claimed_started_at
  ),
  PRIMARY KEY(center_id,occurrence_report_id),
  CONSTRAINT occurrence_center_session_unique UNIQUE(center_id,occurrence_report_id,session_id),
  CONSTRAINT occurrence_exact_session_fk FOREIGN KEY(center_id,session_id)
   REFERENCES learning.class_sessions(center_id,session_id) ON DELETE RESTRICT
);
CREATE INDEX session_occurrence_page_idx
 ON learning.session_occurrence_reports(center_id,session_id,recorded_at DESC,occurrence_report_id DESC);

CREATE TABLE learning.session_attendance_claims (
  center_id text NOT NULL,
  claim_id uuid NOT NULL,
  occurrence_report_id uuid NOT NULL,
  session_id uuid NOT NULL,
  participant_ref text NOT NULL CHECK(length(btrim(participant_ref))>0),
  reported_presence text NOT NULL
    CHECK(reported_presence IN ('REPORTED_PRESENT','REPORTED_ABSENT')),
  actor_subject text NOT NULL CHECK(length(btrim(actor_subject))>0),
  recorded_at timestamptz NOT NULL,
  PRIMARY KEY(center_id,claim_id),
  CONSTRAINT one_participant_report_per_occurrence
    UNIQUE(center_id,occurrence_report_id,participant_ref),
  CONSTRAINT attendance_exact_occurrence_fk FOREIGN KEY
    (center_id,occurrence_report_id,session_id)
    REFERENCES learning.session_occurrence_reports
      (center_id,occurrence_report_id,session_id) ON DELETE RESTRICT
);
CREATE INDEX attendance_occurrence_idx
 ON learning.session_attendance_claims(center_id,occurrence_report_id,claim_id);

CREATE TRIGGER session_occurrence_append_only BEFORE UPDATE OR DELETE
 ON learning.session_occurrence_reports FOR EACH ROW
 EXECUTE FUNCTION programs.reject_draft_revision_mutation();
CREATE TRIGGER session_attendance_append_only BEFORE UPDATE OR DELETE
 ON learning.session_attendance_claims FOR EACH ROW
 EXECUTE FUNCTION programs.reject_draft_revision_mutation();
REVOKE ALL ON TABLE learning.session_occurrence_reports,learning.session_attendance_claims FROM PUBLIC;
ALTER TABLE learning.session_occurrence_reports ENABLE ROW LEVEL SECURITY;
ALTER TABLE learning.session_occurrence_reports FORCE ROW LEVEL SECURITY;
ALTER TABLE learning.session_attendance_claims ENABLE ROW LEVEL SECURITY;
ALTER TABLE learning.session_attendance_claims FORCE ROW LEVEL SECURITY;
CREATE POLICY occurrence_center_scope ON learning.session_occurrence_reports
 USING(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));
CREATE POLICY attendance_center_scope ON learning.session_attendance_claims
 USING(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));

-- Reuse Sprint 13's audited command owner; no runtime grants to the functions.
GRANT SELECT ON learning.class_sessions,learning.session_occurrence_reports
 TO kanoonyar_learning_command_owner;
GRANT INSERT ON learning.session_occurrence_reports,learning.session_attendance_claims
 TO kanoonyar_learning_command_owner;

CREATE FUNCTION learning.record_session_occurrence_claim(
  p_center text,p_occurrence uuid,p_session uuid,p_note text,p_evidence text,
  p_started timestamptz,p_ended timestamptz,p_actor text,
  p_audit uuid,p_event uuid,p_correlation text
) RETURNS uuid LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $fn$
BEGIN
 IF p_center IS NULL OR btrim(p_center)='' OR p_occurrence IS NULL
    OR p_session IS NULL OR p_note IS NULL OR btrim(p_note)=''
    OR (p_evidence IS NOT NULL AND btrim(p_evidence)='')
    OR p_actor IS NULL OR btrim(p_actor)='' OR
    p_audit IS NULL OR p_event IS NULL OR p_correlation IS NULL
    OR btrim(p_correlation)='' OR
    (p_started IS NOT NULL AND p_ended IS NOT NULL AND p_ended<p_started) THEN
   RAISE EXCEPTION 'Invalid unverified occurrence report' USING ERRCODE='22023';
 END IF;
 IF p_center IS DISTINCT FROM NULLIF(current_setting('kanoonyar.center_id',true),'')
    OR NOT EXISTS(SELECT 1 FROM centers.memberships m JOIN centers.registry c
      ON c.center_id=m.center_id WHERE m.center_id=p_center
      AND m.subject_id=p_actor AND m.is_active AND c.is_active) THEN
   RAISE EXCEPTION 'Center scope or active membership absent' USING ERRCODE='42501';
 END IF;
 INSERT INTO learning.session_occurrence_reports(
   center_id,occurrence_report_id,session_id,reported_note,evidence_ref,
   claimed_started_at,claimed_ended_at,actor_subject,recorded_at)
 VALUES(p_center,p_occurrence,p_session,p_note,p_evidence,
   p_started,p_ended,p_actor,transaction_timestamp());
 INSERT INTO governance.command_audit(
   audit_id,occurred_at,actor_subject_id,center_id,action_code,
   target_type,target_id,correlation_id,metadata)
 VALUES(p_audit,transaction_timestamp(),p_actor,p_center,
   'SESSION_OCCURRENCE_CLAIM_RECORDED','session_occurrence_claim',
   p_occurrence::text,p_correlation,
   jsonb_build_object('session_id',p_session,'verification_status','UNVERIFIED'));
 INSERT INTO governance.outbox_events(
   event_id,audit_id,event_type,aggregate_type,aggregate_id,occurred_at,payload)
 VALUES(p_event,p_audit,'SessionOccurrenceClaimRecorded','session_occurrence_claim',
   p_occurrence::text,transaction_timestamp(),
   jsonb_build_object('center_id',p_center,'session_id',p_session,
   'occurrence_report_id',p_occurrence,'verification_status','UNVERIFIED'));
 RETURN p_occurrence;
END;$fn$;
ALTER FUNCTION learning.record_session_occurrence_claim(
 text,uuid,uuid,text,text,timestamptz,timestamptz,text,uuid,uuid,text)
 OWNER TO kanoonyar_learning_command_owner;
REVOKE ALL ON FUNCTION learning.record_session_occurrence_claim(
 text,uuid,uuid,text,text,timestamptz,timestamptz,text,uuid,uuid,text) FROM PUBLIC;

CREATE FUNCTION learning.record_session_attendance_claim(
 p_center text,p_claim uuid,p_occurrence uuid,p_session uuid,
 p_participant_ref text,p_reported_presence text,p_actor text,
 p_audit uuid,p_event uuid,p_correlation text
) RETURNS uuid LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $fn$
BEGIN
 IF p_center IS NULL OR btrim(p_center)='' OR p_claim IS NULL
    OR p_occurrence IS NULL OR p_session IS NULL
    OR p_participant_ref IS NULL OR btrim(p_participant_ref)=''
    OR p_reported_presence NOT IN ('REPORTED_PRESENT','REPORTED_ABSENT')
    OR p_reported_presence IS NULL
    OR p_actor IS NULL OR btrim(p_actor)='' OR
    p_audit IS NULL OR p_event IS NULL OR p_correlation IS NULL
    OR btrim(p_correlation)='' THEN
   RAISE EXCEPTION 'Invalid unverified attendance claim' USING ERRCODE='22023';
 END IF;
 IF p_center IS DISTINCT FROM NULLIF(current_setting('kanoonyar.center_id',true),'')
    OR NOT EXISTS(SELECT 1 FROM centers.memberships m JOIN centers.registry c
      ON c.center_id=m.center_id WHERE m.center_id=p_center
      AND m.subject_id=p_actor AND m.is_active AND c.is_active) THEN
   RAISE EXCEPTION 'Center scope or active membership absent' USING ERRCODE='42501';
 END IF;
 INSERT INTO learning.session_attendance_claims(
   center_id,claim_id,occurrence_report_id,session_id,
   participant_ref,reported_presence,actor_subject,recorded_at)
 VALUES(p_center,p_claim,p_occurrence,p_session,p_participant_ref,
   p_reported_presence,p_actor,transaction_timestamp());
 INSERT INTO governance.command_audit(
   audit_id,occurred_at,actor_subject_id,center_id,action_code,
   target_type,target_id,correlation_id,metadata)
 VALUES(p_audit,transaction_timestamp(),p_actor,p_center,
   'SESSION_ATTENDANCE_CLAIM_RECORDED','session_attendance_claim',
   p_claim::text,p_correlation,jsonb_build_object(
     'session_id',p_session,'occurrence_report_id',p_occurrence,
     'verification_status','UNVERIFIED'));
 INSERT INTO governance.outbox_events(
   event_id,audit_id,event_type,aggregate_type,aggregate_id,occurred_at,payload)
 VALUES(p_event,p_audit,'SessionAttendanceClaimRecorded','session_attendance_claim',
   p_claim::text,transaction_timestamp(),jsonb_build_object(
     'center_id',p_center,'claim_id',p_claim,'session_id',p_session,
     'occurrence_report_id',p_occurrence,'verification_status','UNVERIFIED'));
 RETURN p_claim;
END;$fn$;
ALTER FUNCTION learning.record_session_attendance_claim(
 text,uuid,uuid,uuid,text,text,text,uuid,uuid,text)
 OWNER TO kanoonyar_learning_command_owner;
REVOKE ALL ON FUNCTION learning.record_session_attendance_claim(
 text,uuid,uuid,uuid,text,text,text,uuid,uuid,text) FROM PUBLIC;
COMMIT;
