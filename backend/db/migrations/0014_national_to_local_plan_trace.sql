-- Immutable and version-pinned structural lineage. Not proof of execution.
BEGIN;
ALTER TABLE programs.center_plan_draft_revisions
 ADD CONSTRAINT draft_exact_national_binding_unique
 UNIQUE(center_id,draft_id,revision,program_id,national_version);
ALTER TABLE programs.national_program_issuances
 ADD CONSTRAINT issuance_exact_center_binding_unique
 UNIQUE(center_id,issuance_id,program_id,national_version);
CREATE TABLE programs.center_plan_issuance_links (
 link_id uuid PRIMARY KEY,
 center_id text NOT NULL REFERENCES centers.registry(center_id) ON DELETE RESTRICT,
 draft_id uuid NOT NULL,
 pinned_revision bigint NOT NULL CHECK(pinned_revision>0),
 issuance_id uuid NOT NULL,
 program_id text NOT NULL CHECK(length(btrim(program_id))>0),
 national_version integer NOT NULL CHECK(national_version>0),
 linked_by_subject text NOT NULL CHECK(length(btrim(linked_by_subject))>0),
 linked_at timestamptz NOT NULL,
 CONSTRAINT one_issuance_per_draft UNIQUE(center_id,draft_id),
 CONSTRAINT exact_draft_version_fk FOREIGN KEY
  (center_id,draft_id,pinned_revision,program_id,national_version)
  REFERENCES programs.center_plan_draft_revisions
  (center_id,draft_id,revision,program_id,national_version) ON DELETE RESTRICT,
 CONSTRAINT exact_center_issuance_fk FOREIGN KEY
  (center_id,issuance_id,program_id,national_version)
  REFERENCES programs.national_program_issuances
  (center_id,issuance_id,program_id,national_version) ON DELETE RESTRICT
);
CREATE INDEX center_issuance_trace_idx
 ON programs.center_plan_issuance_links(center_id,issuance_id);
CREATE TRIGGER center_plan_link_immutable BEFORE UPDATE OR DELETE
 ON programs.center_plan_issuance_links FOR EACH ROW
 EXECUTE FUNCTION programs.reject_draft_revision_mutation();
REVOKE ALL ON TABLE programs.center_plan_issuance_links FROM PUBLIC;
ALTER TABLE programs.center_plan_issuance_links ENABLE ROW LEVEL SECURITY;
ALTER TABLE programs.center_plan_issuance_links FORCE ROW LEVEL SECURITY;
CREATE POLICY center_plan_issuance_trace_scope ON programs.center_plan_issuance_links
 USING(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));

-- No runtime role is granted EXECUTE. The supplied actor is not authenticated
-- by SQL; business operation grants remain DENY until separately approved.
CREATE FUNCTION programs.record_center_plan_issuance_link(
 p_link_id uuid,p_center_id text,p_draft_id uuid,p_revision bigint,
 p_issuance_id uuid,p_program_id text,p_version integer,
 p_actor text,p_audit uuid,p_event uuid,p_correlation text
) RETURNS uuid LANGUAGE plpgsql SECURITY INVOKER
 SET search_path=pg_catalog,pg_temp AS $fn$
BEGIN
 IF p_link_id IS NULL OR p_draft_id IS NULL OR p_issuance_id IS NULL
    OR p_audit IS NULL OR p_event IS NULL OR
    p_center_id IS NULL OR btrim(p_center_id)='' OR
    p_program_id IS NULL OR btrim(p_program_id)='' OR
    p_actor IS NULL OR btrim(p_actor)='' OR
    p_correlation IS NULL OR btrim(p_correlation)='' OR
    p_revision IS NULL OR p_revision<=0 OR
    p_version IS NULL OR p_version<=0 THEN
  RAISE EXCEPTION 'Invalid trace request' USING ERRCODE='22023';
 END IF;
 INSERT INTO programs.center_plan_issuance_links(
  link_id,center_id,draft_id,pinned_revision,issuance_id,program_id,
  national_version,linked_by_subject,linked_at)
 VALUES(p_link_id,p_center_id,p_draft_id,p_revision,p_issuance_id,
  p_program_id,p_version,p_actor,transaction_timestamp());
 INSERT INTO governance.command_audit(
  audit_id,occurred_at,actor_subject_id,center_id,action_code,target_type,
  target_id,correlation_id,metadata)
 VALUES(p_audit,transaction_timestamp(),p_actor,p_center_id,
  'CENTER_PLAN_ISSUANCE_LINK_RECORDED','center_plan_issuance_link',
  p_link_id::text,p_correlation,
  jsonb_build_object('draft_id',p_draft_id,'revision',p_revision,'issuance_id',p_issuance_id));
 INSERT INTO governance.outbox_events(
  event_id,audit_id,event_type,aggregate_type,aggregate_id,occurred_at,payload)
 VALUES(p_event,p_audit,'CenterPlanIssuanceLinkRecorded','center_plan_issuance_link',
  p_link_id::text,transaction_timestamp(),
  jsonb_build_object('link_id',p_link_id,'center_id',p_center_id,
    'draft_id',p_draft_id,'pinned_revision',p_revision,'issuance_id',p_issuance_id,
    'program_id',p_program_id,'national_version',p_version));
 RETURN p_link_id;
END;
$fn$;
REVOKE ALL ON FUNCTION programs.record_center_plan_issuance_link(
 uuid,text,uuid,bigint,uuid,text,integer,text,uuid,uuid,text) FROM PUBLIC;
COMMIT;
