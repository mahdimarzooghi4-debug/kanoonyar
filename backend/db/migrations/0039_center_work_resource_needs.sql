-- Sprint 39: center-private planned resource needs tied to real S33 work cards.
-- Planning is NOT stock, funding, purchase, allocation, fulfillment or delivery.
BEGIN;
CREATE TABLE operations.center_work_resource_needs (
 center_id text NOT NULL REFERENCES centers.registry(center_id) ON DELETE RESTRICT,
 need_id uuid NOT NULL CHECK(need_id<>'00000000-0000-0000-0000-000000000000'::uuid),
 work_card_id uuid NOT NULL,
 created_by_subject text NOT NULL CHECK(length(btrim(created_by_subject))>0),
 recorded_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
 PRIMARY KEY(center_id,need_id),
 FOREIGN KEY(center_id,work_card_id)
   REFERENCES operations.center_work_cards(center_id,work_card_id) ON DELETE RESTRICT
);
CREATE TABLE operations.center_work_resource_need_revisions (
 center_id text NOT NULL,
 need_id uuid NOT NULL,
 revision bigint NOT NULL CHECK(revision>0),
 resource_label text NOT NULL CHECK(length(btrim(resource_label)) BETWEEN 1 AND 160),
 planning_detail text NOT NULL CHECK(length(planning_detail)<=2000),
 desired_by timestamptz,
 changed_by_subject text NOT NULL CHECK(length(btrim(changed_by_subject))>0),
 recorded_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
 PRIMARY KEY(center_id,need_id,revision),
 FOREIGN KEY(center_id,need_id) REFERENCES operations.center_work_resource_needs(center_id,need_id)
 ON DELETE RESTRICT
);
CREATE INDEX work_resource_needs_by_card ON operations.center_work_resource_needs
 (center_id,work_card_id,need_id DESC);
CREATE TRIGGER work_resource_need_identity_immutable BEFORE UPDATE OR DELETE
 ON operations.center_work_resource_needs FOR EACH ROW
 EXECUTE FUNCTION programs.reject_draft_revision_mutation();
CREATE TRIGGER work_resource_need_revision_immutable BEFORE UPDATE OR DELETE
 ON operations.center_work_resource_need_revisions FOR EACH ROW
 EXECUTE FUNCTION programs.reject_draft_revision_mutation();
ALTER TABLE operations.center_work_resource_needs ENABLE ROW LEVEL SECURITY;
ALTER TABLE operations.center_work_resource_needs FORCE ROW LEVEL SECURITY;
ALTER TABLE operations.center_work_resource_need_revisions ENABLE ROW LEVEL SECURITY;
ALTER TABLE operations.center_work_resource_need_revisions FORCE ROW LEVEL SECURITY;
CREATE POLICY work_need_center ON operations.center_work_resource_needs
 USING(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));
CREATE POLICY work_need_revision_center ON operations.center_work_resource_need_revisions
 USING(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));
REVOKE ALL ON operations.center_work_resource_needs,
 operations.center_work_resource_need_revisions FROM PUBLIC;
DO $roles$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_work_resource_need_owner') THEN
  CREATE ROLE kanoonyar_work_resource_need_owner NOLOGIN NOINHERIT NOSUPERUSER
   NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION;
 END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_work_resource_need_owner'
  AND NOT rolcanlogin AND NOT rolsuper AND NOT rolbypassrls
  AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication) THEN
  RAISE EXCEPTION 'Unsafe resource need owner';
 END IF;
END;$roles$;
GRANT USAGE ON SCHEMA operations,programs,centers,governance TO kanoonyar_work_resource_need_owner;
GRANT SELECT ON centers.registry,centers.memberships,operations.center_work_cards,
 operations.center_work_card_revisions TO kanoonyar_work_resource_need_owner;
GRANT SELECT,INSERT ON operations.center_work_resource_needs,
 operations.center_work_resource_need_revisions TO kanoonyar_work_resource_need_owner;
GRANT INSERT ON governance.command_audit,governance.outbox_events
 TO kanoonyar_work_resource_need_owner;
CREATE FUNCTION operations.append_work_resource_need(
 p_center text,p_need uuid,p_card uuid,p_expected bigint,p_label text,p_detail text,
 p_desired timestamptz,p_actor text,p_audit uuid,p_event uuid,p_correlation text
) RETURNS bigint LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $fn$
DECLARE v_card uuid;v_last bigint;v_next bigint;
BEGIN
 IF p_center IS NULL OR btrim(p_center)='' OR p_need IS NULL
  OR p_need='00000000-0000-0000-0000-000000000000'::uuid
  OR p_card IS NULL OR p_card='00000000-0000-0000-0000-000000000000'::uuid
  OR p_expected IS NULL OR p_expected<0 OR p_label IS NULL
  OR length(btrim(p_label)) NOT BETWEEN 1 AND 160
  OR p_detail IS NULL OR length(p_detail)>2000
  OR p_actor IS NULL OR btrim(p_actor)='' OR p_audit IS NULL OR p_event IS NULL
  OR p_correlation IS NULL OR btrim(p_correlation)='' THEN
  RAISE EXCEPTION 'Invalid resource need planning' USING ERRCODE='22023';
 END IF;
 IF p_center IS DISTINCT FROM NULLIF(current_setting('kanoonyar.center_id',true),'')
  OR NOT EXISTS(SELECT 1 FROM centers.memberships m JOIN centers.registry c
   ON c.center_id=m.center_id WHERE m.center_id=p_center AND m.subject_id=p_actor
   AND m.is_active AND c.is_active) THEN
  RAISE EXCEPTION 'Center scope or active membership absent' USING ERRCODE='42501';
 END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('work-need:'||p_center||':'||p_need::text,0));
 IF NOT EXISTS(SELECT 1 FROM operations.center_work_cards c WHERE
  c.center_id=p_center AND c.work_card_id=p_card) THEN
  RAISE EXCEPTION 'Exact same-center work card absent' USING ERRCODE='23503';
 END IF;
 SELECT work_card_id INTO v_card FROM operations.center_work_resource_needs
  WHERE center_id=p_center AND need_id=p_need;
 IF p_expected=0 THEN
  IF FOUND THEN RAISE EXCEPTION 'Existing resource need' USING ERRCODE='40001'; END IF;
  INSERT INTO operations.center_work_resource_needs
   (center_id,need_id,work_card_id,created_by_subject)
   VALUES(p_center,p_need,p_card,p_actor);
  v_last:=0;
 ELSE
  IF NOT FOUND OR v_card<>p_card THEN
   RAISE EXCEPTION 'Immutable resource need work-card mismatch' USING ERRCODE='23503';
  END IF;
  SELECT COALESCE(max(revision),0) INTO v_last FROM operations.center_work_resource_need_revisions
   WHERE center_id=p_center AND need_id=p_need;
 END IF;
 IF v_last<>p_expected THEN
  RAISE EXCEPTION 'Stale resource need revision' USING ERRCODE='40001';
 END IF;
 v_next:=v_last+1;
 INSERT INTO operations.center_work_resource_need_revisions
  (center_id,need_id,revision,resource_label,planning_detail,desired_by,changed_by_subject)
 VALUES(p_center,p_need,v_next,btrim(p_label),p_detail,p_desired,p_actor);
 INSERT INTO governance.command_audit(audit_id,occurred_at,actor_subject_id,
  center_id,action_code,target_type,target_id,correlation_id,metadata)
 VALUES(p_audit,transaction_timestamp(),p_actor,p_center,
  'WORK_RESOURCE_NEED_REVISION_APPENDED','work_resource_need',p_need::text,
  p_correlation,jsonb_build_object('revision',v_next,'work_card_id',p_card,
    'visibility','PRIVATE_PLANNING','fulfillment_status','UNKNOWN'));
 INSERT INTO governance.outbox_events(event_id,audit_id,event_type,aggregate_type,
  aggregate_id,occurred_at,payload)
 VALUES(p_event,p_audit,'WorkResourceNeedRevisionAppended','work_resource_need',
  p_need::text,transaction_timestamp(),jsonb_build_object('center_id',p_center,
   'work_card_id',p_card,'need_id',p_need,'revision',v_next,
   'visibility','PRIVATE_PLANNING','is_allocated',false,'is_purchased',false,
   'is_fulfilled',false));
 RETURN v_next;
END;$fn$;
ALTER FUNCTION operations.append_work_resource_need(
 text,uuid,uuid,bigint,text,text,timestamptz,text,uuid,uuid,text)
 OWNER TO kanoonyar_work_resource_need_owner;
REVOKE ALL ON FUNCTION operations.append_work_resource_need(
 text,uuid,uuid,bigint,text,text,timestamptz,text,uuid,uuid,text) FROM PUBLIC;
COMMENT ON TABLE operations.center_work_resource_need_revisions IS
 'Human planning notes only, not a stock ledger, procurement, funding or allocated resource.';
COMMIT;
