-- S63: prevent duplicate technical review audit/outbox envelopes.
-- Scoped only to S60 intents and S61 non-decision inspection notes.
BEGIN;

CREATE UNIQUE INDEX national_issuance_review_audit_target_uq
 ON governance.command_audit(action_code,target_type,target_id)
 WHERE (action_code='NATIONAL_ISSUANCE_REVIEW_INTENT_CAPTURED'
        AND target_type='national_issuance_review_intent')
    OR (action_code='NATIONAL_ISSUANCE_REVIEW_INSPECTION_NOTE_CAPTURED'
        AND target_type='national_issuance_review_inspection_note');

CREATE UNIQUE INDEX national_issuance_review_outbox_aggregate_uq
 ON governance.outbox_events(event_type,aggregate_type,aggregate_id)
 WHERE (event_type='NationalIssuanceReviewIntentCaptured'
        AND aggregate_type='national_issuance_review_intent')
    OR (event_type='NationalIssuanceReviewInspectionNoteCaptured'
        AND aggregate_type='national_issuance_review_inspection_note');

CREATE UNIQUE INDEX national_issuance_review_outbox_audit_uq
 ON governance.outbox_events(audit_id)
 WHERE event_type IN (
   'NationalIssuanceReviewIntentCaptured',
   'NationalIssuanceReviewInspectionNoteCaptured'
 );

COMMENT ON INDEX governance.national_issuance_review_audit_target_uq IS
 'Prevents duplicate append-only audit records for S60/S61 review evidence; grants no authority.';
COMMENT ON INDEX governance.national_issuance_review_outbox_aggregate_uq IS
 'Prevents duplicate S60/S61 outbox evidence for the same aggregate; does not dispatch events.';
COMMENT ON INDEX governance.national_issuance_review_outbox_audit_uq IS
 'Binds at most one national review outbox record to each audit record.';

COMMIT;
