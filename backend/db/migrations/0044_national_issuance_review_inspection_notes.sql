-- S61: append-only NON-DECISION inspection notes linked to immutable S60 request.
-- No reviewer identity attestation, approval, issuance, or runtime SQL privileges.
BEGIN;

CREATE TABLE programs.national_issuance_review_inspection_notes (
 note_id uuid PRIMARY KEY CHECK (note_id <> '00000000-0000-0000-0000-000000000000'::uuid),
 request_id uuid NOT NULL REFERENCES programs.national_issuance_review_intents(request_id)
   ON DELETE RESTRICT,
 pinned_request_digest_sha256 text NOT NULL CHECK
   (pinned_request_digest_sha256 ~ '^[a-f0-9]{64}$'),
 observed_source_snapshot jsonb NOT NULL CHECK
   (jsonb_typeof(observed_source_snapshot)='object'),
 observed_digest_sha256 text NOT NULL CHECK
   (observed_digest_sha256 ~ '^[a-f0-9]{64}$'),
 source_changed_since_request boolean NOT NULL,
 unverified_inspector_subject text NOT NULL CHECK
   (length(btrim(unverified_inspector_subject)) BETWEEN 1 AND 256),
 technical_note text NOT NULL CHECK (length(btrim(technical_note)) BETWEEN 1 AND 1200),
 recorded_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
 human_review_recorded boolean NOT NULL DEFAULT false CHECK (human_review_recorded=false),
 issuance_authorized boolean NOT NULL DEFAULT false CHECK (issuance_authorized=false),
 issuance_executed boolean NOT NULL DEFAULT false CHECK (issuance_executed=false)
);
CREATE INDEX issuance_review_inspection_by_request
 ON programs.national_issuance_review_inspection_notes(request_id,recorded_at,note_id);
REVOKE ALL ON programs.national_issuance_review_inspection_notes FROM PUBLIC;
ALTER TABLE programs.national_issuance_review_inspection_notes ENABLE ROW LEVEL SECURITY;
ALTER TABLE programs.national_issuance_review_inspection_notes FORCE ROW LEVEL SECURITY;
-- No RLS policies: no Production/restricted SQL role can view or write.
CREATE TRIGGER national_issuance_review_inspection_append_only
 BEFORE UPDATE OR DELETE ON programs.national_issuance_review_inspection_notes
 FOR EACH ROW EXECUTE FUNCTION programs.reject_version_or_publication_mutation();

-- For disposable fixtures only, default SECURITY INVOKER, PUBLIC EXECUTE revoked.
-- The unverified reviewer name is technical text, never verified OIDC identity.
CREATE FUNCTION programs.capture_national_issuance_review_inspection_note(
 p_note_id uuid,p_request_id uuid,p_unverified_inspector text,p_technical_note text,
 p_audit_id uuid,p_event_id uuid,p_correlation_id text
) RETURNS uuid LANGUAGE plpgsql SECURITY INVOKER
 SET search_path=pg_catalog,pg_temp AS $fn$
DECLARE
 v_intent programs.national_issuance_review_intents%ROWTYPE;
 v_publication boolean;
 v_center_details jsonb;
 v_current jsonb;
 v_current_digest text;
 v_changed boolean;
BEGIN
 IF current_setting('transaction_isolation') NOT IN ('repeatable read','serializable')
 THEN RAISE EXCEPTION 'Inspection capture requires explicit stable isolation'
  USING ERRCODE='25001'; END IF;
 IF p_note_id IS NULL OR p_note_id='00000000-0000-0000-0000-000000000000'::uuid
 OR p_request_id IS NULL OR p_audit_id IS NULL OR p_event_id IS NULL
 OR p_unverified_inspector IS NULL OR length(btrim(p_unverified_inspector)) NOT BETWEEN 1 AND 256
 OR p_technical_note IS NULL OR length(btrim(p_technical_note)) NOT BETWEEN 1 AND 1200
 OR p_correlation_id IS NULL OR btrim(p_correlation_id)=''
 THEN RAISE EXCEPTION 'Invalid nonauthorizing review inspection note'
  USING ERRCODE='22023'; END IF;

 SELECT * INTO v_intent FROM programs.national_issuance_review_intents
  WHERE request_id=p_request_id;
 IF NOT FOUND THEN RAISE EXCEPTION 'Original review intent not found'
  USING ERRCODE='23503'; END IF;
 IF v_intent.requester_authorized OR v_intent.human_review_recorded
 OR v_intent.issuance_authorized OR v_intent.issuance_executed
 OR v_intent.source_digest_sha256 IS DISTINCT FROM
   encode(sha256(convert_to(v_intent.original_source_snapshot::text,'UTF8')),'hex')
 THEN RAISE EXCEPTION 'Original review source is not trustworthy'
  USING ERRCODE='22023'; END IF;

 SELECT EXISTS(SELECT 1 FROM programs.national_program_publications p
  WHERE p.program_id=v_intent.program_id AND p.version=v_intent.national_version)
  INTO v_publication;
 SELECT jsonb_agg(jsonb_build_object(
   'centerId',selected.center_id,
   'registryExists',c.center_id IS NOT NULL,
   'registryActive',COALESCE(c.is_active,false),
   'exactIssuanceExists',EXISTS (
     SELECT 1 FROM programs.national_program_issuances i
     WHERE i.center_id=selected.center_id AND i.program_id=v_intent.program_id
       AND i.national_version=v_intent.national_version))
   ORDER BY selected.ordinal)
 INTO v_center_details
 FROM unnest(v_intent.explicit_center_ids) WITH ORDINALITY selected(center_id,ordinal)
 LEFT JOIN centers.registry c ON c.center_id=selected.center_id;

 v_current := jsonb_build_object(
  'formatVersion','S60_SOURCE_SNAPSHOT_V1',
  'programId',v_intent.program_id,
  'nationalVersion',v_intent.national_version,
  'exactPublicationExists',v_publication,
  'centers',v_center_details);
 v_current_digest := encode(sha256(convert_to(v_current::text,'UTF8')),'hex');
 v_changed := v_current_digest <> v_intent.source_digest_sha256;

 INSERT INTO programs.national_issuance_review_inspection_notes
 (note_id,request_id,pinned_request_digest_sha256,observed_source_snapshot,
  observed_digest_sha256,source_changed_since_request,
  unverified_inspector_subject,technical_note)
 VALUES(p_note_id,p_request_id,v_intent.source_digest_sha256,v_current,
   v_current_digest,v_changed,p_unverified_inspector,btrim(p_technical_note));

 INSERT INTO governance.command_audit
 (audit_id,occurred_at,actor_subject_id,center_id,action_code,target_type,
  target_id,correlation_id,metadata)
 VALUES(p_audit_id,transaction_timestamp(),p_unverified_inspector,NULL,
  'NATIONAL_ISSUANCE_REVIEW_INSPECTION_NOTE_CAPTURED',
  'national_issuance_review_inspection_note',p_note_id::text,p_correlation_id,
  jsonb_build_object('request_id',p_request_id,'source_changed',v_changed,
   'unverified_actor',true,'human_review_recorded',false,
   'issuance_authorized',false,'issuance_executed',false));

 INSERT INTO governance.outbox_events
 (event_id,audit_id,event_type,aggregate_type,aggregate_id,occurred_at,payload)
 VALUES(p_event_id,p_audit_id,'NationalIssuanceReviewInspectionNoteCaptured',
  'national_issuance_review_inspection_note',p_note_id::text,
  transaction_timestamp(),
  jsonb_build_object('note_id',p_note_id,'request_id',p_request_id,
   'source_changed',v_changed,'technical_only',true,
   'human_review_recorded',false,'issuance_authorized',false,'issuance_executed',false));
 RETURN p_note_id;
END;$fn$;
REVOKE ALL ON FUNCTION programs.capture_national_issuance_review_inspection_note(
 uuid,uuid,text,text,uuid,uuid,text) FROM PUBLIC;
COMMENT ON TABLE programs.national_issuance_review_inspection_notes IS
 'Append-only inspection notes, not reviewer decisions or issuance grants. No RLS policies.';
COMMENT ON FUNCTION programs.capture_national_issuance_review_inspection_note(
 uuid,uuid,text,text,uuid,uuid,text) IS
 'CI-only SECURITY INVOKER. Unverified actor text and source drift do not authorize any operation.';
COMMIT;
