-- S71: a compact read-only summary for technical inspection evidence.
-- The latest timestamp is reported without selecting arbitrarily when tied.
BEGIN;

CREATE VIEW programs.national_issuance_review_inspection_summary
WITH (security_invoker = true) AS
WITH request_totals AS (
 SELECT
   i.request_id,
   i.program_id,
   i.national_version,
   count(n.note_id)::bigint AS inspection_note_count,
   max(n.recorded_at) AS latest_technical_observation_at
 FROM programs.national_issuance_review_intents i
 LEFT JOIN programs.national_issuance_review_inspection_notes n
   ON n.request_id=i.request_id
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
 false AS issuance_executed
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
 'SECURITY INVOKER technical summary only. Counts notes per request; tied latest timestamps are marked ambiguous and latest details are withheld. No private source, reviewer identity, decision, authorization, or issuance.';
COMMIT;
