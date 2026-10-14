-- S29: versioned PRIVATE event plans reusing the S12 source event identity.
-- No publication, occurrence, attendance, success, registration or payment.
BEGIN;
CREATE TABLE events.center_event_plan_bindings (
 center_id text NOT NULL,
 event_id uuid NOT NULL,
 draft_id uuid NOT NULL,
 pinned_plan_revision bigint NOT NULL CHECK(pinned_plan_revision>0),
 created_by_subject text NOT NULL CHECK(length(btrim(created_by_subject))>0),
 created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
 PRIMARY KEY(center_id,event_id),
 FOREIGN KEY(center_id,event_id) REFERENCES events.center_events(center_id,event_id) ON DELETE RESTRICT,
 FOREIGN KEY(center_id,draft_id,pinned_plan_revision)
  REFERENCES programs.center_plan_draft_revisions(center_id,draft_id,revision) ON DELETE RESTRICT
);
CREATE TABLE events.center_event_plan_revisions (
 center_id text NOT NULL,
 event_id uuid NOT NULL,
 revision bigint NOT NULL CHECK(revision>0),
 title text NOT NULL CHECK(length(btrim(title)) BETWEEN 1 AND 200),
 purpose text NOT NULL CHECK(length(btrim(purpose)) BETWEEN 1 AND 500),
 location_description text NOT NULL CHECK(length(btrim(location_description)) BETWEEN 1 AND 200),
 scheduled_at timestamptz NOT NULL,
 changed_by_subject text NOT NULL CHECK(length(btrim(changed_by_subject))>0),
 recorded_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
 PRIMARY KEY(center_id,event_id,revision),
 FOREIGN KEY(center_id,event_id) REFERENCES events.center_event_plan_bindings(center_id,event_id) ON DELETE RESTRICT
);
CREATE INDEX center_event_plan_recent ON events.center_event_plan_revisions(center_id,event_id,revision DESC);
CREATE TRIGGER center_event_plan_binding_immutable BEFORE UPDATE OR DELETE
 ON events.center_event_plan_bindings FOR EACH ROW EXECUTE FUNCTION programs.reject_draft_revision_mutation();
CREATE TRIGGER center_event_plan_revision_immutable BEFORE UPDATE OR DELETE
 ON events.center_event_plan_revisions FOR EACH ROW EXECUTE FUNCTION programs.reject_draft_revision_mutation();
ALTER TABLE events.center_event_plan_bindings ENABLE ROW LEVEL SECURITY;
ALTER TABLE events.center_event_plan_bindings FORCE ROW LEVEL SECURITY;
ALTER TABLE events.center_event_plan_revisions ENABLE ROW LEVEL SECURITY;
ALTER TABLE events.center_event_plan_revisions FORCE ROW LEVEL SECURITY;
CREATE POLICY event_binding_scope ON events.center_event_plan_bindings
 USING(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));
CREATE POLICY event_revision_scope ON events.center_event_plan_revisions
 USING(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));
REVOKE ALL ON events.center_event_plan_bindings,events.center_event_plan_revisions FROM PUBLIC;
DO $owner$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_event_plan_owner') THEN
  CREATE ROLE kanoonyar_event_plan_owner NOLOGIN NOINHERIT NOSUPERUSER NOBYPASSRLS
   NOCREATEDB NOCREATEROLE NOREPLICATION;
 END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_event_plan_owner'
  AND NOT rolcanlogin AND NOT rolsuper AND NOT rolbypassrls
  AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication) THEN
  RAISE EXCEPTION 'Unsafe event plan owner';
 END IF;
END;$owner$;
GRANT USAGE ON SCHEMA events,programs,centers,governance TO kanoonyar_event_plan_owner;
GRANT SELECT ON centers.registry,centers.memberships,programs.center_plan_draft_revisions
 TO kanoonyar_event_plan_owner;
GRANT SELECT,INSERT ON events.center_events,events.center_event_plan_bindings,
 events.center_event_plan_revisions TO kanoonyar_event_plan_owner;
GRANT INSERT ON governance.command_audit,governance.outbox_events TO kanoonyar_event_plan_owner;
CREATE FUNCTION events.append_center_event_plan(
 p_center text,p_event uuid,p_expected bigint,p_draft uuid,p_pinned bigint,
 p_title text,p_purpose text,p_location text,p_scheduled timestamptz,
 p_actor text,p_audit uuid,p_outbox uuid,p_correlation text
) RETURNS bigint LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $fn$
DECLARE v_draft uuid;v_pinned bigint;v_current bigint;v_next bigint;
BEGIN
 IF p_center IS NULL OR btrim(p_center)='' OR p_event IS NULL
   OR p_expected IS NULL OR p_expected<0 OR p_draft IS NULL OR p_pinned IS NULL OR p_pinned<1
   OR p_title IS NULL OR length(btrim(p_title)) NOT BETWEEN 1 AND 200
   OR p_purpose IS NULL OR length(btrim(p_purpose)) NOT BETWEEN 1 AND 500
   OR p_location IS NULL OR length(btrim(p_location)) NOT BETWEEN 1 AND 200
   OR p_scheduled IS NULL OR p_actor IS NULL OR btrim(p_actor)=''
   OR p_audit IS NULL OR p_outbox IS NULL OR p_correlation IS NULL OR btrim(p_correlation)=''
 THEN RAISE EXCEPTION 'Invalid private event plan' USING ERRCODE='22023'; END IF;
 IF p_center IS DISTINCT FROM NULLIF(current_setting('kanoonyar.center_id',true),'')
   OR NOT EXISTS(SELECT 1 FROM centers.memberships m JOIN centers.registry c
    ON c.center_id=m.center_id WHERE m.center_id=p_center AND m.subject_id=p_actor
     AND m.is_active AND c.is_active)
 THEN RAISE EXCEPTION 'Center scope or active membership absent' USING ERRCODE='42501'; END IF;
 IF NOT EXISTS(SELECT 1 FROM programs.center_plan_draft_revisions
   WHERE center_id=p_center AND draft_id=p_draft AND revision=p_pinned)
 THEN RAISE EXCEPTION 'Pinned plan revision absent' USING ERRCODE='23503'; END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('event-plan:'||p_center||':'||p_event::text,0));
 SELECT draft_id,pinned_plan_revision INTO v_draft,v_pinned
 FROM events.center_event_plan_bindings WHERE center_id=p_center AND event_id=p_event;
 IF p_expected=0 THEN
  IF v_draft IS NOT NULL THEN RAISE EXCEPTION 'Event already exists' USING ERRCODE='40001'; END IF;
  -- S12 immutable source event is the canonical typed report source, NOT observed execution.
  INSERT INTO events.center_events(center_id,event_id,event_title,scheduled_at)
   VALUES(p_center,p_event,p_title,p_scheduled);
  INSERT INTO events.center_event_plan_bindings(center_id,event_id,draft_id,pinned_plan_revision,created_by_subject)
   VALUES(p_center,p_event,p_draft,p_pinned,p_actor);
  v_current:=0;
 ELSE
  IF v_draft IS NULL OR v_draft<>p_draft OR v_pinned<>p_pinned
  THEN RAISE EXCEPTION 'Immutable event plan binding changed' USING ERRCODE='23514'; END IF;
  SELECT COALESCE(max(revision),0) INTO v_current FROM events.center_event_plan_revisions
   WHERE center_id=p_center AND event_id=p_event;
 END IF;
 IF v_current<>p_expected THEN RAISE EXCEPTION 'Stale event plan revision' USING ERRCODE='40001'; END IF;
 v_next:=v_current+1;
 INSERT INTO events.center_event_plan_revisions(center_id,event_id,revision,title,purpose,
   location_description,scheduled_at,changed_by_subject)
 VALUES(p_center,p_event,v_next,p_title,p_purpose,p_location,p_scheduled,p_actor);
 INSERT INTO governance.command_audit(audit_id,occurred_at,actor_subject_id,center_id,
   action_code,target_type,target_id,correlation_id,metadata)
 VALUES(p_audit,transaction_timestamp(),p_actor,p_center,'CENTER_EVENT_PLAN_REVISION_APPENDED',
  'center_event_plan',p_event::text,p_correlation,
  jsonb_build_object('revision',v_next,'draft_id',p_draft,'pinned_plan_revision',p_pinned,
   'visibility','PRIVATE_DRAFT'));
 INSERT INTO governance.outbox_events(event_id,audit_id,event_type,aggregate_type,aggregate_id,
   occurred_at,payload)
 VALUES(p_outbox,p_audit,'CenterEventPlanRevisionAppended','center_event_plan',
  p_event::text,transaction_timestamp(),jsonb_build_object('center_id',p_center,
   'event_id',p_event,'revision',v_next,'draft_id',p_draft,
   'pinned_plan_revision',p_pinned,'is_published',false,'is_executed',false));
 RETURN v_next;
END;$fn$;
ALTER FUNCTION events.append_center_event_plan(text,uuid,bigint,uuid,bigint,text,text,text,timestamptz,text,uuid,uuid,text)
 OWNER TO kanoonyar_event_plan_owner;
REVOKE ALL ON FUNCTION events.append_center_event_plan(text,uuid,bigint,uuid,bigint,text,text,text,timestamptz,text,uuid,uuid,text) FROM PUBLIC;
COMMENT ON TABLE events.center_event_plan_revisions IS
 'Private prospective event schedule history. Canonical S12 event identity remains immutable. No attendance or verified delivery.';
COMMIT;
