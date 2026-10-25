-- S76: expose stable, non-authorizing machine-readable integrity finding codes.
BEGIN;

CREATE VIEW programs.national_issuance_review_integrity_findings
WITH (security_invoker = true) AS
SELECT
 'S76_INTEGRITY_FINDING_V1'::text AS finding_contract_version,
 s.request_id,
 s.program_id,
 s.national_version,
 'SOURCE_SNAPSHOT_DIGEST_INVALID'::text AS finding_code
FROM programs.national_issuance_review_inspection_summary s
WHERE s.request_snapshot_digest_valid IS DISTINCT FROM true
UNION ALL
SELECT
 'S76_INTEGRITY_FINDING_V1',
 s.request_id,
 s.program_id,
 s.national_version,
 'SOURCE_ENVELOPE_INVALID'
FROM programs.national_issuance_review_inspection_summary s
WHERE s.request_source_envelope_valid IS DISTINCT FROM true
UNION ALL
SELECT
 'S76_INTEGRITY_FINDING_V1',
 s.request_id,
 s.program_id,
 s.national_version,
 'CENTER_SELECTION_MISMATCH'
FROM programs.national_issuance_review_inspection_summary s
WHERE s.request_center_selection_valid IS DISTINCT FROM true
UNION ALL
SELECT
 'S76_INTEGRITY_FINDING_V1',
 s.request_id,
 s.program_id,
 s.national_version,
 'AUTHORITY_FLAGS_NOT_CLEAR'
FROM programs.national_issuance_review_inspection_summary s
WHERE s.request_authority_flags_clear IS DISTINCT FROM true
UNION ALL
SELECT
 'S76_INTEGRITY_FINDING_V1',
 s.request_id,
 s.program_id,
 s.national_version,
 'AUDIT_EVIDENCE_INVALID'
FROM programs.national_issuance_review_inspection_summary s
WHERE s.request_audit_integrity_valid IS DISTINCT FROM true
UNION ALL
SELECT
 'S76_INTEGRITY_FINDING_V1',
 s.request_id,
 s.program_id,
 s.national_version,
 'OUTBOX_EVIDENCE_INVALID'
FROM programs.national_issuance_review_inspection_summary s
WHERE s.request_outbox_integrity_valid IS DISTINCT FROM true;

REVOKE ALL ON programs.national_issuance_review_integrity_findings FROM PUBLIC;
COMMENT ON VIEW programs.national_issuance_review_integrity_findings IS
 'SECURITY INVOKER, aggregate-only, versioned technical integrity findings. One row per failed diagnostic component. Findings are not reviewer decisions, approvals, authorizations, issuance commands, or proof of human review.';
COMMIT;
