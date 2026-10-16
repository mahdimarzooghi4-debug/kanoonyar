-- S80: stable aggregate diagnostics for inspection dossier integrity.
-- Finding codes describe technical evidence only; they do not represent decisions.
BEGIN;

CREATE VIEW programs.national_issuance_review_inspection_findings
WITH (security_invoker = true) AS
SELECT
 'S80_INSPECTION_FINDING_V1'::text AS finding_contract_version,
 d.request_id,
 d.note_id,
 d.program_id,
 d.national_version,
 'ORIGINAL_REQUEST_INTEGRITY_INVALID'::text AS finding_code
FROM programs.national_issuance_review_inspection_dossier d
WHERE d.original_request_integrity_valid IS DISTINCT FROM true
UNION ALL
SELECT
 'S80_INSPECTION_FINDING_V1',
 d.request_id,
 d.note_id,
 d.program_id,
 d.national_version,
 'OBSERVATION_INTEGRITY_INVALID'
FROM programs.national_issuance_review_inspection_dossier d
WHERE d.observation_integrity_valid IS DISTINCT FROM true
UNION ALL
SELECT
 'S80_INSPECTION_FINDING_V1',
 d.request_id,
 d.note_id,
 d.program_id,
 d.national_version,
 'INSPECTION_AUDIT_EVIDENCE_INVALID'
FROM programs.national_issuance_review_inspection_dossier d
WHERE d.audit_and_outbox_integrity_valid IS DISTINCT FROM true
UNION ALL
SELECT
 'S80_INSPECTION_FINDING_V1',
 d.request_id,
 d.note_id,
 d.program_id,
 d.national_version,
 'SOURCE_OBSERVATION_NOT_COMPARABLE'
FROM programs.national_issuance_review_inspection_dossier d
WHERE d.source_observation_comparable IS DISTINCT FROM true;

REVOKE ALL ON programs.national_issuance_review_inspection_findings FROM PUBLIC;
COMMENT ON VIEW programs.national_issuance_review_inspection_findings IS
 'SECURITY INVOKER, versioned aggregate-only technical findings. Codes report inspection evidence integrity or comparability; they are not decisions, approvals, permissions, authorizations, or issuance commands.';
COMMIT;
