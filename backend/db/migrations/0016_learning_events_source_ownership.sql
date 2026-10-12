-- Sprint 12: source-owned class/session and event PLANS plus typed report-source
-- evidence of existence. Source existence != delivery, attendance or verified outcome.
-- No runtime authorization, public read, command execute, publication or enrollment.
BEGIN;
CREATE SCHEMA IF NOT EXISTS learning;
CREATE SCHEMA IF NOT EXISTS events;
REVOKE ALL ON SCHEMA learning,events FROM PUBLIC;

CREATE TABLE learning.center_classes (
 center_id text NOT NULL REFERENCES centers.registry(center_id) ON DELETE RESTRICT,
 class_id uuid NOT NULL,
 title text NOT NULL CHECK(length(btrim(title))>0),
 created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
 PRIMARY KEY(center_id,class_id)
);
CREATE TABLE learning.class_sessions (
 center_id text NOT NULL,
 session_id uuid NOT NULL,
 class_id uuid NOT NULL,
 session_title text NOT NULL CHECK(length(btrim(session_title))>0),
 scheduled_at timestamptz NULL,
 created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
 PRIMARY KEY(center_id,session_id),
 CONSTRAINT class_session_same_center_fk
  FOREIGN KEY(center_id,class_id)
  REFERENCES learning.center_classes(center_id,class_id) ON DELETE RESTRICT
);
CREATE TABLE events.center_events (
 center_id text NOT NULL REFERENCES centers.registry(center_id) ON DELETE RESTRICT,
 event_id uuid NOT NULL,
 event_title text NOT NULL CHECK(length(btrim(event_title))>0),
 scheduled_at timestamptz NULL,
 created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
 PRIMARY KEY(center_id,event_id)
);

-- The first slice is immutable scheduling/identity evidence. Mutable schedules
-- require a separately versioned history contract, never silent UPDATE.
CREATE TRIGGER center_class_append_only BEFORE UPDATE OR DELETE
 ON learning.center_classes FOR EACH ROW
 EXECUTE FUNCTION programs.reject_draft_revision_mutation();
CREATE TRIGGER class_session_append_only BEFORE UPDATE OR DELETE
 ON learning.class_sessions FOR EACH ROW
 EXECUTE FUNCTION programs.reject_draft_revision_mutation();
CREATE TRIGGER center_event_append_only BEFORE UPDATE OR DELETE
 ON events.center_events FOR EACH ROW
 EXECUTE FUNCTION programs.reject_draft_revision_mutation();

REVOKE ALL ON TABLE learning.center_classes,learning.class_sessions,events.center_events FROM PUBLIC;
ALTER TABLE learning.center_classes ENABLE ROW LEVEL SECURITY;
ALTER TABLE learning.center_classes FORCE ROW LEVEL SECURITY;
ALTER TABLE learning.class_sessions ENABLE ROW LEVEL SECURITY;
ALTER TABLE learning.class_sessions FORCE ROW LEVEL SECURITY;
ALTER TABLE events.center_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE events.center_events FORCE ROW LEVEL SECURITY;
CREATE POLICY class_center_scope ON learning.center_classes
 USING(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));
CREATE POLICY session_center_scope ON learning.class_sessions
 USING(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));
CREATE POLICY event_center_scope ON events.center_events
 USING(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));

-- Match the report's own center AND exact typed owner/source identity.
-- Older opaque reports remain valid but do not automatically acquire lineage.
ALTER TABLE programs.center_activity_reports
 ADD CONSTRAINT report_exact_typed_identity_unique
 UNIQUE(center_id,report_id,source_kind,source_record_ref);
CREATE TABLE programs.activity_report_source_bindings (
 center_id text NOT NULL,
 report_id uuid NOT NULL,
 source_kind text NOT NULL,
 source_record_ref text NOT NULL,
 class_session_id uuid NULL,
 event_id uuid NULL,
 linked_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
 PRIMARY KEY(center_id,report_id),
 CONSTRAINT owner_source_type_and_identity CHECK (
   (source_kind='CLASS_SESSION' AND class_session_id IS NOT NULL
    AND event_id IS NULL AND source_record_ref=class_session_id::text)
   OR
   (source_kind='CENTER_EVENT' AND event_id IS NOT NULL
    AND class_session_id IS NULL AND source_record_ref=event_id::text)
 ),
 CONSTRAINT report_source_exact_identity_fk FOREIGN KEY
  (center_id,report_id,source_kind,source_record_ref)
  REFERENCES programs.center_activity_reports
  (center_id,report_id,source_kind,source_record_ref) ON DELETE RESTRICT,
 CONSTRAINT report_owned_session_fk FOREIGN KEY(center_id,class_session_id)
  REFERENCES learning.class_sessions(center_id,session_id) ON DELETE RESTRICT,
 CONSTRAINT report_owned_event_fk FOREIGN KEY(center_id,event_id)
  REFERENCES events.center_events(center_id,event_id) ON DELETE RESTRICT
);
CREATE INDEX activity_source_type_reference_idx
 ON programs.activity_report_source_bindings(center_id,source_kind,source_record_ref);
CREATE TRIGGER report_source_binding_append_only BEFORE UPDATE OR DELETE
 ON programs.activity_report_source_bindings FOR EACH ROW
 EXECUTE FUNCTION programs.reject_draft_revision_mutation();
REVOKE ALL ON TABLE programs.activity_report_source_bindings FROM PUBLIC;
ALTER TABLE programs.activity_report_source_bindings ENABLE ROW LEVEL SECURITY;
ALTER TABLE programs.activity_report_source_bindings FORCE ROW LEVEL SECURITY;
CREATE POLICY report_binding_center_scope ON programs.activity_report_source_bindings
 USING(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));

-- The wrapper is atomic: failure of the source FK also undoes its nested
-- report + audit + outbox insert. The supplied actor/source claims aren't
-- cryptographic identity, and references aren't verified performance evidence.
CREATE FUNCTION programs.record_center_sourced_activity_report(
 p_report_id uuid,p_center_id text,p_link_id uuid,p_draft_id uuid,
 p_source_kind text,p_source_id uuid,p_evidence_ref text,p_summary text,
 p_claimed_at timestamptz,p_actor text,
 p_audit_id uuid,p_event_id uuid,p_correlation_id text
) RETURNS uuid LANGUAGE plpgsql SECURITY INVOKER
 SET search_path=pg_catalog,pg_temp AS $fn$
BEGIN
 IF p_source_id IS NULL OR p_source_kind IS NULL
    OR p_source_kind NOT IN ('CLASS_SESSION','CENTER_EVENT') THEN
   RAISE EXCEPTION 'Unsupported typed source identity' USING ERRCODE='22023';
 END IF;
 PERFORM programs.record_center_activity_report(
   p_report_id,p_center_id,p_link_id,p_draft_id,
   p_source_kind,p_source_id::text,p_evidence_ref,p_summary,p_claimed_at,
   p_actor,p_audit_id,p_event_id,p_correlation_id);
 INSERT INTO programs.activity_report_source_bindings(
   center_id,report_id,source_kind,source_record_ref,class_session_id,event_id)
 VALUES(p_center_id,p_report_id,p_source_kind,p_source_id::text,
   CASE WHEN p_source_kind='CLASS_SESSION' THEN p_source_id ELSE NULL END,
   CASE WHEN p_source_kind='CENTER_EVENT' THEN p_source_id ELSE NULL END);
 RETURN p_report_id;
END;
$fn$;
REVOKE ALL ON FUNCTION programs.record_center_sourced_activity_report(
 uuid,text,uuid,uuid,text,uuid,text,text,timestamptz,text,uuid,uuid,text) FROM PUBLIC;
COMMENT ON FUNCTION programs.record_center_sourced_activity_report(
 uuid,text,uuid,uuid,text,uuid,text,text,timestamptz,text,uuid,uuid,text)
 IS 'Typed source existence only; no verified delivery, attendance, outcome, approved evidence, or runtime authorization.';
COMMIT;
