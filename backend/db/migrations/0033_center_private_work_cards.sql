-- Sprint 33 — Private local-plan work-card planning; no assignment or performed status.
BEGIN;
CREATE SCHEMA operations;
REVOKE ALL ON SCHEMA operations FROM PUBLIC;
CREATE TABLE operations.center_work_cards (
 center_id text NOT NULL REFERENCES centers.registry(center_id) ON DELETE RESTRICT,
 work_card_id uuid NOT NULL,
 draft_id uuid NOT NULL,
 pinned_plan_revision bigint NOT NULL CHECK (pinned_plan_revision>0),
 created_by_subject text NOT NULL CHECK(length(btrim(created_by_subject))>0),
 recorded_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
 PRIMARY KEY(center_id,work_card_id),
 FOREIGN KEY(center_id,draft_id,pinned_plan_revision)
  REFERENCES programs.center_plan_draft_revisions(center_id,draft_id,revision)
  ON DELETE RESTRICT
);
CREATE TABLE operations.center_work_card_revisions (
 center_id text NOT NULL,
 work_card_id uuid NOT NULL,
 revision bigint NOT NULL CHECK(revision>0),
 title text NOT NULL CHECK(length(btrim(title)) BETWEEN 1 AND 160),
 planning_note text NOT NULL CHECK(length(planning_note)<=1200),
 planned_due_at timestamptz,
 changed_by_subject text NOT NULL CHECK(length(btrim(changed_by_subject))>0),
 recorded_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
 PRIMARY KEY(center_id,work_card_id,revision),
 FOREIGN KEY(center_id,work_card_id) REFERENCES operations.center_work_cards(center_id,work_card_id)
 ON DELETE RESTRICT
);
CREATE INDEX center_work_cards_read_idx ON operations.center_work_card_revisions
 (center_id,work_card_id,revision DESC);
CREATE TRIGGER work_card_identity_immutable BEFORE UPDATE OR DELETE
 ON operations.center_work_cards FOR EACH ROW EXECUTE FUNCTION programs.reject_draft_revision_mutation();
CREATE TRIGGER work_card_revision_immutable BEFORE UPDATE OR DELETE
 ON operations.center_work_card_revisions FOR EACH ROW EXECUTE FUNCTION programs.reject_draft_revision_mutation();
ALTER TABLE operations.center_work_cards ENABLE ROW LEVEL SECURITY;
ALTER TABLE operations.center_work_cards FORCE ROW LEVEL SECURITY;
ALTER TABLE operations.center_work_card_revisions ENABLE ROW LEVEL SECURITY;
ALTER TABLE operations.center_work_card_revisions FORCE ROW LEVEL SECURITY;
CREATE POLICY work_card_center_scope ON operations.center_work_cards
 USING (center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));
CREATE POLICY work_card_revision_center_scope ON operations.center_work_card_revisions
 USING (center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));
REVOKE ALL ON operations.center_work_cards,operations.center_work_card_revisions FROM PUBLIC;
DO $roles$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_work_card_owner') THEN
  CREATE ROLE kanoonyar_work_card_owner NOLOGIN NOINHERIT NOSUPERUSER
   NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION;
 END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_work_card_owner'
  AND NOT rolcanlogin AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb
  AND NOT rolcreaterole AND NOT rolreplication) THEN
  RAISE EXCEPTION 'Unsafe private work-card owner'; END IF;
END;$roles$;
GRANT USAGE ON SCHEMA operations,programs,centers,governance TO kanoonyar_work_card_owner;
GRANT SELECT ON programs.center_plan_draft_revisions,centers.registry,centers.memberships
 TO kanoonyar_work_card_owner;
GRANT SELECT,INSERT ON operations.center_work_cards,operations.center_work_card_revisions
 TO kanoonyar_work_card_owner;
GRANT INSERT ON governance.command_audit,governance.outbox_events TO kanoonyar_work_card_owner;
CREATE FUNCTION operations.append_center_work_card(
 p_center text,p_card uuid,p_expected bigint,p_draft uuid,p_pinned bigint,
 p_title text,p_note text,p_due timestamptz,p_actor text,p_audit uuid,p_outbox uuid,p_correlation text
) RETURNS bigint LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $fn$
DECLARE v_identity operations.center_work_cards%ROWTYPE;v_previous bigint;v_next bigint;
BEGIN
 IF p_center IS NULL OR btrim(p_center)='' OR p_card IS NULL OR p_card='00000000-0000-0000-0000-000000000000'::uuid
  OR p_expected IS NULL OR p_expected<0 OR p_draft IS NULL
  OR p_draft='00000000-0000-0000-0000-000000000000'::uuid
  OR p_pinned IS NULL OR p_pinned<1 OR p_title IS NULL
  OR length(btrim(p_title)) NOT BETWEEN 1 AND 160
  OR p_note IS NULL OR length(p_note)>1200 OR p_actor IS NULL OR btrim(p_actor)=''
  OR p_audit IS NULL OR p_outbox IS NULL OR p_correlation IS NULL
  OR btrim(p_correlation)='' THEN
  RAISE EXCEPTION 'Invalid planned work card' USING ERRCODE='22023';
 END IF;
 IF p_center IS DISTINCT FROM NULLIF(current_setting('kanoonyar.center_id',true),'')
   OR NOT EXISTS(SELECT 1 FROM centers.memberships m JOIN centers.registry c
      ON c.center_id=m.center_id WHERE m.center_id=p_center AND m.subject_id=p_actor
        AND m.is_active AND c.is_active) THEN
  RAISE EXCEPTION 'Center scope or active membership absent' USING ERRCODE='42501';
 END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('work-card:'||p_center||':'||p_card::text,0));
 IF NOT EXISTS(SELECT 1 FROM programs.center_plan_draft_revisions
    WHERE center_id=p_center AND draft_id=p_draft AND revision=p_pinned) THEN
  RAISE EXCEPTION 'Exact same-center source plan revision absent' USING ERRCODE='23503';
 END IF;
 SELECT * INTO v_identity FROM operations.center_work_cards
   WHERE center_id=p_center AND work_card_id=p_card;
 IF p_expected=0 THEN
  IF FOUND THEN RAISE EXCEPTION 'Existing work card' USING ERRCODE='40001'; END IF;
  INSERT INTO operations.center_work_cards(center_id,work_card_id,draft_id,pinned_plan_revision,created_by_subject)
   VALUES(p_center,p_card,p_draft,p_pinned,p_actor);
  v_previous:=0;
 ELSE
  IF NOT FOUND OR v_identity.draft_id<>p_draft OR v_identity.pinned_plan_revision<>p_pinned THEN
   RAISE EXCEPTION 'Immutable work-card source binding mismatch' USING ERRCODE='23503'; END IF;
  SELECT COALESCE(max(revision),0) INTO v_previous
   FROM operations.center_work_card_revisions WHERE center_id=p_center AND work_card_id=p_card;
 END IF;
 IF v_previous<>p_expected THEN
  RAISE EXCEPTION 'Stale work card version' USING ERRCODE='40001'; END IF;
 v_next:=v_previous+1;
 INSERT INTO operations.center_work_card_revisions(center_id,work_card_id,revision,title,
  planning_note,planned_due_at,changed_by_subject)
 VALUES(p_center,p_card,v_next,btrim(p_title),p_note,p_due,p_actor);
 INSERT INTO governance.command_audit(audit_id,occurred_at,actor_subject_id,center_id,
   action_code,target_type,target_id,correlation_id,metadata)
 VALUES(p_audit,transaction_timestamp(),p_actor,p_center,'CENTER_WORK_CARD_REVISION_APPENDED',
  'center_work_card',p_card::text,p_correlation,jsonb_build_object('revision',v_next,
  'draft_id',p_draft,'pinned_plan_revision',p_pinned,'visibility','PRIVATE_DRAFT'));
 INSERT INTO governance.outbox_events(event_id,audit_id,event_type,aggregate_type,
  aggregate_id,occurred_at,payload)
 VALUES(p_outbox,p_audit,'CenterWorkCardRevisionAppended','center_work_card',p_card::text,
  transaction_timestamp(),jsonb_build_object('center_id',p_center,'work_card_id',p_card,
  'revision',v_next,'draft_id',p_draft,'pinned_plan_revision',p_pinned,
  'visibility','PRIVATE_DRAFT','is_executed',false));
 RETURN v_next;
END;$fn$;
ALTER FUNCTION operations.append_center_work_card(
 text,uuid,bigint,uuid,bigint,text,text,timestamptz,text,uuid,uuid,text)
 OWNER TO kanoonyar_work_card_owner;
REVOKE ALL ON FUNCTION operations.append_center_work_card(
 text,uuid,bigint,uuid,bigint,text,text,timestamptz,text,uuid,uuid,text) FROM PUBLIC;
COMMIT;
