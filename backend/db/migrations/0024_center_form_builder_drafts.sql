-- Sprint 22: center-owned, private, versioned form DEFINITIONS only.
-- No submissions/responses, consent inference, publication, file upload or external actions.
BEGIN;
CREATE TABLE web.center_forms(
 center_id text NOT NULL REFERENCES centers.registry(center_id) ON DELETE RESTRICT,
 form_id uuid NOT NULL,
 slug text NOT NULL CHECK (slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(slug)<=80),
 created_by_subject text NOT NULL CHECK(length(btrim(created_by_subject))>0),
 created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
 PRIMARY KEY(center_id,form_id), UNIQUE(center_id,slug)
);
CREATE TABLE web.center_form_draft_revisions(
 center_id text NOT NULL,
 form_id uuid NOT NULL,
 revision bigint NOT NULL CHECK(revision>0),
 title text NOT NULL CHECK(length(btrim(title)) BETWEEN 1 AND 200),
 fields_definition jsonb NOT NULL CHECK(jsonb_typeof(fields_definition)='array'
    AND jsonb_array_length(fields_definition) BETWEEN 1 AND 12),
 changed_by_subject text NOT NULL CHECK(length(btrim(changed_by_subject))>0),
 recorded_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
 PRIMARY KEY(center_id,form_id,revision),
 FOREIGN KEY(center_id,form_id) REFERENCES web.center_forms(center_id,form_id) ON DELETE RESTRICT
);
CREATE INDEX center_form_recent_idx ON web.center_form_draft_revisions(center_id,form_id,revision DESC);
CREATE TRIGGER center_form_immutable BEFORE UPDATE OR DELETE ON web.center_forms
 FOR EACH ROW EXECUTE FUNCTION programs.reject_draft_revision_mutation();
CREATE TRIGGER center_form_revision_immutable BEFORE UPDATE OR DELETE ON web.center_form_draft_revisions
 FOR EACH ROW EXECUTE FUNCTION programs.reject_draft_revision_mutation();
REVOKE ALL ON web.center_forms,web.center_form_draft_revisions FROM PUBLIC;
ALTER TABLE web.center_forms ENABLE ROW LEVEL SECURITY;
ALTER TABLE web.center_forms FORCE ROW LEVEL SECURITY;
ALTER TABLE web.center_form_draft_revisions ENABLE ROW LEVEL SECURITY;
ALTER TABLE web.center_form_draft_revisions FORCE ROW LEVEL SECURITY;
CREATE POLICY center_form_scope ON web.center_forms
 USING(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));
CREATE POLICY center_form_revision_scope ON web.center_form_draft_revisions
 USING(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));

DO $role$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_form_draft_owner') THEN
  CREATE ROLE kanoonyar_form_draft_owner NOLOGIN NOINHERIT NOSUPERUSER NOBYPASSRLS
    NOCREATEDB NOCREATEROLE NOREPLICATION;
 END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_form_draft_owner'
   AND NOT rolcanlogin AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreaterole
   AND NOT rolcreatedb AND NOT rolreplication)
 THEN RAISE EXCEPTION 'Unsafe form definition owner'; END IF;
END;$role$;
GRANT USAGE ON SCHEMA web,centers,governance TO kanoonyar_form_draft_owner;
GRANT SELECT ON centers.registry,centers.memberships TO kanoonyar_form_draft_owner;
GRANT SELECT,INSERT ON web.center_forms,web.center_form_draft_revisions TO kanoonyar_form_draft_owner;
GRANT INSERT ON governance.command_audit,governance.outbox_events TO kanoonyar_form_draft_owner;

CREATE FUNCTION web.append_center_form_draft(
 p_center text,p_form uuid,p_expected bigint,p_slug text,p_title text,p_fields jsonb,
 p_actor text,p_audit uuid,p_event uuid,p_correlation text
) RETURNS bigint LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $fn$
DECLARE v_current bigint;v_slug text;v_revision bigint;v_field jsonb;v_key text;v_kind text;
        v_label text;v_options jsonb;v_option text;v_count bigint;v_distinct bigint;
        v_keys text[]:=ARRAY[]::text[];
BEGIN
 IF p_center IS NULL OR btrim(p_center)='' OR p_form IS NULL
  OR p_expected IS NULL OR p_expected<0
  OR p_slug IS NULL OR length(p_slug)>80 OR p_slug !~ '^[a-z0-9]+(-[a-z0-9]+)*$'
  OR p_title IS NULL OR length(btrim(p_title)) NOT BETWEEN 1 AND 200
  OR p_fields IS NULL OR jsonb_typeof(p_fields) IS DISTINCT FROM 'array'
  OR p_actor IS NULL OR btrim(p_actor)='' OR p_audit IS NULL OR p_event IS NULL
  OR p_correlation IS NULL OR btrim(p_correlation)=''
 THEN RAISE EXCEPTION 'Invalid private form draft command' USING ERRCODE='22023'; END IF;
 IF jsonb_array_length(p_fields) NOT BETWEEN 1 AND 12 THEN
  RAISE EXCEPTION 'Invalid form field count' USING ERRCODE='22023'; END IF;
 FOR v_field IN SELECT value FROM jsonb_array_elements(p_fields)
 LOOP
  IF jsonb_typeof(v_field) IS DISTINCT FROM 'object'
     OR (SELECT count(*) FROM jsonb_object_keys(v_field))<>5
     OR NOT (v_field ? 'key' AND v_field ? 'label' AND v_field ? 'kind'
        AND v_field ? 'required' AND v_field ? 'options')
     OR jsonb_typeof(v_field->'key') IS DISTINCT FROM 'string'
     OR jsonb_typeof(v_field->'label') IS DISTINCT FROM 'string'
     OR jsonb_typeof(v_field->'kind') IS DISTINCT FROM 'string'
     OR jsonb_typeof(v_field->'required') IS DISTINCT FROM 'boolean'
     OR jsonb_typeof(v_field->'options') IS DISTINCT FROM 'array'
  THEN RAISE EXCEPTION 'Invalid form field shape' USING ERRCODE='22023'; END IF;
  v_key:=v_field->>'key';v_label:=v_field->>'label';v_kind:=v_field->>'kind';
  v_options:=v_field->'options';
  IF v_key !~ '^[a-z][a-z0-9_]{0,31}$'
    OR length(btrim(v_label)) NOT BETWEEN 1 AND 120
    OR v_kind NOT IN ('SHORT_TEXT','LONG_TEXT','SINGLE_SELECT')
    OR v_key=ANY(v_keys)
  THEN RAISE EXCEPTION 'Unsupported, duplicate or unsafe form field' USING ERRCODE='22023'; END IF;
  v_keys:=array_append(v_keys,v_key);
  IF v_kind='SINGLE_SELECT' THEN
    IF jsonb_array_length(v_options) NOT BETWEEN 2 AND 8
       OR EXISTS(SELECT 1 FROM jsonb_array_elements(v_options) x
          WHERE jsonb_typeof(x.value) IS DISTINCT FROM 'string')
    THEN RAISE EXCEPTION 'Invalid choice options' USING ERRCODE='22023'; END IF;
    FOR v_option IN SELECT value FROM jsonb_array_elements_text(v_options)
    LOOP
      IF length(btrim(v_option)) NOT BETWEEN 1 AND 80 THEN
       RAISE EXCEPTION 'Invalid choice label' USING ERRCODE='22023'; END IF;
    END LOOP;
    SELECT count(*),count(DISTINCT value) INTO v_count,v_distinct
     FROM jsonb_array_elements_text(v_options);
    IF v_count<>v_distinct THEN
     RAISE EXCEPTION 'Duplicate choice options' USING ERRCODE='22023'; END IF;
  ELSIF jsonb_array_length(v_options)<>0 THEN
    RAISE EXCEPTION 'Non-choice field has options' USING ERRCODE='22023';
  END IF;
 END LOOP;
 IF p_center IS DISTINCT FROM NULLIF(current_setting('kanoonyar.center_id',true),'')
    OR NOT EXISTS(SELECT 1 FROM centers.memberships m JOIN centers.registry c
      ON c.center_id=m.center_id WHERE m.center_id=p_center AND m.subject_id=p_actor
         AND m.is_active AND c.is_active)
 THEN RAISE EXCEPTION 'Center scope or active membership absent' USING ERRCODE='42501'; END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('center-form:'||p_center||':'||p_form::text,0));
 SELECT slug INTO v_slug FROM web.center_forms
  WHERE center_id=p_center AND form_id=p_form;
 IF p_expected=0 THEN
   IF v_slug IS NOT NULL THEN RAISE EXCEPTION 'Form already exists' USING ERRCODE='40001'; END IF;
   INSERT INTO web.center_forms(center_id,form_id,slug,created_by_subject)
    VALUES(p_center,p_form,p_slug,p_actor);
   v_current:=0;
 ELSE
   IF v_slug IS NULL OR v_slug<>p_slug THEN
     RAISE EXCEPTION 'Form identity or immutable slug mismatch' USING ERRCODE='23503'; END IF;
   SELECT COALESCE(max(revision),0) INTO v_current FROM web.center_form_draft_revisions
     WHERE center_id=p_center AND form_id=p_form;
 END IF;
 IF v_current<>p_expected THEN
    RAISE EXCEPTION 'Stale form definition revision' USING ERRCODE='40001'; END IF;
 v_revision:=v_current+1;
 INSERT INTO web.center_form_draft_revisions(
  center_id,form_id,revision,title,fields_definition,changed_by_subject)
 VALUES(p_center,p_form,v_revision,p_title,p_fields,p_actor);
 INSERT INTO governance.command_audit(
  audit_id,occurred_at,actor_subject_id,center_id,action_code,target_type,target_id,
  correlation_id,metadata)
 VALUES(p_audit,transaction_timestamp(),p_actor,p_center,'CENTER_FORM_DRAFT_REVISION_APPENDED',
  'center_form_draft',p_form::text,p_correlation,
  jsonb_build_object('revision',v_revision,'slug',p_slug,'field_count',jsonb_array_length(p_fields),
   'visibility','PRIVATE_DRAFT','response_collection_enabled',false));
 INSERT INTO governance.outbox_events(
  event_id,audit_id,event_type,aggregate_type,aggregate_id,occurred_at,payload)
 VALUES(p_event,p_audit,'CenterFormDraftRevisionAppended','center_form_draft',
  p_form::text,transaction_timestamp(),
  jsonb_build_object('center_id',p_center,'form_id',p_form,'revision',v_revision,
    'visibility','PRIVATE_DRAFT','is_published',false,'accepts_submissions',false));
 RETURN v_revision;
END;$fn$;
ALTER FUNCTION web.append_center_form_draft(text,uuid,bigint,text,text,jsonb,text,uuid,uuid,text)
 OWNER TO kanoonyar_form_draft_owner;
REVOKE ALL ON FUNCTION web.append_center_form_draft(text,uuid,bigint,text,text,jsonb,text,uuid,uuid,text) FROM PUBLIC;
COMMENT ON TABLE web.center_form_draft_revisions IS 'Private schema definitions only. No response collection or consent authority.';
COMMIT;
