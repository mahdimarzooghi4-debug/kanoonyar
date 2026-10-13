-- S26: human clarification request/response lineage, never a verification workflow.
BEGIN;
ALTER TABLE campaigns.campaign_report_inspection_notes
 ADD CONSTRAINT campaign_inspection_exact_source UNIQUE(center_id,note_id,campaign_id,association_id);

CREATE TABLE campaigns.inspection_followup_requests(
 center_id text NOT NULL,
 request_id uuid NOT NULL,
 campaign_id uuid NOT NULL,
 association_id uuid NOT NULL,
 note_id uuid NOT NULL,
 question_text text NOT NULL CHECK(length(btrim(question_text)) BETWEEN 1 AND 1000),
 requested_by_subject text NOT NULL CHECK(length(btrim(requested_by_subject))>0),
 requested_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
 PRIMARY KEY(center_id,request_id),
 UNIQUE(center_id,request_id,campaign_id,association_id,note_id),
 FOREIGN KEY(center_id,note_id,campaign_id,association_id)
  REFERENCES campaigns.campaign_report_inspection_notes(center_id,note_id,campaign_id,association_id) ON DELETE RESTRICT
);
CREATE TABLE campaigns.inspection_followup_responses(
 center_id text NOT NULL,
 response_id uuid NOT NULL,
 request_id uuid NOT NULL,
 campaign_id uuid NOT NULL,
 association_id uuid NOT NULL,
 note_id uuid NOT NULL,
 response_text text NOT NULL CHECK(length(btrim(response_text)) BETWEEN 1 AND 1000),
 responded_by_subject text NOT NULL CHECK(length(btrim(responded_by_subject))>0),
 responded_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
 PRIMARY KEY(center_id,response_id),
 FOREIGN KEY(center_id,request_id,campaign_id,association_id,note_id)
  REFERENCES campaigns.inspection_followup_requests(center_id,request_id,campaign_id,association_id,note_id)
  ON DELETE RESTRICT
);
CREATE INDEX followup_request_history ON campaigns.inspection_followup_requests
 (center_id,campaign_id,association_id,note_id,request_id DESC);
CREATE INDEX followup_response_history ON campaigns.inspection_followup_responses
 (center_id,campaign_id,association_id,note_id,request_id,response_id DESC);
CREATE TRIGGER followup_request_immutable BEFORE UPDATE OR DELETE ON campaigns.inspection_followup_requests
 FOR EACH ROW EXECUTE FUNCTION programs.reject_draft_revision_mutation();
CREATE TRIGGER followup_response_immutable BEFORE UPDATE OR DELETE ON campaigns.inspection_followup_responses
 FOR EACH ROW EXECUTE FUNCTION programs.reject_draft_revision_mutation();
REVOKE ALL ON campaigns.inspection_followup_requests,campaigns.inspection_followup_responses FROM PUBLIC;
ALTER TABLE campaigns.inspection_followup_requests ENABLE ROW LEVEL SECURITY;
ALTER TABLE campaigns.inspection_followup_requests FORCE ROW LEVEL SECURITY;
ALTER TABLE campaigns.inspection_followup_responses ENABLE ROW LEVEL SECURITY;
ALTER TABLE campaigns.inspection_followup_responses FORCE ROW LEVEL SECURITY;
CREATE POLICY followup_requests_center ON campaigns.inspection_followup_requests
 USING(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));
CREATE POLICY followup_responses_center ON campaigns.inspection_followup_responses
 USING(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));
DO $role$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_campaign_followup_owner') THEN
  CREATE ROLE kanoonyar_campaign_followup_owner NOLOGIN NOINHERIT NOSUPERUSER NOBYPASSRLS
   NOCREATEDB NOCREATEROLE NOREPLICATION;
 END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_campaign_followup_owner'
  AND NOT rolcanlogin AND NOT rolsuper AND NOT rolbypassrls
  AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication)
 THEN RAISE EXCEPTION 'Unsafe followup function owner'; END IF;
END;$role$;
GRANT USAGE ON SCHEMA campaigns,centers,governance,programs TO kanoonyar_campaign_followup_owner;
GRANT SELECT ON centers.registry,centers.memberships,campaigns.campaign_report_inspection_notes
 TO kanoonyar_campaign_followup_owner;
GRANT SELECT,INSERT ON campaigns.inspection_followup_requests,campaigns.inspection_followup_responses
 TO kanoonyar_campaign_followup_owner;
GRANT INSERT ON governance.command_audit,governance.outbox_events TO kanoonyar_campaign_followup_owner;

CREATE FUNCTION campaigns.append_inspection_followup(
 p_center text,p_campaign uuid,p_association uuid,p_note uuid,
 p_entry uuid,p_request uuid,p_kind text,p_content text,p_actor text,
 p_audit uuid,p_event uuid,p_correlation text
) RETURNS uuid LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $fn$
BEGIN
 IF p_center IS NULL OR btrim(p_center)='' OR p_campaign IS NULL
  OR p_association IS NULL OR p_note IS NULL OR p_entry IS NULL
  OR p_kind IS NULL OR p_kind NOT IN ('REQUEST','RESPONSE')
  OR (p_kind='REQUEST' AND p_request IS NOT NULL)
  OR (p_kind='RESPONSE' AND p_request IS NULL)
  OR p_content IS NULL OR length(btrim(p_content)) NOT BETWEEN 1 AND 1000
  OR p_actor IS NULL OR btrim(p_actor)='' OR p_audit IS NULL OR p_event IS NULL
  OR p_correlation IS NULL OR btrim(p_correlation)=''
 THEN RAISE EXCEPTION 'Invalid clarification entry' USING ERRCODE='22023'; END IF;
 IF p_center IS DISTINCT FROM NULLIF(current_setting('kanoonyar.center_id',true),'')
  OR NOT EXISTS(SELECT 1 FROM centers.memberships m JOIN centers.registry c
   ON c.center_id=m.center_id WHERE m.center_id=p_center AND m.subject_id=p_actor
    AND m.is_active AND c.is_active)
 THEN RAISE EXCEPTION 'Center scope or active membership absent' USING ERRCODE='42501'; END IF;
 IF NOT EXISTS(SELECT 1 FROM campaigns.campaign_report_inspection_notes n
   WHERE n.center_id=p_center AND n.campaign_id=p_campaign
    AND n.association_id=p_association AND n.note_id=p_note)
 THEN RAISE EXCEPTION 'Unknown inspection note' USING ERRCODE='23503'; END IF;
 IF p_kind='REQUEST' THEN
  INSERT INTO campaigns.inspection_followup_requests(
   center_id,request_id,campaign_id,association_id,note_id,question_text,requested_by_subject)
  VALUES(p_center,p_entry,p_campaign,p_association,p_note,btrim(p_content),p_actor);
 ELSE
  INSERT INTO campaigns.inspection_followup_responses(
   center_id,response_id,request_id,campaign_id,association_id,note_id,response_text,responded_by_subject)
  VALUES(p_center,p_entry,p_request,p_campaign,p_association,p_note,btrim(p_content),p_actor);
 END IF;
 INSERT INTO governance.command_audit(
  audit_id,occurred_at,actor_subject_id,center_id,action_code,target_type,target_id,correlation_id,metadata)
 VALUES(p_audit,transaction_timestamp(),p_actor,p_center,'CAMPAIGN_INSPECTION_FOLLOWUP_'||p_kind,
  'campaign_inspection_followup',p_entry::text,p_correlation,
  jsonb_build_object('campaign_id',p_campaign,'association_id',p_association,'note_id',p_note,
   'request_id',p_request,'entry_kind',p_kind,'verification_status','UNVERIFIED','is_approval',false));
 INSERT INTO governance.outbox_events(event_id,audit_id,event_type,aggregate_type,aggregate_id,occurred_at,payload)
 VALUES(p_event,p_audit,'CampaignInspectionFollowup'||initcap(lower(p_kind))||'Recorded',
  'campaign_inspection_followup',p_entry::text,transaction_timestamp(),
  jsonb_build_object('center_id',p_center,'campaign_id',p_campaign,'association_id',p_association,
   'note_id',p_note,'entry_id',p_entry,'request_id',p_request,'entry_kind',p_kind,
   'verification_status','UNVERIFIED','is_approval',false,'is_executed',false));
 RETURN p_entry;
END;$fn$;
ALTER FUNCTION campaigns.append_inspection_followup(
 text,uuid,uuid,uuid,uuid,uuid,text,text,text,uuid,uuid,text)
 OWNER TO kanoonyar_campaign_followup_owner;
REVOKE ALL ON FUNCTION campaigns.append_inspection_followup(
 text,uuid,uuid,uuid,uuid,uuid,text,text,text,uuid,uuid,text) FROM PUBLIC;
COMMENT ON TABLE campaigns.inspection_followup_requests IS
 'Private, append-only human clarification questions: no assignment, delivery, approval or verification.';
COMMENT ON TABLE campaigns.inspection_followup_responses IS
 'Private response observations to a specific clarification request; does not mark it resolved or its source verified.';
COMMIT;