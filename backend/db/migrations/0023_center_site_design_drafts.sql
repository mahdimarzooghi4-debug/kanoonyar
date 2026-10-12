-- Sprint 21: private, per-center, explicitly configured design revisions.
-- No publication, authorization delegation, external assets, CSS or HTML.
BEGIN;
CREATE TABLE web.center_site_design_revisions (
 center_id text NOT NULL REFERENCES centers.registry(center_id) ON DELETE RESTRICT,
 revision bigint NOT NULL CHECK(revision>0),
 layout_key text NOT NULL CHECK(layout_key IN ('EDITORIAL','HERO','COMPACT')),
 palette_key text NOT NULL CHECK(palette_key IN ('OCEAN','FOREST','EARTH')),
 type_key text NOT NULL CHECK(type_key IN ('SYSTEM','SERIF')),
 header_text text NOT NULL CHECK(length(btrim(header_text)) BETWEEN 1 AND 120),
 footer_text text NOT NULL CHECK(length(footer_text)<=240),
 show_intro boolean NOT NULL,
 show_footer boolean NOT NULL,
 changed_by_subject text NOT NULL CHECK(length(btrim(changed_by_subject))>0),
 recorded_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
 PRIMARY KEY(center_id,revision)
);
CREATE INDEX center_site_design_recent_idx
 ON web.center_site_design_revisions(center_id,revision DESC);
CREATE TRIGGER site_design_append_only BEFORE UPDATE OR DELETE
 ON web.center_site_design_revisions FOR EACH ROW
 EXECUTE FUNCTION programs.reject_draft_revision_mutation();
REVOKE ALL ON web.center_site_design_revisions FROM PUBLIC;
ALTER TABLE web.center_site_design_revisions ENABLE ROW LEVEL SECURITY;
ALTER TABLE web.center_site_design_revisions FORCE ROW LEVEL SECURITY;
CREATE POLICY site_design_center_scope ON web.center_site_design_revisions
 USING(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));

DO $role$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_site_design_owner') THEN
  CREATE ROLE kanoonyar_site_design_owner NOLOGIN NOINHERIT NOSUPERUSER
   NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION;
 END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_site_design_owner'
   AND NOT rolcanlogin AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreaterole
   AND NOT rolcreatedb AND NOT rolreplication)
 THEN RAISE EXCEPTION 'Unsafe site design owner'; END IF;
END;$role$;
GRANT USAGE ON SCHEMA web,centers,governance TO kanoonyar_site_design_owner;
GRANT SELECT ON centers.registry,centers.memberships TO kanoonyar_site_design_owner;
GRANT SELECT,INSERT ON web.center_site_design_revisions TO kanoonyar_site_design_owner;
GRANT INSERT ON governance.command_audit,governance.outbox_events TO kanoonyar_site_design_owner;

CREATE FUNCTION web.append_center_site_design(
 p_center text,p_expected bigint,p_layout text,p_palette text,p_type text,
 p_header text,p_footer text,p_intro boolean,p_show_footer boolean,p_actor text,
 p_audit uuid,p_event uuid,p_correlation text
) RETURNS bigint LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $fn$
DECLARE v_current bigint;v_next bigint;
BEGIN
 IF p_center IS NULL OR btrim(p_center)='' OR p_expected IS NULL OR p_expected<0
  OR p_layout IS NULL OR p_layout NOT IN ('EDITORIAL','HERO','COMPACT')
  OR p_palette IS NULL OR p_palette NOT IN ('OCEAN','FOREST','EARTH')
  OR p_type IS NULL OR p_type NOT IN ('SYSTEM','SERIF')
  OR p_header IS NULL OR length(btrim(p_header)) NOT BETWEEN 1 AND 120
  OR p_footer IS NULL OR length(p_footer)>240
  OR p_intro IS NULL OR p_show_footer IS NULL
  OR p_actor IS NULL OR btrim(p_actor)=''
  OR p_audit IS NULL OR p_event IS NULL OR p_correlation IS NULL OR btrim(p_correlation)=''
 THEN RAISE EXCEPTION 'Invalid private site design' USING ERRCODE='22023'; END IF;
 IF p_center IS DISTINCT FROM NULLIF(current_setting('kanoonyar.center_id',true),'')
    OR NOT EXISTS(SELECT 1 FROM centers.memberships m JOIN centers.registry c
      ON c.center_id=m.center_id WHERE m.center_id=p_center AND m.subject_id=p_actor
      AND m.is_active AND c.is_active)
 THEN RAISE EXCEPTION 'Center scope or active membership absent' USING ERRCODE='42501'; END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('site-design:'||p_center,0));
 SELECT COALESCE(max(revision),0) INTO v_current
   FROM web.center_site_design_revisions WHERE center_id=p_center;
 IF v_current<>p_expected THEN
  RAISE EXCEPTION 'Stale site design revision' USING ERRCODE='40001'; END IF;
 v_next:=v_current+1;
 INSERT INTO web.center_site_design_revisions(center_id,revision,layout_key,palette_key,
  type_key,header_text,footer_text,show_intro,show_footer,changed_by_subject)
 VALUES(p_center,v_next,p_layout,p_palette,p_type,p_header,p_footer,
   p_intro,p_show_footer,p_actor);
 INSERT INTO governance.command_audit(audit_id,occurred_at,actor_subject_id,center_id,
  action_code,target_type,target_id,correlation_id,metadata)
 VALUES(p_audit,transaction_timestamp(),p_actor,p_center,'CENTER_SITE_DESIGN_REVISION_APPENDED',
  'center_site_design',p_center,p_correlation,jsonb_build_object(
   'revision',v_next,'layout',p_layout,'palette',p_palette,'visibility','PRIVATE_DRAFT'));
 INSERT INTO governance.outbox_events(event_id,audit_id,event_type,aggregate_type,
  aggregate_id,occurred_at,payload)
 VALUES(p_event,p_audit,'CenterSiteDesignRevisionAppended','center_site_design',
   p_center,transaction_timestamp(),jsonb_build_object('center_id',p_center,
    'revision',v_next,'visibility','PRIVATE_DRAFT','is_published',false));
 RETURN v_next;
END;$fn$;
ALTER FUNCTION web.append_center_site_design(
 text,bigint,text,text,text,text,text,boolean,boolean,text,uuid,uuid,text)
 OWNER TO kanoonyar_site_design_owner;
REVOKE ALL ON FUNCTION web.append_center_site_design(
 text,bigint,text,text,text,text,text,boolean,boolean,text,uuid,uuid,text) FROM PUBLIC;
COMMIT;
