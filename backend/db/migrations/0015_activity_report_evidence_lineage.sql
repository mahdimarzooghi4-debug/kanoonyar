-- Sprint 11. This stores an unverified human/source REPORT about local activity,
-- not an executed activity, verified evidence, class attendance or outcome.
-- No runtime insert/select or function execute grants are authorized here.
BEGIN;
ALTER TABLE programs.center_plan_issuance_links
 ADD CONSTRAINT trace_center_link_draft_unique UNIQUE(center_id,link_id,draft_id);

CREATE TABLE programs.center_activity_reports (
 report_id uuid PRIMARY KEY,
 report_sequence bigint GENERATED ALWAYS AS IDENTITY UNIQUE,
 center_id text NOT NULL REFERENCES centers.registry(center_id) ON DELETE RESTRICT,
 link_id uuid NOT NULL,
 draft_id uuid NOT NULL,
 source_kind text NOT NULL CHECK(length(btrim(source_kind))>0),
 source_record_ref text NOT NULL CHECK(length(btrim(source_record_ref))>0),
 evidence_ref text NOT NULL CHECK(length(btrim(evidence_ref))>0),
 reported_summary text NOT NULL CHECK(length(btrim(reported_summary))>0),
 claimed_occurred_at timestamptz NULL,
 reported_by_subject text NOT NULL CHECK(length(btrim(reported_by_subject))>0),
 recorded_at timestamptz NOT NULL,
 CONSTRAINT report_exact_center_draft_trace_fk
  FOREIGN KEY(center_id,link_id,draft_id)
  REFERENCES programs.center_plan_issuance_links(center_id,link_id,draft_id)
  ON DELETE RESTRICT,
 CONSTRAINT report_evidence_duplicate_guard
  UNIQUE(center_id,link_id,source_kind,source_record_ref,evidence_ref)
);
CREATE INDEX center_activity_report_page_idx
 ON programs.center_activity_reports(center_id,draft_id,report_sequence DESC);
CREATE TRIGGER center_activity_report_immutable BEFORE UPDATE OR DELETE
 ON programs.center_activity_reports FOR EACH ROW
 EXECUTE FUNCTION programs.reject_draft_revision_mutation();
REVOKE ALL ON TABLE programs.center_activity_reports FROM PUBLIC;
REVOKE ALL ON SEQUENCE programs.center_activity_reports_report_sequence_seq FROM PUBLIC;
ALTER TABLE programs.center_activity_reports ENABLE ROW LEVEL SECURITY;
ALTER TABLE programs.center_activity_reports FORCE ROW LEVEL SECURITY;
CREATE POLICY center_activity_report_scope ON programs.center_activity_reports
 USING(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));

-- SECURITY INVOKER deliberately has no runtime EXECUTE grant. The actor, source
-- identifier, and evidence pointer are NOT authenticated or verified by SQL.
CREATE FUNCTION programs.record_center_activity_report(
 p_report_id uuid,p_center_id text,p_link_id uuid,p_draft_id uuid,
 p_source_kind text,p_source_ref text,p_evidence_ref text,p_summary text,
 p_claimed_at timestamptz,p_actor text,
 p_audit_id uuid,p_event_id uuid,p_correlation_id text
) RETURNS uuid LANGUAGE plpgsql SECURITY INVOKER
SET search_path=pg_catalog,pg_temp AS $fn$
BEGIN
 IF p_report_id IS NULL OR p_link_id IS NULL OR p_draft_id IS NULL
    OR p_audit_id IS NULL OR p_event_id IS NULL
    OR p_center_id IS NULL OR btrim(p_center_id)=''
    OR p_source_kind IS NULL OR btrim(p_source_kind)=''
    OR p_source_ref IS NULL OR btrim(p_source_ref)=''
    OR p_evidence_ref IS NULL OR btrim(p_evidence_ref)=''
    OR p_summary IS NULL OR btrim(p_summary)=''
    OR p_actor IS NULL OR btrim(p_actor)=''
    OR p_correlation_id IS NULL OR btrim(p_correlation_id)='' THEN
   RAISE EXCEPTION 'Invalid unverified activity report' USING ERRCODE='22023';
 END IF;
 INSERT INTO programs.center_activity_reports(
  report_id,center_id,link_id,draft_id,source_kind,source_record_ref,
  evidence_ref,reported_summary,claimed_occurred_at,reported_by_subject,recorded_at)
 VALUES(p_report_id,p_center_id,p_link_id,p_draft_id,p_source_kind,p_source_ref,
  p_evidence_ref,p_summary,p_claimed_at,p_actor,transaction_timestamp());

 INSERT INTO governance.command_audit(
  audit_id,occurred_at,actor_subject_id,center_id,action_code,target_type,
  target_id,correlation_id,metadata)
 VALUES(p_audit_id,transaction_timestamp(),p_actor,p_center_id,
  'UNVERIFIED_ACTIVITY_REPORT_RECORDED','center_activity_report',
  p_report_id::text,p_correlation_id,
  jsonb_build_object('link_id',p_link_id,'draft_id',p_draft_id,
    'source_kind',p_source_kind,'source_record_ref',p_source_ref));

 INSERT INTO governance.outbox_events(
  event_id,audit_id,event_type,aggregate_type,aggregate_id,occurred_at,payload)
 VALUES(p_event_id,p_audit_id,'UnverifiedActivityReportRecorded',
  'center_activity_report',p_report_id::text,transaction_timestamp(),
  jsonb_build_object('report_id',p_report_id,'center_id',p_center_id,
    'draft_id',p_draft_id,'link_id',p_link_id,'source_kind',p_source_kind,
    'source_record_ref',p_source_ref,'verification_status','UNVERIFIED'));
 RETURN p_report_id;
END;
$fn$;
REVOKE ALL ON FUNCTION programs.record_center_activity_report(
 uuid,text,uuid,uuid,text,text,text,text,timestamptz,text,uuid,uuid,text) FROM PUBLIC;
COMMIT;
