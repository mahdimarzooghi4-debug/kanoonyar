-- S60: immutable ORIGINAL source-backed national issuance review INTENT,
-- never human review, approval, actual issuance or authorization.
-- B02/T15 HQ issuer/reviewer rights are DRAFT. No Production EXECUTE grant.
BEGIN;

CREATE TABLE programs.national_issuance_review_intents (
    request_id uuid PRIMARY KEY
      CHECK (request_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    program_id text NOT NULL CHECK
      (length(program_id) BETWEEN 1 AND 256 AND program_id = btrim(program_id)),
    national_version integer NOT NULL CHECK (national_version > 0),
    explicit_center_ids text[] NOT NULL CHECK (cardinality(explicit_center_ids) > 0),
    source_format_version text NOT NULL DEFAULT 'S60_SOURCE_SNAPSHOT_V1'
      CHECK (source_format_version = 'S60_SOURCE_SNAPSHOT_V1'),
    original_source_snapshot jsonb NOT NULL CHECK
      (jsonb_typeof(original_source_snapshot) = 'object'),
    source_digest_sha256 text NOT NULL CHECK
      (source_digest_sha256 ~ '^[a-f0-9]{64}$'),
    stated_review_reason text NOT NULL CHECK
      (length(btrim(stated_review_reason)) BETWEEN 1 AND 800),
    unverified_requester_subject text NOT NULL CHECK
      (length(btrim(unverified_requester_subject)) BETWEEN 1 AND 256),
    recorded_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
    requester_authorized boolean NOT NULL DEFAULT false CHECK (requester_authorized = false),
    human_review_recorded boolean NOT NULL DEFAULT false CHECK (human_review_recorded = false),
    issuance_authorized boolean NOT NULL DEFAULT false CHECK (issuance_authorized = false),
    issuance_executed boolean NOT NULL DEFAULT false CHECK (issuance_executed = false)
);
CREATE INDEX national_issuance_review_by_source
 ON programs.national_issuance_review_intents(program_id,national_version,request_id);
REVOKE ALL ON programs.national_issuance_review_intents FROM PUBLIC;
ALTER TABLE programs.national_issuance_review_intents ENABLE ROW LEVEL SECURITY;
ALTER TABLE programs.national_issuance_review_intents FORCE ROW LEVEL SECURITY;
-- Intentionally NO SELECT, INSERT, UPDATE or DELETE RLS policy.
CREATE TRIGGER national_issuance_review_intent_append_only
 BEFORE UPDATE OR DELETE ON programs.national_issuance_review_intents
 FOR EACH ROW EXECUTE FUNCTION programs.reject_version_or_publication_mutation();

-- SECURITY INVOKER (the default) by design. CI superuser alone can exercise
-- the technical contract. A normal SQL login, including the S52 role,
-- receives no EXECUTE or INSERT. Supplying an actor ID is never authentication.
CREATE FUNCTION programs.capture_national_issuance_review_intent(
    p_request_id uuid, p_program_id text, p_version integer,
    p_explicit_centers text[], p_unverified_requester text,
    p_reason text, p_audit_id uuid, p_event_id uuid, p_correlation_id text
) RETURNS uuid LANGUAGE plpgsql SECURITY INVOKER
SET search_path = pg_catalog,pg_temp
AS $fn$
DECLARE
    v_published boolean;
    v_centers jsonb;
    v_snapshot jsonb;
    v_digest text;
BEGIN
    IF current_setting('transaction_isolation') NOT IN ('repeatable read','serializable')
    THEN
        RAISE EXCEPTION 'Review capture requires a stable explicit transaction snapshot'
          USING ERRCODE='25001';
    END IF;
    IF p_request_id IS NULL OR
       p_request_id = '00000000-0000-0000-0000-000000000000'::uuid OR
       p_audit_id IS NULL OR p_event_id IS NULL OR
       p_program_id IS NULL OR p_program_id <> btrim(p_program_id) OR
       length(p_program_id) NOT BETWEEN 1 AND 256 OR
       p_program_id ~ '[[:cntrl:]]' OR
       p_version IS NULL OR p_version <= 0 OR
       p_explicit_centers IS NULL OR cardinality(p_explicit_centers) = 0 OR
       p_unverified_requester IS NULL OR
       length(btrim(p_unverified_requester)) NOT BETWEEN 1 AND 256 OR
       p_reason IS NULL OR length(btrim(p_reason)) NOT BETWEEN 1 AND 800 OR
       p_correlation_id IS NULL OR btrim(p_correlation_id) = ''
    THEN
        RAISE EXCEPTION 'Invalid unapproved issuance-review intent'
          USING ERRCODE='22023';
    END IF;
    IF EXISTS (
        SELECT 1 FROM unnest(p_explicit_centers) AS source(center_id)
        WHERE center_id IS NULL OR center_id <> btrim(center_id)
            OR length(center_id) NOT BETWEEN 1 AND 256
            OR center_id ~ '[[:cntrl:]]'
    ) OR (
        SELECT count(DISTINCT center_id) FROM unnest(p_explicit_centers) AS source(center_id)
    ) <> cardinality(p_explicit_centers)
    THEN
        RAISE EXCEPTION 'Invalid or duplicated explicit center selection'
          USING ERRCODE='22023';
    END IF;

    SELECT EXISTS(
        SELECT 1 FROM programs.national_program_publications p
        WHERE p.program_id=p_program_id AND p.version=p_version
    ) INTO v_published;

    -- Capture the actual same-snapshot source facts. Never trust a
    -- caller-supplied S59 digest or client-generated flags as authoritative.
    SELECT jsonb_agg(jsonb_build_object(
        'centerId',selection.center_id,
        'registryExists',registry.center_id IS NOT NULL,
        'registryActive',COALESCE(registry.is_active,false),
        'exactIssuanceExists',EXISTS (
            SELECT 1 FROM programs.national_program_issuances issued
            WHERE issued.center_id=selection.center_id
              AND issued.program_id=p_program_id
              AND issued.national_version=p_version
        )) ORDER BY selection.ordinal)
    INTO v_centers
    FROM unnest(p_explicit_centers) WITH ORDINALITY
      AS selection(center_id,ordinal)
    LEFT JOIN centers.registry registry
      ON registry.center_id=selection.center_id;

    v_snapshot := jsonb_build_object(
      'formatVersion','S60_SOURCE_SNAPSHOT_V1',
      'programId',p_program_id,'nationalVersion',p_version,
      'exactPublicationExists',v_published,
      'centers',v_centers
    );
    -- A stable local digest for accidental modification detection only.
    -- Neither this hash nor a matching S59 hash is a signature or authorization.
    v_digest := encode(sha256(convert_to(v_snapshot::text,'UTF8')),'hex');

    INSERT INTO programs.national_issuance_review_intents(
       request_id,program_id,national_version,explicit_center_ids,
       original_source_snapshot,source_digest_sha256,
       unverified_requester_subject,stated_review_reason
    ) VALUES (
       p_request_id,p_program_id,p_version,p_explicit_centers,
       v_snapshot,v_digest,p_unverified_requester,p_reason
    );

    INSERT INTO governance.command_audit(
      audit_id,occurred_at,actor_subject_id,center_id,action_code,
      target_type,target_id,correlation_id,metadata
    ) VALUES (
      p_audit_id,transaction_timestamp(),p_unverified_requester,NULL,
      'NATIONAL_ISSUANCE_REVIEW_INTENT_CAPTURED',
      'national_issuance_review_intent',p_request_id::text,p_correlation_id,
      jsonb_build_object('source_digest_sha256',v_digest,
        'unverified_actor',true,'requester_authorized',false,
        'human_review_recorded',false,'issuance_authorized',false,
        'issuance_executed',false)
    );

    INSERT INTO governance.outbox_events(
      event_id,audit_id,event_type,aggregate_type,aggregate_id,occurred_at,payload
    ) VALUES (
      p_event_id,p_audit_id,'NationalIssuanceReviewIntentCaptured',
      'national_issuance_review_intent',p_request_id::text,
      transaction_timestamp(),
      jsonb_build_object('request_id',p_request_id,
        'recorded_only',true,'requester_authorized',false,
        'human_review_recorded',false,'issuance_authorized',false,
        'issuance_executed',false)
    );
    RETURN p_request_id;
END;
$fn$;
REVOKE ALL ON FUNCTION programs.capture_national_issuance_review_intent(
    uuid,text,integer,text[],text,text,uuid,uuid,text) FROM PUBLIC;
COMMENT ON FUNCTION programs.capture_national_issuance_review_intent(
    uuid,text,integer,text[],text,text,uuid,uuid,text) IS
 'OFFLINE technical review intent only; no runtime EXECUTE or grant. Actor text is untrusted; a current source snapshot is not an issuance approval.';
COMMENT ON TABLE programs.national_issuance_review_intents IS
 'Append-only unapproved national issuance review requests; not human decisions, issuance authorizations, or real center issuances.';
COMMIT;
