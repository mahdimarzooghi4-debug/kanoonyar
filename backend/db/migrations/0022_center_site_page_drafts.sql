-- S20: standalone, private, immutable, center-owned website page drafts.
-- Explicitly no PUBLIC route, publish command, HTML execution or approval state.
BEGIN;
CREATE SCHEMA web;
CREATE TABLE web.center_pages (
 center_id text NOT NULL REFERENCES centers.registry(center_id) ON DELETE RESTRICT,
 page_id uuid NOT NULL,
 slug text NOT NULL CHECK(slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(slug)<=80),
 created_by_subject text NOT NULL CHECK(length(btrim(created_by_subject))>0),
 created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
 PRIMARY KEY(center_id,page_id),
 UNIQUE(center_id,slug)
);
CREATE TABLE web.page_draft_revisions (
 center_id text NOT NULL,
 page_id uuid NOT NULL,
 revision bigint NOT NULL CHECK(revision>0),
 title text NOT NULL CHECK(length(btrim(title)) BETWEEN 1 AND 200),
 body_text text NOT NULL CHECK(length(body_text)<=20000),
 changed_by_subject text NOT NULL CHECK(length(btrim(changed_by_subject))>0),
 recorded_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
 PRIMARY KEY(center_id,page_id,revision),
 FOREIGN KEY(center_id,page_id) REFERENCES web.center_pages(center_id,page_id) ON DELETE RESTRICT
);
CREATE INDEX center_page_drafts_lookup ON web.page_draft_revisions(center_id,page_id,revision DESC);
CREATE TRIGGER center_page_immutable BEFORE UPDATE OR DELETE ON web.center_pages
 FOR EACH ROW EXECUTE FUNCTION programs.reject_draft_revision_mutation();
CREATE TRIGGER page_revision_immutable BEFORE UPDATE OR DELETE ON web.page_draft_revisions
 FOR EACH ROW EXECUTE FUNCTION programs.reject_draft_revision_mutation();
ALTER TABLE web.center_pages ENABLE ROW LEVEL SECURITY;
ALTER TABLE web.center_pages FORCE ROW LEVEL SECURITY;
ALTER TABLE web.page_draft_revisions ENABLE ROW LEVEL SECURITY;
ALTER TABLE web.page_draft_revisions FORCE ROW LEVEL SECURITY;
CREATE POLICY page_scope ON web.center_pages
 USING(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));
CREATE POLICY page_draft_scope ON web.page_draft_revisions
 USING(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));
REVOKE ALL ON SCHEMA web FROM PUBLIC;
REVOKE ALL ON web.center_pages,web.page_draft_revisions FROM PUBLIC;
DO $owner$
BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_site_draft_owner') THEN
  CREATE ROLE kanoonyar_site_draft_owner NOLOGIN NOINHERIT NOSUPERUSER NOBYPASSRLS
    NOCREATEDB NOCREATEROLE NOREPLICATION;
 END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_site_draft_owner'
    AND NOT rolcanlogin AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb
    AND NOT rolcreaterole AND NOT rolreplication) THEN
   RAISE EXCEPTION 'Unsafe site page draft command owner'; END IF;
END;$owner$;
GRANT USAGE ON SCHEMA web,centers,governance TO kanoonyar_site_draft_owner;
GRANT SELECT ON centers.registry,centers.memberships TO kanoonyar_site_draft_owner;
GRANT SELECT,INSERT ON web.center_pages,web.page_draft_revisions TO kanoonyar_site_draft_owner;
GRANT INSERT ON governance.command_audit,governance.outbox_events TO kanoonyar_site_draft_owner;
CREATE FUNCTION web.append_center_page_draft(
 p_center text,p_page uuid,p_expected bigint,p_slug text,p_title text,p_body text,
 p_actor text,p_audit uuid,p_event uuid,p_correlation text
) RETURNS bigint LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $fn$
DECLARE v_previous bigint;v_slug text;v_next bigint;
BEGIN
 IF p_center IS NULL OR btrim(p_center)='' OR p_page IS NULL OR p_expected IS NULL OR p_expected<0
    OR p_slug IS NULL OR length(p_slug)>80 OR p_slug !~ '^[a-z0-9]+(-[a-z0-9]+)*$'
    OR p_title IS NULL OR length(btrim(p_title)) NOT BETWEEN 1 AND 200
    OR p_body IS NULL OR length(p_body)>20000
    OR p_actor IS NULL OR btrim(p_actor)='' OR p_audit IS NULL OR p_event IS NULL
    OR p_correlation IS NULL OR btrim(p_correlation)='' THEN
  RAISE EXCEPTION 'Invalid draft page command' USING ERRCODE='22023';
 END IF;
 IF p_center IS DISTINCT FROM NULLIF(current_setting('kanoonyar.center_id',true),'')
   OR NOT EXISTS(SELECT 1 FROM centers.memberships m JOIN centers.registry c
      ON c.center_id=m.center_id WHERE m.center_id=p_center AND m.subject_id=p_actor
         AND m.is_active AND c.is_active) THEN
  RAISE EXCEPTION 'Center scope or active membership absent' USING ERRCODE='42501';
 END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('web-page:'||p_center||':'||p_page::text,0));
 SELECT slug INTO v_slug FROM web.center_pages WHERE center_id=p_center AND page_id=p_page;
 IF p_expected=0 THEN
   IF v_slug IS NOT NULL THEN
    RAISE EXCEPTION 'Page already exists' USING ERRCODE='40001'; END IF;
   INSERT INTO web.center_pages(center_id,page_id,slug,created_by_subject)
   VALUES(p_center,p_page,p_slug,p_actor);
   v_previous:=0;
 ELSE
   IF v_slug IS NULL OR v_slug<>p_slug THEN
     RAISE EXCEPTION 'Immutable page slug does not match' USING ERRCODE='23503'; END IF;
   SELECT COALESCE(max(revision),0) INTO v_previous FROM web.page_draft_revisions
      WHERE center_id=p_center AND page_id=p_page;
 END IF;
 IF v_previous<>p_expected THEN
    RAISE EXCEPTION 'Stale page draft revision' USING ERRCODE='40001'; END IF;
 v_next:=v_previous+1;
 INSERT INTO web.page_draft_revisions(center_id,page_id,revision,title,body_text,changed_by_subject)
 VALUES(p_center,p_page,v_next,p_title,p_body,p_actor);
 INSERT INTO governance.command_audit(
  audit_id,occurred_at,actor_subject_id,center_id,action_code,target_type,
  target_id,correlation_id,metadata)
 VALUES(p_audit,transaction_timestamp(),p_actor,p_center,'CENTER_SITE_DRAFT_REVISION_APPENDED',
  'center_site_page',p_page::text,p_correlation,
  jsonb_build_object('revision',v_next,'slug',p_slug,'visibility','PRIVATE_DRAFT'));
 INSERT INTO governance.outbox_events(
  event_id,audit_id,event_type,aggregate_type,aggregate_id,occurred_at,payload)
 VALUES(p_event,p_audit,'CenterSiteDraftRevisionAppended','center_site_page',
  p_page::text,transaction_timestamp(),
  jsonb_build_object('center_id',p_center,'page_id',p_page,'revision',v_next,
    'visibility','PRIVATE_DRAFT','is_published',false));
 RETURN v_next;
END;$fn$;
ALTER FUNCTION web.append_center_page_draft(text,uuid,bigint,text,text,text,text,uuid,uuid,text)
 OWNER TO kanoonyar_site_draft_owner;
REVOKE ALL ON FUNCTION web.append_center_page_draft(text,uuid,bigint,text,text,text,text,uuid,uuid,text) FROM PUBLIC;
COMMIT;
