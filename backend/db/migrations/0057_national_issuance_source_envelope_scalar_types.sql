-- S75: validate semantic identity and consistency inside source envelopes.
BEGIN;

CREATE OR REPLACE VIEW programs.national_issuance_review_inspection_summary
WITH (security_invoker = true) AS
WITH request_integrity_parts AS (
 SELECT
   i.request_id,
   COALESCE(i.source_digest_sha256 =
     encode(sha256(convert_to(i.original_source_snapshot::text,'UTF8')),'hex'),
     false) AS request_snapshot_digest_valid,
   COALESCE(
     jsonb_typeof(i.original_source_snapshot)='object'
     AND i.original_source_snapshot->>'formatVersion'=i.source_format_version
     AND i.source_format_version='S60_SOURCE_SNAPSHOT_V1'
     AND jsonb_typeof(i.original_source_snapshot->'programId')='string'
     AND jsonb_typeof(i.original_source_snapshot->'nationalVersion')='number'
     AND i.original_source_snapshot->>'programId'=i.program_id
     AND i.original_source_snapshot->>'nationalVersion'=i.national_version::text
     AND jsonb_typeof(i.original_source_snapshot->'exactPublicationExists')='boolean'
     AND jsonb_typeof(i.original_source_snapshot->'centers')='array'
     AND COALESCE((
       SELECT bool_and(
         jsonb_typeof(c.value)='object'
         AND jsonb_typeof(c.value->'centerId')='string'
         AND length(btrim(c.value->>'centerId')) BETWEEN 1 AND 256
         AND c.value->>'centerId'=btrim(c.value->>'centerId')
         AND c.value->>'centerId' !~ '[[:cntrl:]]'
         AND jsonb_typeof(c.value->'registryExists')='boolean'
         AND jsonb_typeof(c.value->'registryActive')='boolean'
         AND jsonb_typeof(c.value->'exactIssuanceExists')='boolean'
         AND CASE WHEN c.value->'registryActive'='true'::jsonb
           THEN c.value->'registryExists'='true'::jsonb ELSE true END
         AND CASE WHEN c.value->'exactIssuanceExists'='true'::jsonb
           THEN c.value->'registryExists'='true'::jsonb ELSE true END
       )
       FROM jsonb_array_elements(
         CASE WHEN jsonb_typeof(i.original_source_snapshot->'centers')='array'
           THEN i.original_source_snapshot->'centers' ELSE '[]'::jsonb END
       ) AS c(value)
     ),false)
     AND COALESCE((
       SELECT count(*)=count(DISTINCT c.value->>'centerId')
       FROM jsonb_array_elements(
         CASE WHEN jsonb_typeof(i.original_source_snapshot->'centers')='array'
           THEN i.original_source_snapshot->'centers' ELSE '[]'::jsonb END
       ) AS c(value)
     ),false),
     false
   ) AS request_source_envelope_valid,
   COALESCE((
     SELECT array_agg(c.value->>'centerId' ORDER BY c.ordinality)
       = i.explicit_center_ids
     FROM jsonb_array_elements(
       CASE WHEN jsonb_typeof(i.original_source_snapshot->'centers')='array'
         THEN i.original_source_snapshot->'centers' ELSE '[]'::jsonb END
     ) WITH ORDINALITY AS c(value,ordinality)
   ),false) AS request_center_selection_valid,
   (i.requester_authorized=false AND i.human_review_recorded=false
     AND i.issuance_authorized=false AND i.issuance_executed=false)
       AS request_authority_flags_clear,
   COALESCE(
     (SELECT count(*) FROM governance.command_audit a
       WHERE a.action_code='NATIONAL_ISSUANCE_REVIEW_INTENT_CAPTURED'
         AND a.target_type='national_issuance_review_intent'
         AND a.target_id=i.request_id::text)=1
     AND
     (SELECT count(*) FROM governance.command_audit a
       WHERE a.action_code='NATIONAL_ISSUANCE_REVIEW_INTENT_CAPTURED'
         AND a.target_type='national_issuance_review_intent'
         AND a.target_id=i.request_id::text
         AND a.metadata->>'source_digest_sha256'=i.source_digest_sha256
         AND a.metadata->>'unverified_actor'='true'
         AND a.metadata->>'requester_authorized'='false'
         AND a.metadata->>'human_review_recorded'='false'
         AND a.metadata->>'issuance_authorized'='false'
         AND a.metadata->>'issuance_executed'='false')=1,
     false
   ) AS request_audit_integrity_valid,
   COALESCE(
     (SELECT count(*) FROM governance.outbox_events o
       WHERE o.event_type='NationalIssuanceReviewIntentCaptured'
         AND o.aggregate_type='national_issuance_review_intent'
         AND o.aggregate_id=i.request_id::text)=1
     AND
     (SELECT count(*) FROM governance.outbox_events o
       JOIN governance.command_audit a ON a.audit_id=o.audit_id
       WHERE a.action_code='NATIONAL_ISSUANCE_REVIEW_INTENT_CAPTURED'
         AND a.target_type='national_issuance_review_intent'
         AND a.target_id=i.request_id::text
         AND o.event_type='NationalIssuanceReviewIntentCaptured'
         AND o.aggregate_type='national_issuance_review_intent'
         AND o.aggregate_id=i.request_id::text
         AND o.payload->>'request_id'=i.request_id::text
         AND o.payload->>'recorded_only'='true'
         AND o.payload->>'requester_authorized'='false'
         AND o.payload->>'human_review_recorded'='false'
         AND o.payload->>'issuance_authorized'='false'
         AND o.payload->>'issuance_executed'='false')=1,
     false
   ) AS request_outbox_integrity_valid
 FROM programs.national_issuance_review_intents i
),
request_integrity AS (
 SELECT parts.*,
   COALESCE(
     parts.request_snapshot_digest_valid
     AND parts.request_source_envelope_valid
     AND parts.request_center_selection_valid
     AND parts.request_authority_flags_clear
     AND parts.request_audit_integrity_valid
     AND parts.request_outbox_integrity_valid,
     false
   ) AS request_source_integrity_valid
 FROM request_integrity_parts parts
),
request_totals AS (
 SELECT
   i.request_id,
   i.program_id,
   i.national_version,
   ri.request_source_integrity_valid,
   ri.request_snapshot_digest_valid,
   ri.request_source_envelope_valid,
   ri.request_center_selection_valid,
   ri.request_authority_flags_clear,
   ri.request_audit_integrity_valid,
   ri.request_outbox_integrity_valid,
   count(n.note_id)::bigint AS inspection_note_count,
   max(n.recorded_at) AS latest_technical_observation_at,
   count(n.note_id) FILTER (WHERE
     d.original_request_integrity_valid IS TRUE
     AND d.observation_integrity_valid IS TRUE
     AND d.audit_and_outbox_integrity_valid IS TRUE
     AND d.source_observation_comparable IS TRUE
   )::bigint AS integrity_consistent_note_count,
   count(n.note_id) FILTER (WHERE
     d.source_observation_comparable IS DISTINCT FROM true
   )::bigint AS source_incomparable_note_count,
   count(n.note_id) FILTER (WHERE
     d.source_observation_comparable IS TRUE
     AND d.current_source_matches_observation IS FALSE
   )::bigint AS current_source_drift_note_count
 FROM programs.national_issuance_review_intents i
 JOIN request_integrity ri ON ri.request_id=i.request_id
 LEFT JOIN programs.national_issuance_review_inspection_notes n
   ON n.request_id=i.request_id
 LEFT JOIN programs.national_issuance_review_inspection_dossier d
   ON d.note_id=n.note_id
 GROUP BY i.request_id,i.program_id,i.national_version,ri.request_source_integrity_valid,
   ri.request_snapshot_digest_valid,ri.request_source_envelope_valid,
   ri.request_center_selection_valid,ri.request_authority_flags_clear,
   ri.request_audit_integrity_valid,ri.request_outbox_integrity_valid
)
SELECT
 totals.request_id,
 totals.program_id,
 totals.national_version,
 totals.inspection_note_count,
 totals.latest_technical_observation_at,
 COALESCE(latest.latest_candidate_count,0)::bigint
   AS latest_observation_candidate_count,
 COALESCE(latest.latest_candidate_count>1,false)
   AS latest_observation_ambiguous,
 CASE WHEN latest.latest_candidate_count=1
   THEN latest.source_observation_comparable ELSE NULL END
   AS latest_source_observation_comparable,
 CASE WHEN latest.latest_candidate_count=1
   THEN latest.current_source_matches_observation ELSE NULL END
   AS current_source_matches_latest_observation,
 CASE WHEN latest.latest_candidate_count=1
   THEN latest.publication_state_changed ELSE NULL END
   AS latest_publication_state_changed,
 CASE WHEN latest.latest_candidate_count=1
   THEN latest.center_registry_change_count ELSE NULL END
   AS latest_center_registry_change_count,
 CASE WHEN latest.latest_candidate_count=1
   THEN latest.exact_issuance_change_count ELSE NULL END
   AS latest_exact_issuance_change_count,
 CASE WHEN latest.latest_candidate_count=1 THEN
   latest.original_request_integrity_valid
   AND latest.observation_integrity_valid
   AND latest.audit_and_outbox_integrity_valid
   AND latest.source_observation_comparable
   ELSE NULL END AS latest_evidence_integrity_valid,
 false AS human_review_recorded,
 false AS issuance_authorized,
 false AS issuance_executed,
 totals.integrity_consistent_note_count,
 (totals.inspection_note_count-totals.integrity_consistent_note_count)
   AS integrity_inconsistent_note_count,
 totals.source_incomparable_note_count,
 totals.current_source_drift_note_count,
 totals.request_source_integrity_valid,
 totals.request_snapshot_digest_valid,
 totals.request_source_envelope_valid,
 totals.request_center_selection_valid,
 totals.request_authority_flags_clear,
 totals.request_audit_integrity_valid,
 totals.request_outbox_integrity_valid
FROM request_totals totals
LEFT JOIN LATERAL (
 SELECT
   count(*)::bigint AS latest_candidate_count,
   (array_agg(d.source_observation_comparable))[1]
     AS source_observation_comparable,
   (array_agg(d.current_source_matches_observation))[1]
     AS current_source_matches_observation,
   (array_agg(d.publication_state_changed))[1]
     AS publication_state_changed,
   (array_agg(d.center_registry_change_count))[1]
     AS center_registry_change_count,
   (array_agg(d.exact_issuance_change_count))[1]
     AS exact_issuance_change_count,
   (array_agg(d.original_request_integrity_valid))[1]
     AS original_request_integrity_valid,
   (array_agg(d.observation_integrity_valid))[1]
     AS observation_integrity_valid,
   (array_agg(d.audit_and_outbox_integrity_valid))[1]
     AS audit_and_outbox_integrity_valid
 FROM programs.national_issuance_review_inspection_dossier d
 WHERE d.request_id=totals.request_id
   AND d.inspection_recorded_at=totals.latest_technical_observation_at
) latest ON totals.latest_technical_observation_at IS NOT NULL;

REVOKE ALL ON programs.national_issuance_review_inspection_summary FROM PUBLIC;
COMMENT ON VIEW programs.national_issuance_review_inspection_summary IS
 'SECURITY INVOKER technical summary only. Adds component-level original-request digest, envelope, ordered selection, authority-flag, audit, and outbox integrity diagnostics to aggregate inspection history. Tied latest timestamps are ambiguous and latest details are withheld. No private source, center identifiers, requester text, reviewer identity, decision, authorization, or issuance.';
COMMIT;
