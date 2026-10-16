-- S82: classify current-source freshness for immutable technical observations.
BEGIN;

CREATE VIEW programs.national_issuance_review_source_freshness_diagnostics
WITH (security_invoker = true) AS
SELECT
 'S82_SOURCE_FRESHNESS_V1'::text AS diagnostic_contract_version,
 d.request_id,
 d.note_id,
 d.program_id,
 d.national_version,
 'SOURCE_COMPARISON_UNAVAILABLE'::text AS diagnostic_code,
 NULL::bigint AS change_count
FROM programs.national_issuance_review_inspection_dossier d
WHERE d.source_observation_comparable IS DISTINCT FROM true
UNION ALL
SELECT
 'S82_SOURCE_FRESHNESS_V1',
 d.request_id,
 d.note_id,
 d.program_id,
 d.national_version,
 'CURRENT_SOURCE_MATCHES_OBSERVATION',
 0::bigint
FROM programs.national_issuance_review_inspection_dossier d
WHERE d.source_observation_comparable IS TRUE
  AND d.current_source_matches_observation IS TRUE
UNION ALL
SELECT
 'S82_SOURCE_FRESHNESS_V1',
 d.request_id,
 d.note_id,
 d.program_id,
 d.national_version,
 'PUBLICATION_STATE_CHANGED',
 1::bigint
FROM programs.national_issuance_review_inspection_dossier d
WHERE d.source_observation_comparable IS TRUE
  AND d.publication_state_changed IS TRUE
UNION ALL
SELECT
 'S82_SOURCE_FRESHNESS_V1',
 d.request_id,
 d.note_id,
 d.program_id,
 d.national_version,
 'CENTER_REGISTRY_CHANGED',
 d.center_registry_change_count::bigint
FROM programs.national_issuance_review_inspection_dossier d
WHERE d.source_observation_comparable IS TRUE
  AND d.center_registry_change_count>0
UNION ALL
SELECT
 'S82_SOURCE_FRESHNESS_V1',
 d.request_id,
 d.note_id,
 d.program_id,
 d.national_version,
 'EXACT_ISSUANCE_CHANGED',
 d.exact_issuance_change_count::bigint
FROM programs.national_issuance_review_inspection_dossier d
WHERE d.source_observation_comparable IS TRUE
  AND d.exact_issuance_change_count>0
UNION ALL
SELECT
 'S82_SOURCE_FRESHNESS_V1',
 d.request_id,
 d.note_id,
 d.program_id,
 d.national_version,
 'SOURCE_DRIFT_UNCLASSIFIED',
 1::bigint
FROM programs.national_issuance_review_inspection_dossier d
WHERE d.source_observation_comparable IS TRUE
  AND d.current_source_matches_observation IS DISTINCT FROM true
  AND d.publication_state_changed IS DISTINCT FROM true
  AND COALESCE(d.center_registry_change_count,0)=0
  AND COALESCE(d.exact_issuance_change_count,0)=0;

REVOKE ALL ON programs.national_issuance_review_source_freshness_diagnostics FROM PUBLIC;
COMMENT ON VIEW programs.national_issuance_review_source_freshness_diagnostics IS
 'SECURITY INVOKER aggregate-only technical freshness diagnostics. Change counts are code-specific. Non-comparable observations expose no drift counts. No source identifiers, decisions, approvals, permissions, authorization, or issuance.';
COMMIT;
