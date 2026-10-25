-- S81: classify non-comparable inspection observations into stable, non-authorizing reasons.
BEGIN;

CREATE VIEW programs.national_issuance_review_observation_diagnostics
WITH (security_invoker = true) AS
SELECT
 'S81_OBSERVATION_DIAGNOSTIC_V1'::text AS diagnostic_contract_version,
 d.request_id,
 d.note_id,
 d.program_id,
 d.national_version,
 CASE
   WHEN d.original_request_integrity_valid IS DISTINCT FROM true
     THEN 'ORIGINAL_REQUEST_INTEGRITY_INVALID'
   WHEN d.observation_integrity_valid IS DISTINCT FROM true
     THEN 'OBSERVATION_INTEGRITY_INVALID'
   WHEN jsonb_typeof(n.observed_source_snapshot) IS DISTINCT FROM 'object'
     OR n.observed_source_snapshot->>'formatVersion' IS DISTINCT FROM 'S60_SOURCE_SNAPSHOT_V1'
     OR jsonb_typeof(n.observed_source_snapshot->'programId') IS DISTINCT FROM 'string'
     OR n.observed_source_snapshot->>'programId' IS DISTINCT FROM i.program_id
     OR jsonb_typeof(n.observed_source_snapshot->'nationalVersion') IS DISTINCT FROM 'number'
     OR n.observed_source_snapshot->>'nationalVersion' IS DISTINCT FROM i.national_version::text
     OR jsonb_typeof(n.observed_source_snapshot->'exactPublicationExists') IS DISTINCT FROM 'boolean'
     OR jsonb_typeof(n.observed_source_snapshot->'centers') IS DISTINCT FROM 'array'
     THEN 'OBSERVED_SOURCE_ENVELOPE_INVALID'
   WHEN CASE
     WHEN jsonb_typeof(n.observed_source_snapshot->'centers')='array' THEN EXISTS (
       SELECT 1
       FROM jsonb_array_elements(n.observed_source_snapshot->'centers') observed(value)
       WHERE jsonb_typeof(observed.value) IS DISTINCT FROM 'object'
          OR jsonb_typeof(observed.value->'centerId') IS DISTINCT FROM 'string'
          OR length(btrim(observed.value->>'centerId')) NOT BETWEEN 1 AND 256
          OR observed.value->>'centerId' IS DISTINCT FROM btrim(observed.value->>'centerId')
          OR observed.value->>'centerId' ~ '[[:cntrl:]]'
     )
     ELSE false
   END THEN 'OBSERVED_CENTER_IDENTIFIER_INVALID'
   WHEN CASE
     WHEN jsonb_typeof(n.observed_source_snapshot->'centers')='array' THEN (
       SELECT count(*)<>count(DISTINCT observed.value->>'centerId')
       FROM jsonb_array_elements(n.observed_source_snapshot->'centers') observed(value)
     )
     ELSE false
   END THEN 'OBSERVED_CENTER_IDENTIFIER_DUPLICATE'
   WHEN CASE
     WHEN jsonb_typeof(n.observed_source_snapshot->'centers')='array' THEN EXISTS (
       SELECT 1
       FROM jsonb_array_elements(n.observed_source_snapshot->'centers') observed(value)
       WHERE jsonb_typeof(observed.value->'registryExists') IS DISTINCT FROM 'boolean'
          OR jsonb_typeof(observed.value->'registryActive') IS DISTINCT FROM 'boolean'
          OR jsonb_typeof(observed.value->'exactIssuanceExists') IS DISTINCT FROM 'boolean'
          OR (observed.value->'registryActive'='true'::jsonb
              AND observed.value->'registryExists' IS DISTINCT FROM 'true'::jsonb)
          OR (observed.value->'exactIssuanceExists'='true'::jsonb
              AND observed.value->'registryExists' IS DISTINCT FROM 'true'::jsonb)
     )
     ELSE false
   END THEN 'OBSERVED_CENTER_FACTS_INVALID'
   WHEN CASE
     WHEN jsonb_typeof(n.observed_source_snapshot->'centers')='array' THEN EXISTS (
       SELECT 1
       FROM unnest(i.explicit_center_ids) WITH ORDINALITY requested(center_id,ordinal)
       FULL JOIN jsonb_array_elements(n.observed_source_snapshot->'centers')
         WITH ORDINALITY observed(value,ordinal) USING (ordinal)
       WHERE requested.center_id IS DISTINCT FROM observed.value->>'centerId'
     )
     ELSE false
   END THEN 'OBSERVED_CENTER_SCOPE_MISMATCH'
   ELSE 'OBSERVATION_NOT_COMPARABLE_UNCLASSIFIED'
 END::text AS diagnostic_code
FROM programs.national_issuance_review_inspection_dossier d
JOIN programs.national_issuance_review_inspection_notes n
  ON n.note_id=d.note_id
JOIN programs.national_issuance_review_intents i
  ON i.request_id=d.request_id
WHERE d.source_observation_comparable IS DISTINCT FROM true;

REVOKE ALL ON programs.national_issuance_review_observation_diagnostics FROM PUBLIC;
COMMENT ON VIEW programs.national_issuance_review_observation_diagnostics IS
 'SECURITY INVOKER, aggregate-only technical classification of non-comparable observations. Stable diagnostic codes do not attest identity, indicate human review, grant permission, authorize issuance, or trigger operations.';
COMMIT;
