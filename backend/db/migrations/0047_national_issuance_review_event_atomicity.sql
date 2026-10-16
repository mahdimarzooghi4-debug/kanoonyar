-- S64: make each national review source/audit/outbox triple transaction-complete.
-- Deferred checks reject incomplete or orphan S60/S61 records at commit.
BEGIN;

CREATE FUNCTION programs.require_national_issuance_review_event_pair()
RETURNS trigger
LANGUAGE plpgsql
SECURITY INVOKER
SET search_path=pg_catalog,pg_temp
AS $fn$
DECLARE
 v_family text;
 v_target_id text;
 v_pair_count bigint;
BEGIN
 IF TG_TABLE_SCHEMA='programs'
    AND TG_TABLE_NAME='national_issuance_review_intents' THEN
   v_family := 'intent';
   v_target_id := NEW.request_id::text;
 ELSIF TG_TABLE_SCHEMA='programs'
       AND TG_TABLE_NAME='national_issuance_review_inspection_notes' THEN
   v_family := 'inspection';
   v_target_id := NEW.note_id::text;
 ELSIF TG_TABLE_SCHEMA='governance'
       AND TG_TABLE_NAME='command_audit' THEN
   IF NEW.action_code='NATIONAL_ISSUANCE_REVIEW_INTENT_CAPTURED'
      AND NEW.target_type='national_issuance_review_intent' THEN
     v_family := 'intent';
   ELSIF NEW.action_code='NATIONAL_ISSUANCE_REVIEW_INSPECTION_NOTE_CAPTURED'
      AND NEW.target_type='national_issuance_review_inspection_note' THEN
     v_family := 'inspection';
   ELSE
     RETURN NULL;
   END IF;
   v_target_id := NEW.target_id;
 ELSIF TG_TABLE_SCHEMA='governance'
       AND TG_TABLE_NAME='outbox_events' THEN
   IF NEW.event_type='NationalIssuanceReviewIntentCaptured'
      AND NEW.aggregate_type='national_issuance_review_intent' THEN
     v_family := 'intent';
   ELSIF NEW.event_type='NationalIssuanceReviewInspectionNoteCaptured'
      AND NEW.aggregate_type='national_issuance_review_inspection_note' THEN
     v_family := 'inspection';
   ELSE
     RETURN NULL;
   END IF;
   v_target_id := NEW.aggregate_id;
 ELSE
   RAISE EXCEPTION 'Unexpected S64 integrity trigger relation'
     USING ERRCODE='23514';
 END IF;

 SELECT count(*) INTO v_pair_count
 FROM governance.command_audit a
 JOIN governance.outbox_events o ON o.audit_id=a.audit_id
 WHERE
   (v_family='intent'
    AND EXISTS (
      SELECT 1 FROM programs.national_issuance_review_intents i
      WHERE i.request_id::text=v_target_id
        AND a.action_code='NATIONAL_ISSUANCE_REVIEW_INTENT_CAPTURED'
        AND a.target_type='national_issuance_review_intent'
        AND a.target_id=i.request_id::text
        AND a.metadata->>'source_digest_sha256'=i.source_digest_sha256
        AND a.metadata->>'requester_authorized'='false'
        AND a.metadata->>'human_review_recorded'='false'
        AND a.metadata->>'issuance_authorized'='false'
        AND a.metadata->>'issuance_executed'='false'
        AND o.event_type='NationalIssuanceReviewIntentCaptured'
        AND o.aggregate_type='national_issuance_review_intent'
        AND o.aggregate_id=i.request_id::text
        AND o.payload->>'request_id'=i.request_id::text
        AND o.payload->>'recorded_only'='true'
        AND o.payload->>'requester_authorized'='false'
        AND o.payload->>'human_review_recorded'='false'
        AND o.payload->>'issuance_authorized'='false'
        AND o.payload->>'issuance_executed'='false'
    ))
   OR
   (v_family='inspection'
    AND EXISTS (
      SELECT 1 FROM programs.national_issuance_review_inspection_notes n
      WHERE n.note_id::text=v_target_id
        AND a.action_code='NATIONAL_ISSUANCE_REVIEW_INSPECTION_NOTE_CAPTURED'
        AND a.target_type='national_issuance_review_inspection_note'
        AND a.target_id=n.note_id::text
        AND a.metadata->>'request_id'=n.request_id::text
        AND a.metadata->>'source_changed'=
          CASE WHEN n.source_changed_since_request THEN 'true' ELSE 'false' END
        AND a.metadata->>'unverified_actor'='true'
        AND a.metadata->>'human_review_recorded'='false'
        AND a.metadata->>'issuance_authorized'='false'
        AND a.metadata->>'issuance_executed'='false'
        AND o.event_type='NationalIssuanceReviewInspectionNoteCaptured'
        AND o.aggregate_type='national_issuance_review_inspection_note'
        AND o.aggregate_id=n.note_id::text
        AND o.payload->>'note_id'=n.note_id::text
        AND o.payload->>'request_id'=n.request_id::text
        AND o.payload->>'source_changed'=
          CASE WHEN n.source_changed_since_request THEN 'true' ELSE 'false' END
        AND o.payload->>'technical_only'='true'
        AND o.payload->>'human_review_recorded'='false'
        AND o.payload->>'issuance_authorized'='false'
        AND o.payload->>'issuance_executed'='false'
    ));

 IF v_pair_count<>1 THEN
   RAISE EXCEPTION 'National issuance review evidence requires exactly one matching source/audit/outbox triple (family %, target %)',
     v_family,v_target_id
     USING ERRCODE='23514';
 END IF;
 RETURN NULL;
END;
$fn$;

REVOKE ALL ON FUNCTION programs.require_national_issuance_review_event_pair() FROM PUBLIC;

CREATE CONSTRAINT TRIGGER national_issuance_intent_event_pair_complete
 AFTER INSERT ON programs.national_issuance_review_intents
 DEFERRABLE INITIALLY DEFERRED
 FOR EACH ROW EXECUTE FUNCTION programs.require_national_issuance_review_event_pair();

CREATE CONSTRAINT TRIGGER national_issuance_inspection_event_pair_complete
 AFTER INSERT ON programs.national_issuance_review_inspection_notes
 DEFERRABLE INITIALLY DEFERRED
 FOR EACH ROW EXECUTE FUNCTION programs.require_national_issuance_review_event_pair();

CREATE CONSTRAINT TRIGGER national_issuance_audit_event_pair_complete
 AFTER INSERT ON governance.command_audit
 DEFERRABLE INITIALLY DEFERRED
 FOR EACH ROW EXECUTE FUNCTION programs.require_national_issuance_review_event_pair();

CREATE CONSTRAINT TRIGGER national_issuance_outbox_event_pair_complete
 AFTER INSERT ON governance.outbox_events
 DEFERRABLE INITIALLY DEFERRED
 FOR EACH ROW EXECUTE FUNCTION programs.require_national_issuance_review_event_pair();

COMMENT ON FUNCTION programs.require_national_issuance_review_event_pair() IS
 'Deferred SECURITY INVOKER integrity guard for S60/S61 source/audit/outbox triples; never authenticates, approves, grants, issues, or dispatches.';
COMMIT;
