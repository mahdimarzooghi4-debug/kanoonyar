-- S62: virtual source-integrity dossier for technical inspection only.
-- No reviewer identity, decision, authorization, or issuance is produced.
BEGIN;

CREATE VIEW programs.national_issuance_review_inspection_dossier
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
 ) AS original_request_integrity_valid,
 (
   n.observed_digest_sha256 =
     encode(sha256(convert_to(n.observed_source_snapshot::text,'UTF8')),'hex')
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
 false AS issuance_executed
FROM programs.national_issuance_review_inspection_notes n
JOIN programs.national_issuance_review_intents i
  ON i.request_id = n.request_id;

REVOKE ALL ON programs.national_issuance_review_inspection_dossier FROM PUBLIC;
COMMENT ON VIEW programs.national_issuance_review_inspection_dossier IS
 'Virtual technical integrity projection only. SECURITY INVOKER; no operational role grants, no private note text or center list, never a human review or issuance decision.';
COMMIT;
