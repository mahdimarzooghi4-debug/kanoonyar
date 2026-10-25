-- S83: non-authorizing program/version rollup for national inspection coverage.
BEGIN;

CREATE VIEW programs.national_issuance_review_national_inspection_rollup
WITH (security_invoker = true) AS
WITH note_evidence AS (
 SELECT
   d.request_id,
   d.note_id,
   (
     d.original_request_integrity_valid IS TRUE
     AND d.observation_integrity_valid IS TRUE
     AND d.audit_and_outbox_integrity_valid IS TRUE
     AND d.source_observation_comparable IS TRUE
   ) AS integrity_consistent,
   d.source_observation_comparable IS DISTINCT FROM true
     AS source_comparison_unavailable,
   (
     d.source_observation_comparable IS TRUE
     AND d.current_source_matches_observation IS TRUE
   ) AS current_source_matches,
   (
     d.source_observation_comparable IS TRUE
     AND d.current_source_matches_observation IS FALSE
   ) AS current_source_drift,
   (
     d.source_observation_comparable IS TRUE
     AND d.current_source_matches_observation IS NULL
   ) AS freshness_unclassified
 FROM programs.national_issuance_review_inspection_dossier d
),
request_rollup AS (
 SELECT
   i.program_id,
   i.national_version,
   i.request_id,
   max(s.inspection_note_count)::bigint AS inspection_note_count,
   max(s.latest_technical_observation_at) AS latest_technical_observation_at,
   max(s.integrity_consistent_note_count)::bigint AS integrity_consistent_note_count,
   max(s.integrity_inconsistent_note_count)::bigint AS integrity_inconsistent_note_count,
   count(n.note_id) FILTER (WHERE n.source_comparison_unavailable)::bigint
     AS source_comparison_unavailable_note_count,
   count(n.note_id) FILTER (WHERE n.current_source_matches)::bigint
     AS current_source_match_note_count,
   count(n.note_id) FILTER (WHERE n.current_source_drift)::bigint
     AS current_source_drift_note_count,
   count(n.note_id) FILTER (WHERE n.freshness_unclassified)::bigint
     AS source_freshness_unclassified_note_count
 FROM programs.national_issuance_review_intents i
 JOIN programs.national_issuance_review_inspection_summary s
   ON s.request_id=i.request_id
 LEFT JOIN note_evidence n ON n.request_id=i.request_id
 GROUP BY i.program_id,i.national_version,i.request_id
)
SELECT
 'S83_NATIONAL_INSPECTION_ROLLUP_V1'::text AS rollup_contract_version,
 r.program_id,
 r.national_version,
 count(*)::bigint AS review_request_count,
 count(*) FILTER (WHERE r.inspection_note_count=0)::bigint
   AS requests_without_inspection_count,
 count(*) FILTER (WHERE r.inspection_note_count>0)::bigint
   AS requests_with_inspection_count,
 sum(r.inspection_note_count)::bigint AS inspection_note_count,
 sum(r.integrity_consistent_note_count)::bigint
   AS integrity_consistent_note_count,
 sum(r.integrity_inconsistent_note_count)::bigint
   AS integrity_inconsistent_note_count,
 sum(r.source_comparison_unavailable_note_count)::bigint
   AS source_comparison_unavailable_note_count,
 sum(r.current_source_match_note_count)::bigint
   AS current_source_match_note_count,
 sum(r.current_source_drift_note_count)::bigint
   AS current_source_drift_note_count,
 sum(r.source_freshness_unclassified_note_count)::bigint
   AS source_freshness_unclassified_note_count,
 max(r.latest_technical_observation_at)
   AS latest_technical_observation_at,
 false AS human_review_recorded,
 false AS issuance_authorized,
 false AS issuance_executed
FROM request_rollup r
GROUP BY r.program_id,r.national_version;

REVOKE ALL ON programs.national_issuance_review_national_inspection_rollup FROM PUBLIC;
COMMENT ON VIEW programs.national_issuance_review_national_inspection_rollup IS
 'SECURITY INVOKER aggregate-only national technical coverage by program/version. Exposes no center identifiers, request identifiers, source snapshots, note text, or reviewer identity. Counts do not indicate review, approval, authorization, or issuance.';
COMMIT;
