-- S69: summarize the type and count of current-source differences from each
-- immutable inspection observation. Aggregate-only and non-authorizing.
-- No center identifiers or source snapshots are exposed.

BEGIN;

CREATE OR REPLACE VIEW programs.national_issuance_review_inspection_dossier
WITH (security_invoker = true) AS
SELECT
 n.request_id,
 n.note_id,
 n.recorded_at AS inspection_recorded_at,
 i.program_id,
 i.national_version,
 i.source_digest_sha256 AS original_source_digest_sha256,
 n.pinned_request_digest_sha256,
 n.observed_digest_sha256,
 n.source_changed_since_request,
 (
   n.pinned_request_digest_sha256 = i.source_digest_sha256
   AND i.source_digest_sha256 =
     encode(sha256(convert_to(i.original_source_snapshot::text,'UTF8')),'hex')
   AND NOT i.requester_authorized
   AND NOT i.human_review_recorded
   AND NOT i.issuance_authorized
   AND NOT i.issuance_executed
   AND NOT n.human_review_recorded
   AND NOT n.issuance_authorized
   AND NOT n.issuance_executed
 ) AS original_request_integrity_valid,
 (
   n.observed_digest_sha256 =
     encode(sha256(convert_to(n.observed_source_snapshot::text,'UTF8')),'hex')
   AND n.source_changed_since_request =
     (n.observed_digest_sha256 <> n.pinned_request_digest_sha256)
 ) AS observation_integrity_valid,
 EXISTS (
   SELECT 1
   FROM governance.command_audit a
   JOIN governance.outbox_events o ON o.audit_id = a.audit_id
   WHERE a.action_code = 'NATIONAL_ISSUANCE_REVIEW_INSPECTION_NOTE_CAPTURED'
     AND a.target_type = 'national_issuance_review_inspection_note'
     AND a.target_id = n.note_id::text
     AND a.metadata->>'request_id' = n.request_id::text
     AND a.metadata->>'source_changed' =
       CASE WHEN n.source_changed_since_request THEN 'true' ELSE 'false' END
     AND a.metadata->>'human_review_recorded' = 'false'
     AND a.metadata->>'issuance_authorized' = 'false'
     AND a.metadata->>'issuance_executed' = 'false'
     AND o.event_type = 'NationalIssuanceReviewInspectionNoteCaptured'
     AND o.aggregate_type = 'national_issuance_review_inspection_note'
     AND o.aggregate_id = n.note_id::text
     AND o.payload->>'note_id' = n.note_id::text
     AND o.payload->>'request_id' = n.request_id::text
     AND o.payload->>'source_changed' =
       CASE WHEN n.source_changed_since_request THEN 'true' ELSE 'false' END
     AND o.payload->>'human_review_recorded' = 'false'
     AND o.payload->>'issuance_authorized' = 'false'
     AND o.payload->>'issuance_executed' = 'false'
 ) AS audit_and_outbox_integrity_valid,
 false AS human_review_recorded,
 false AS issuance_authorized,
 false AS issuance_executed,
 encode(sha256(convert_to(current_source.snapshot::text,'UTF8')),'hex')
   AS current_source_digest_sha256,
 encode(sha256(convert_to(current_source.snapshot::text,'UTF8')),'hex')
   = n.observed_digest_sha256 AS current_source_matches_observation,
 drift_breakdown.publication_state_changed,
 drift_breakdown.center_registry_change_count,
 drift_breakdown.exact_issuance_change_count
FROM programs.national_issuance_review_inspection_notes n
JOIN programs.national_issuance_review_intents i
  ON i.request_id = n.request_id
CROSS JOIN LATERAL (
 SELECT jsonb_build_object(
   'formatVersion','S60_SOURCE_SNAPSHOT_V1',
   'programId',i.program_id,
   'nationalVersion',i.national_version,
   'exactPublicationExists',EXISTS (
     SELECT 1 FROM programs.national_program_publications p
     WHERE p.program_id=i.program_id AND p.version=i.national_version),
   'centers',current_centers.center_details
 ) AS snapshot
 FROM (
   SELECT jsonb_agg(jsonb_build_object(
     'centerId',selected.center_id,
     'registryExists',c.center_id IS NOT NULL,
     'registryActive',COALESCE(c.is_active,false),
     'exactIssuanceExists',EXISTS (
       SELECT 1 FROM programs.national_program_issuances issuance
       WHERE issuance.center_id=selected.center_id
         AND issuance.program_id=i.program_id
         AND issuance.national_version=i.national_version))
     ORDER BY selected.ordinal) AS center_details
   FROM unnest(i.explicit_center_ids) WITH ORDINALITY selected(center_id,ordinal)
   LEFT JOIN centers.registry c ON c.center_id=selected.center_id
 ) current_centers
) current_source
CROSS JOIN LATERAL (
 SELECT
   (
     n.observed_digest_sha256 =
       encode(sha256(convert_to(n.observed_source_snapshot::text,'UTF8')),'hex')
     AND jsonb_typeof(n.observed_source_snapshot)='object'
     AND n.observed_source_snapshot->>'formatVersion'='S60_SOURCE_SNAPSHOT_V1'
     AND n.observed_source_snapshot->>'programId'=i.program_id
     AND n.observed_source_snapshot->>'nationalVersion'=i.national_version::text
     AND jsonb_typeof(n.observed_source_snapshot->'exactPublicationExists')='boolean'
     AND CASE
       WHEN jsonb_typeof(n.observed_source_snapshot->'centers')='array' THEN
         NOT EXISTS (
           SELECT 1
           FROM jsonb_array_elements(n.observed_source_snapshot->'centers') observed(value)
           WHERE jsonb_typeof(observed.value) IS DISTINCT FROM 'object'
              OR jsonb_typeof(observed.value->'centerId') IS DISTINCT FROM 'string'
              OR jsonb_typeof(observed.value->'registryExists') IS DISTINCT FROM 'boolean'
              OR jsonb_typeof(observed.value->'registryActive') IS DISTINCT FROM 'boolean'
              OR jsonb_typeof(observed.value->'exactIssuanceExists') IS DISTINCT FROM 'boolean'
         )
       ELSE false
     END
   ) AS observation_comparable
) observation_check
CROSS JOIN LATERAL (
 SELECT
   CASE WHEN observation_check.observation_comparable
     THEN n.observed_source_snapshot->'exactPublicationExists'
          IS DISTINCT FROM current_source.snapshot->'exactPublicationExists'
     ELSE NULL END AS publication_state_changed,
   CASE WHEN observation_check.observation_comparable THEN (
     SELECT count(*)
     FROM jsonb_array_elements(n.observed_source_snapshot->'centers')
       WITH ORDINALITY observed(value,ordinal)
     FULL JOIN jsonb_array_elements(current_source.snapshot->'centers')
       WITH ORDINALITY fresh(value,ordinal) USING (ordinal)
     WHERE observed.value->>'centerId' IS DISTINCT FROM fresh.value->>'centerId'
        OR observed.value->'registryExists' IS DISTINCT FROM fresh.value->'registryExists'
        OR observed.value->'registryActive' IS DISTINCT FROM fresh.value->'registryActive'
   ) ELSE NULL END AS center_registry_change_count,
   CASE WHEN observation_check.observation_comparable THEN (
     SELECT count(*)
     FROM jsonb_array_elements(n.observed_source_snapshot->'centers')
       WITH ORDINALITY observed(value,ordinal)
     FULL JOIN jsonb_array_elements(current_source.snapshot->'centers')
       WITH ORDINALITY fresh(value,ordinal) USING (ordinal)
     WHERE observed.value->>'centerId' IS DISTINCT FROM fresh.value->>'centerId'
        OR observed.value->'exactIssuanceExists'
           IS DISTINCT FROM fresh.value->'exactIssuanceExists'
   ) ELSE NULL END AS exact_issuance_change_count
) drift_breakdown;

REVOKE ALL ON programs.national_issuance_review_inspection_dossier FROM PUBLIC;
COMMENT ON VIEW programs.national_issuance_review_inspection_dossier IS
 'Virtual SECURITY INVOKER technical integrity projection. Current-source drift is summarized as publication state and aggregate center registry/exact-issuance differences. No center IDs, private source fields, grants, human review, decision, or issuance authority.';
COMMIT;
