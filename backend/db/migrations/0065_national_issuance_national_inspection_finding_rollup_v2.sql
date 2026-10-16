-- S90: aggregate stable request and inspection technical finding codes.
-- Counts are diagnostics only; this view does not make review or issuance decisions.
BEGIN;

CREATE VIEW programs.national_issuance_review_national_inspection_rollup_v2
WITH (security_invoker = true) AS
WITH request_findings AS (
 SELECT program_id,national_version,
   count(*) FILTER (WHERE finding_code='SOURCE_SNAPSHOT_DIGEST_INVALID')::bigint
     AS source_snapshot_digest_invalid_request_count,
   count(*) FILTER (WHERE finding_code='SOURCE_ENVELOPE_INVALID')::bigint
     AS source_envelope_invalid_request_count,
   count(*) FILTER (WHERE finding_code='CENTER_SELECTION_MISMATCH')::bigint
     AS center_selection_mismatch_request_count,
   count(*) FILTER (WHERE finding_code='AUTHORITY_FLAGS_NOT_CLEAR')::bigint
     AS authority_flags_not_clear_request_count,
   count(*) FILTER (WHERE finding_code='AUDIT_EVIDENCE_INVALID')::bigint
     AS audit_evidence_invalid_request_count,
   count(*) FILTER (WHERE finding_code='OUTBOX_EVIDENCE_INVALID')::bigint
     AS outbox_evidence_invalid_request_count
 FROM programs.national_issuance_review_integrity_findings
 GROUP BY program_id,national_version
),
inspection_findings AS (
 SELECT program_id,national_version,
   count(*) FILTER (WHERE finding_code='ORIGINAL_REQUEST_INTEGRITY_INVALID')::bigint
     AS original_request_integrity_invalid_note_count,
   count(*) FILTER (WHERE finding_code='OBSERVATION_INTEGRITY_INVALID')::bigint
     AS observation_integrity_invalid_note_count,
   count(*) FILTER (WHERE finding_code='INSPECTION_AUDIT_EVIDENCE_INVALID')::bigint
     AS inspection_audit_evidence_invalid_note_count,
   count(*) FILTER (WHERE finding_code='SOURCE_OBSERVATION_NOT_COMPARABLE')::bigint
     AS source_observation_not_comparable_note_count
 FROM programs.national_issuance_review_inspection_findings
 GROUP BY program_id,national_version
)
SELECT
 'S90_NATIONAL_INSPECTION_ROLLUP_V2'::text AS rollup_contract_version,
 b.program_id,
 b.national_version,
 b.review_request_count,
 b.requests_without_inspection_count,
 b.requests_with_inspection_count,
 b.inspection_note_count,
 b.integrity_consistent_note_count,
 b.integrity_inconsistent_note_count,
 b.source_comparison_unavailable_note_count,
 b.current_source_match_note_count,
 b.current_source_drift_note_count,
 b.source_freshness_unclassified_note_count,
 coalesce(r.source_snapshot_digest_invalid_request_count,0)::bigint
   AS source_snapshot_digest_invalid_request_count,
 coalesce(r.source_envelope_invalid_request_count,0)::bigint
   AS source_envelope_invalid_request_count,
 coalesce(r.center_selection_mismatch_request_count,0)::bigint
   AS center_selection_mismatch_request_count,
 coalesce(r.authority_flags_not_clear_request_count,0)::bigint
   AS authority_flags_not_clear_request_count,
 coalesce(r.audit_evidence_invalid_request_count,0)::bigint
   AS audit_evidence_invalid_request_count,
 coalesce(r.outbox_evidence_invalid_request_count,0)::bigint
   AS outbox_evidence_invalid_request_count,
 coalesce(n.original_request_integrity_invalid_note_count,0)::bigint
   AS original_request_integrity_invalid_note_count,
 coalesce(n.observation_integrity_invalid_note_count,0)::bigint
   AS observation_integrity_invalid_note_count,
 coalesce(n.inspection_audit_evidence_invalid_note_count,0)::bigint
   AS inspection_audit_evidence_invalid_note_count,
 coalesce(n.source_observation_not_comparable_note_count,0)::bigint
   AS source_observation_not_comparable_note_count,
 b.latest_technical_observation_at,
 b.human_review_recorded,
 b.issuance_authorized,
 b.issuance_executed
FROM programs.national_issuance_review_national_inspection_rollup b
LEFT JOIN request_findings r USING (program_id,national_version)
LEFT JOIN inspection_findings n USING (program_id,national_version);

REVOKE ALL ON programs.national_issuance_review_national_inspection_rollup_v2 FROM PUBLIC;
COMMENT ON VIEW programs.national_issuance_review_national_inspection_rollup_v2 IS
 'SECURITY INVOKER, aggregate-only S90 technical finding counts by national program/version. Finding-code counts expose no center, request, note, snapshot, or person identifiers and are not reviewer decisions, approvals, authorizations, or issuance.';
COMMIT;
