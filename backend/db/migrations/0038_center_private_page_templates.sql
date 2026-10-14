-- Sprint 38: reusable *private* page templates pinned to genuine same-center page revisions.
-- Never global templates, public publication, content license, or cross-center reuse.
BEGIN;
CREATE TABLE web.center_private_page_templates (
 center_id text NOT NULL REFERENCES centers.registry(center_id) ON DELETE RESTRICT,
 template_id uuid NOT NULL CHECK(template_id<>'00000000-0000-0000-0000-000000000000'::uuid),
 source_page_id uuid NOT NULL,
 created_by_subject text NOT NULL CHECK(length(btrim(created_by_subject))>0),
 recorded_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
 PRIMARY KEY(center_id,template_id),
 FOREIGN KEY(center_id,source_page_id) REFERENCES web.center_pages(center_id,page_id)
  ON DELETE RESTRICT
);
CREATE TABLE web.center_private_page_template_revisions (
 center_id text NOT NULL,
 template_id uuid NOT NULL,
 revision bigint NOT NULL CHECK(revision>0),
 pinned_page_revision bigint NOT NULL CHECK(pinned_page_revision>0),
 template_label text NOT NULL CHECK(length(btrim(template_label)) BETWEEN 1 AND 160),
 recorded_by_subject text NOT NULL CHECK(length(btrim(recorded_by_subject))>0),
 recorded_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
 PRIMARY KEY(center_id,template_id,revision),
 FOREIGN KEY(center_id,template_id)
  REFERENCES web.center_private_page_templates(center_id,template_id) ON DELETE RESTRICT
);
CREATE INDEX private_page_template_recent
 ON web.center_private_page_template_revisions(center_id,template_id,revision DESC);
-- Preserve exact source owner identity AND immutable source revision at database level.
ALTER TABLE web.center_private_page_template_revisions
 ADD COLUMN source_page_id uuid NOT NULL;
ALTER TABLE web.center_private_page_template_revisions
 ADD CONSTRAINT private_page_template_pinned_source
 FOREIGN KEY(center_id,source_page_id,pinned_page_revision)
 REFERENCES web.page_draft_revisions(center_id,page_id,revision) ON DELETE RESTRICT;
ALTER TABLE web.center_private_page_templates
 ADD CONSTRAINT private_page_template_identity_unique UNIQUE(center_id,template_id,source_page_id);
ALTER TABLE web.center_private_page_template_revisions
 ADD CONSTRAINT private_page_template_exact_identity
 FOREIGN KEY(center_id,template_id,source_page_id)
 REFERENCES web.center_private_page_templates(center_id,template_id,source_page_id)
 ON DELETE RESTRICT;
CREATE TRIGGER private_page_template_immutable BEFORE UPDATE OR DELETE
 ON web.center_private_page_templates FOR EACH ROW
 EXECUTE FUNCTION programs.reject_draft_revision_mutation();
CREATE TRIGGER private_page_template_revision_immutable BEFORE UPDATE OR DELETE
 ON web.center_private_page_template_revisions FOR EACH ROW
 EXECUTE FUNCTION programs.reject_draft_revision_mutation();
ALTER TABLE web.center_private_page_templates ENABLE ROW LEVEL SECURITY;
ALTER TABLE web.center_private_page_templates FORCE ROW LEVEL SECURITY;
ALTER TABLE web.center_private_page_template_revisions ENABLE ROW LEVEL SECURITY;
ALTER TABLE web.center_private_page_template_revisions FORCE ROW LEVEL SECURITY;
CREATE POLICY private_template_center_scope ON web.center_private_page_templates
 USING(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));
CREATE POLICY private_template_revision_scope ON web.center_private_page_template_revisions
 USING(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));
REVOKE ALL ON web.center_private_page_templates,
 web.center_private_page_template_revisions FROM PUBLIC;
DO $owner$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_private_template_owner') THEN
  CREATE ROLE kanoonyar_private_template_owner NOLOGIN NOINHERIT NOSUPERUSER
   NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION;
 END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_private_template_owner'
  AND NOT rolcanlogin AND NOT rolsuper AND NOT rolbypassrls
  AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication) THEN
  RAISE EXCEPTION 'Unsafe template command owner';
 END IF;
END;$owner$;
GRANT USAGE ON SCHEMA web,centers,governance TO kanoonyar_private_template_owner;
GRANT SELECT ON centers.memberships,centers.registry,web.center_pages,
 web.page_draft_revisions TO kanoonyar_private_template_owner;
GRANT SELECT,INSERT ON web.center_private_page_templates,
 web.center_private_page_template_revisions TO kanoonyar_private_template_owner;
GRANT INSERT ON governance.command_audit,governance.outbox_events
 TO kanoonyar_private_template_owner;
CREATE FUNCTION web.append_center_private_page_template(
 p_center text,p_template uuid,p_source_page uuid,p_expected bigint,
 p_pinned_revision bigint,p_label text,p_actor text,p_audit uuid,p_event uuid,p_correlation text
) RETURNS bigint LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $fn$
DECLARE v_source uuid;v_last bigint;v_next bigint;
BEGIN
 IF p_center IS NULL OR btrim(p_center)='' OR p_template IS NULL
  OR p_template='00000000-0000-0000-0000-000000000000'::uuid
  OR p_source_page IS NULL
  OR p_source_page='00000000-0000-0000-0000-000000000000'::uuid
  OR p_expected IS NULL OR p_expected<0 OR p_pinned_revision IS NULL
  OR p_pinned_revision<=0 OR p_label IS NULL
  OR length(btrim(p_label)) NOT BETWEEN 1 AND 160
  OR p_actor IS NULL OR btrim(p_actor)='' OR p_audit IS NULL OR p_event IS NULL
  OR p_correlation IS NULL OR btrim(p_correlation)='' THEN
  RAISE EXCEPTION 'Invalid private template command' USING ERRCODE='22023';
 END IF;
 IF p_center IS DISTINCT FROM NULLIF(current_setting('kanoonyar.center_id',true),'')
  OR NOT EXISTS(SELECT 1 FROM centers.memberships m JOIN centers.registry c
   ON c.center_id=m.center_id WHERE m.center_id=p_center
    AND m.subject_id=p_actor AND m.is_active AND c.is_active) THEN
  RAISE EXCEPTION 'Center scope or active membership absent' USING ERRCODE='42501';
 END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended(
  'web-template:'||p_center||':'||p_template::text,0));
 IF NOT EXISTS(SELECT 1 FROM web.page_draft_revisions d
  WHERE d.center_id=p_center AND d.page_id=p_source_page
   AND d.revision=p_pinned_revision) THEN
  RAISE EXCEPTION 'Exact same-center source revision absent' USING ERRCODE='23503';
 END IF;
 SELECT source_page_id INTO v_source FROM web.center_private_page_templates
  WHERE center_id=p_center AND template_id=p_template;
 IF p_expected=0 THEN
  IF FOUND THEN
   RAISE EXCEPTION 'Private template already exists' USING ERRCODE='40001';
  END IF;
  INSERT INTO web.center_private_page_templates
   (center_id,template_id,source_page_id,created_by_subject)
  VALUES(p_center,p_template,p_source_page,p_actor);
  v_last:=0;
 ELSE
  IF NOT FOUND OR v_source<>p_source_page THEN
   RAISE EXCEPTION 'Immutable template source mismatch' USING ERRCODE='23503';
  END IF;
  SELECT COALESCE(max(revision),0) INTO v_last
   FROM web.center_private_page_template_revisions
   WHERE center_id=p_center AND template_id=p_template;
 END IF;
 IF v_last<>p_expected THEN
  RAISE EXCEPTION 'Stale template revision' USING ERRCODE='40001';
 END IF;
 v_next:=v_last+1;
 INSERT INTO web.center_private_page_template_revisions
  (center_id,template_id,source_page_id,revision,pinned_page_revision,
   template_label,recorded_by_subject)
 VALUES(p_center,p_template,p_source_page,v_next,p_pinned_revision,
  btrim(p_label),p_actor);
 INSERT INTO governance.command_audit(audit_id,occurred_at,actor_subject_id,
  center_id,action_code,target_type,target_id,correlation_id,metadata)
 VALUES(p_audit,transaction_timestamp(),p_actor,p_center,
  'PRIVATE_PAGE_TEMPLATE_REVISION_APPENDED','private_page_template',p_template::text,
  p_correlation,jsonb_build_object('revision',v_next,'source_page_id',p_source_page,
  'pinned_page_revision',p_pinned_revision,'visibility','PRIVATE_TEMPLATE'));
 INSERT INTO governance.outbox_events(event_id,audit_id,event_type,aggregate_type,
  aggregate_id,occurred_at,payload)
 VALUES(p_event,p_audit,'PrivatePageTemplateRevisionAppended','private_page_template',
  p_template::text,transaction_timestamp(),jsonb_build_object('center_id',p_center,
  'template_id',p_template,'revision',v_next,'pinned_page_revision',p_pinned_revision,
  'visibility','PRIVATE_TEMPLATE','is_published',false));
 RETURN v_next;
END;$fn$;
ALTER FUNCTION web.append_center_private_page_template(
 text,uuid,uuid,bigint,bigint,text,text,uuid,uuid,text)
 OWNER TO kanoonyar_private_template_owner;
REVOKE ALL ON FUNCTION web.append_center_private_page_template(
 text,uuid,uuid,bigint,bigint,text,text,uuid,uuid,text) FROM PUBLIC;
COMMENT ON TABLE web.center_private_page_template_revisions IS
 'Exact same-center pinned private page drafts for optional explicit local reuse; never public or shared.';
COMMIT;
