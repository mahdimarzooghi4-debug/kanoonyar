-- S72: add aggregate, non-authorizing integrity and current-drift counts
-- across the full technical inspection history for each request.
BEGIN;

CREATE OR REPLACE VIEW programs.national_issuance_review_inspection_summary
WITH (security_invoker = true) AS
WITH request_totals AS (
 SELECT
   i.request_id,
   i.program_id,
   i.national_version,
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
 LEFT JOIN programs.national_issuance_review_inspection_notes n
   ON n.request_id=i.request_id
 LEFT JOIN programs.national_issuance_review_inspection_dossier d
   ON d.note_id=n.note_id
 GROUP BY i.request_id,i.program_id,i.national_version
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
 totals.current_source_drift_note_count
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
 'SECURITY INVOKER technical summary only. Reports aggregate full-history integrity/comparability/current-drift counts; tied latest timestamps are ambiguous and latest details are withheld. No private source, reviewer identity, decision, authorization, or issuance.';
COMMIT;
