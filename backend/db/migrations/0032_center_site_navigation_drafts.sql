-- Sprint 32: private, append-only site navigation, linking exact local page revisions.
-- No publication, public routing, external URLs, implicit grants or approval.
BEGIN;
CREATE TABLE web.center_site_navigation_revisions (
 center_id text NOT NULL REFERENCES centers.registry(center_id) ON DELETE RESTRICT,
 revision bigint NOT NULL CHECK (revision > 0),
 created_by_subject text NOT NULL CHECK (length(btrim(created_by_subject))>0),
 recorded_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
 PRIMARY KEY (center_id,revision)
);
CREATE TABLE web.center_site_navigation_items (
 center_id text NOT NULL,
 navigation_revision bigint NOT NULL,
 position integer NOT NULL CHECK (position > 0),
 page_id uuid NOT NULL,
 pinned_page_revision bigint NOT NULL CHECK (pinned_page_revision > 0),
 PRIMARY KEY (center_id,navigation_revision,position),
 UNIQUE (center_id,navigation_revision,page_id),
 FOREIGN KEY (center_id,navigation_revision)
  REFERENCES web.center_site_navigation_revisions(center_id,revision) ON DELETE RESTRICT,
 FOREIGN KEY (center_id,page_id,pinned_page_revision)
  REFERENCES web.page_draft_revisions(center_id,page_id,revision) ON DELETE RESTRICT
);
CREATE TRIGGER center_site_nav_revision_immutable BEFORE UPDATE OR DELETE
 ON web.center_site_navigation_revisions FOR EACH ROW
 EXECUTE FUNCTION programs.reject_draft_revision_mutation();
CREATE TRIGGER center_site_nav_item_immutable BEFORE UPDATE OR DELETE
 ON web.center_site_navigation_items FOR EACH ROW
 EXECUTE FUNCTION programs.reject_draft_revision_mutation();
ALTER TABLE web.center_site_navigation_revisions ENABLE ROW LEVEL SECURITY;
ALTER TABLE web.center_site_navigation_revisions FORCE ROW LEVEL SECURITY;
ALTER TABLE web.center_site_navigation_items ENABLE ROW LEVEL SECURITY;
ALTER TABLE web.center_site_navigation_items FORCE ROW LEVEL SECURITY;
CREATE POLICY site_nav_revision_center_scope ON web.center_site_navigation_revisions
 USING(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));
CREATE POLICY site_nav_item_center_scope ON web.center_site_navigation_items
 USING(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));
REVOKE ALL ON web.center_site_navigation_revisions,web.center_site_navigation_items FROM PUBLIC;
DO $owner$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_site_navigation_owner') THEN
  CREATE ROLE kanoonyar_site_navigation_owner NOLOGIN NOINHERIT NOSUPERUSER
   NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION;
 END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_site_navigation_owner'
  AND NOT rolcanlogin AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb
  AND NOT rolcreaterole AND NOT rolreplication) THEN
  RAISE EXCEPTION 'Unsafe site navigation command owner';
 END IF;
END;$owner$;
GRANT USAGE ON SCHEMA web,centers,governance TO kanoonyar_site_navigation_owner;
GRANT SELECT ON centers.registry,centers.memberships,web.center_pages,
 web.page_draft_revisions TO kanoonyar_site_navigation_owner;
GRANT SELECT,INSERT ON web.center_site_navigation_revisions,
 web.center_site_navigation_items TO kanoonyar_site_navigation_owner;
GRANT INSERT ON governance.command_audit,governance.outbox_events TO kanoonyar_site_navigation_owner;
CREATE FUNCTION web.append_center_site_navigation(
 p_center text,p_expected bigint,p_page_ids uuid[],p_page_revisions bigint[],
 p_actor text,p_audit uuid,p_outbox uuid,p_correlation text
) RETURNS bigint LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $fn$
DECLARE n integer; v_index integer; v_current bigint;v_next bigint;
BEGIN
 IF p_center IS NULL OR btrim(p_center)='' OR p_expected IS NULL OR p_expected<0
  OR p_page_ids IS NULL OR p_page_revisions IS NULL
  OR cardinality(p_page_ids)<>cardinality(p_page_revisions)
  OR (cardinality(p_page_ids)>0 AND (array_ndims(p_page_ids)<>1
    OR array_ndims(p_page_revisions)<>1
    OR array_lower(p_page_ids,1)<>1 OR array_lower(p_page_revisions,1)<>1))
  OR p_actor IS NULL OR btrim(p_actor)=''
  OR p_audit IS NULL OR p_outbox IS NULL OR p_correlation IS NULL
  OR btrim(p_correlation)='' THEN
   RAISE EXCEPTION 'Invalid private site navigation' USING ERRCODE='22023';
 END IF;
 n:=cardinality(p_page_ids);
 IF array_position(p_page_ids,NULL) IS NOT NULL
    OR array_position(p_page_revisions,NULL) IS NOT NULL
    OR (SELECT count(DISTINCT x) FROM unnest(p_page_ids) AS x)<>n THEN
   RAISE EXCEPTION 'Duplicate or empty navigation page identity' USING ERRCODE='22023';
 END IF;
 IF p_center IS DISTINCT FROM NULLIF(current_setting('kanoonyar.center_id',true),'')
   OR NOT EXISTS(SELECT 1 FROM centers.memberships m JOIN centers.registry c
    ON c.center_id=m.center_id WHERE m.center_id=p_center
     AND m.subject_id=p_actor AND m.is_active AND c.is_active) THEN
   RAISE EXCEPTION 'Center scope or active membership absent' USING ERRCODE='42501';
 END IF;
 -- Empty array means an explicitly saved empty private navigation, not an inferred default.
 FOR v_index IN 1..n LOOP
  IF p_page_ids[v_index]='00000000-0000-0000-0000-000000000000'::uuid
    OR p_page_revisions[v_index]<1 OR NOT EXISTS(
      SELECT 1 FROM web.page_draft_revisions d
       WHERE d.center_id=p_center AND d.page_id=p_page_ids[v_index]
         AND d.revision=p_page_revisions[v_index])
  THEN RAISE EXCEPTION 'Navigation must pin an actual same-center page revision'
       USING ERRCODE='23503'; END IF;
 END LOOP;
 PERFORM pg_advisory_xact_lock(hashtextextended('site-navigation:'||p_center,0));
 SELECT COALESCE(max(revision),0) INTO v_current
  FROM web.center_site_navigation_revisions WHERE center_id=p_center;
 IF v_current<>p_expected THEN
  RAISE EXCEPTION 'Stale private site navigation revision' USING ERRCODE='40001';
 END IF;
 v_next:=v_current+1;
 INSERT INTO web.center_site_navigation_revisions(center_id,revision,created_by_subject)
  VALUES(p_center,v_next,p_actor);
 FOR v_index IN 1..n LOOP
  INSERT INTO web.center_site_navigation_items(center_id,navigation_revision,
   position,page_id,pinned_page_revision)
   VALUES(p_center,v_next,v_index,p_page_ids[v_index],p_page_revisions[v_index]);
 END LOOP;
 INSERT INTO governance.command_audit(audit_id,occurred_at,actor_subject_id,
   center_id,action_code,target_type,target_id,correlation_id,metadata)
 VALUES(p_audit,transaction_timestamp(),p_actor,p_center,
  'CENTER_SITE_NAVIGATION_REVISION_APPENDED','center_site_navigation',p_center,
  p_correlation,jsonb_build_object('revision',v_next,'linked_pages',n,
   'visibility','PRIVATE_DRAFT'));
 INSERT INTO governance.outbox_events(event_id,audit_id,event_type,aggregate_type,
   aggregate_id,occurred_at,payload)
 VALUES(p_outbox,p_audit,'CenterSiteNavigationRevisionAppended','center_site_navigation',
  p_center,transaction_timestamp(),jsonb_build_object('center_id',p_center,
   'revision',v_next,'linked_pages',n,'is_published',false));
 RETURN v_next;
END;$fn$;
ALTER FUNCTION web.append_center_site_navigation(text,bigint,uuid[],bigint[],text,uuid,uuid,text)
 OWNER TO kanoonyar_site_navigation_owner;
REVOKE ALL ON FUNCTION web.append_center_site_navigation(text,bigint,uuid[],bigint[],text,uuid,uuid,text)
 FROM PUBLIC;
COMMENT ON TABLE web.center_site_navigation_items IS
 'Source-pinned, immutable ordered references to same-center private site page revisions; not public links.';
COMMIT;
