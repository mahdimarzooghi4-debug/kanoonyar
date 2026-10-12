-- Sprint 08 security foundation: remove direct table-write privileges from SQL command logins.
-- No login/grant to an operational user is issued here. This does NOT authenticate a
-- SQL-supplied subject or resolve a spoofable custom GUC; runtime business grants stay DENY.
BEGIN;
DO $role$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_draft_command_owner') THEN
    CREATE ROLE kanoonyar_draft_command_owner NOLOGIN NOINHERIT NOSUPERUSER NOBYPASSRLS
      NOCREATEDB NOCREATEROLE NOREPLICATION;
  END IF;
  IF NOT EXISTS (
    SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_draft_command_owner'
      AND NOT rolcanlogin AND NOT rolsuper AND NOT rolbypassrls
      AND NOT rolcreaterole AND NOT rolcreatedb AND NOT rolreplication
  ) THEN
    RAISE EXCEPTION 'Unsafe draft command owner role';
  END IF;
END;
$role$;
GRANT USAGE ON SCHEMA programs, centers, governance TO kanoonyar_draft_command_owner;
GRANT SELECT, INSERT ON programs.center_plan_draft_revisions TO kanoonyar_draft_command_owner;
GRANT SELECT ON centers.memberships, centers.registry TO kanoonyar_draft_command_owner;
GRANT INSERT ON governance.command_audit, governance.outbox_events TO kanoonyar_draft_command_owner;
CREATE OR REPLACE FUNCTION programs.append_local_plan_draft_revision(
  p_center_id text, p_draft_id uuid, p_expected_revision bigint,
  p_program_id text, p_national_version integer, p_local_title text,
  p_activities jsonb, p_actor_subject text, p_audit_id uuid,
  p_event_id uuid, p_correlation_id text
) RETURNS bigint LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, pg_temp AS $fn$
DECLARE v_latest bigint;
BEGIN
  IF p_center_id IS NULL OR btrim(p_center_id) = '' OR p_draft_id IS NULL OR
     p_expected_revision IS NULL OR p_expected_revision < 0 OR
     p_program_id IS NULL OR btrim(p_program_id) = '' OR
     p_national_version IS NULL OR p_national_version <= 0 OR
     p_local_title IS NULL OR btrim(p_local_title) = '' OR
     p_activities IS NULL OR jsonb_typeof(p_activities) <> 'array' OR
     p_actor_subject IS NULL OR btrim(p_actor_subject) = '' OR
     p_audit_id IS NULL OR p_event_id IS NULL OR
     p_correlation_id IS NULL OR btrim(p_correlation_id) = '' THEN
    RAISE EXCEPTION 'Invalid draft revision command' USING ERRCODE = '22023';
  END IF;
  -- This scope is defense in depth, not authenticated identity. SQL credential
  -- holders can set GUCs or forge p_actor_subject: do not activate this command
  -- without an independently approved and enforceable authorization boundary.
  IF p_center_id IS DISTINCT FROM NULLIF(current_setting('kanoonyar.center_id', true), '') THEN
    RAISE EXCEPTION 'Database center scope mismatch' USING ERRCODE = '42501';
  END IF;
  IF NOT EXISTS (
    SELECT 1 FROM centers.memberships m
    JOIN centers.registry c ON c.center_id = m.center_id
    WHERE m.center_id = p_center_id AND m.subject_id = p_actor_subject
      AND m.is_active AND c.is_active
  ) THEN
    RAISE EXCEPTION 'Active center membership required' USING ERRCODE = '42501';
  END IF;
  -- Transaction-scoped lock serializes competing revisions of the same center/draft.
  PERFORM pg_advisory_xact_lock(hashtextextended(p_center_id || ':' || p_draft_id::text, 0));
  SELECT COALESCE(max(revision), 0) INTO v_latest
    FROM programs.center_plan_draft_revisions
    WHERE center_id = p_center_id AND draft_id = p_draft_id;
  IF v_latest <> p_expected_revision THEN
    RAISE EXCEPTION 'Draft revision conflict' USING ERRCODE = '40001';
  END IF;
  IF v_latest > 0 AND EXISTS (
    SELECT 1 FROM programs.center_plan_draft_revisions
    WHERE center_id = p_center_id AND draft_id = p_draft_id
      AND revision = v_latest
      AND (program_id <> p_program_id OR national_version <> p_national_version)
  ) THEN
    RAISE EXCEPTION 'National program binding cannot change' USING ERRCODE = '23514';
  END IF;
  INSERT INTO programs.center_plan_draft_revisions
    (center_id, draft_id, revision, program_id, national_version,
     local_title, local_activities, actor_subject)
  VALUES (p_center_id, p_draft_id, v_latest + 1, p_program_id,
          p_national_version, p_local_title, p_activities, p_actor_subject);
  INSERT INTO governance.command_audit
    (audit_id, occurred_at, actor_subject_id, center_id, action_code,
     target_type, target_id, correlation_id, metadata)
  VALUES (p_audit_id, transaction_timestamp(), p_actor_subject, p_center_id,
          'LOCAL_PLAN_DRAFT_REVISION_RECORDED', 'local_plan_draft',
          p_draft_id::text, p_correlation_id,
          jsonb_build_object('revision', v_latest + 1, 'national_version', p_national_version));
  INSERT INTO governance.outbox_events
    (event_id, audit_id, event_type, aggregate_type, aggregate_id, occurred_at, payload)
  VALUES (p_event_id, p_audit_id, 'LocalPlanDraftRevisionRecorded',
          'local_plan_draft', p_draft_id::text, transaction_timestamp(),
          jsonb_build_object('center_id', p_center_id, 'draft_id', p_draft_id,
                             'revision', v_latest + 1, 'program_id', p_program_id,
                             'national_version', p_national_version));
  RETURN v_latest + 1;
END;
$fn$;
ALTER FUNCTION programs.append_local_plan_draft_revision(text,uuid,bigint,text,integer,text,jsonb,text,uuid,uuid,text)
  OWNER TO kanoonyar_draft_command_owner;
REVOKE ALL ON FUNCTION programs.append_local_plan_draft_revision(text,uuid,bigint,text,integer,text,jsonb,text,uuid,uuid,text)
  FROM PUBLIC;
COMMENT ON FUNCTION programs.append_local_plan_draft_revision(text,uuid,bigint,text,integer,text,jsonb,text,uuid,uuid,text) IS
  'SECURITY DEFINER limited command owner for atomic append plus audit/outbox. Actor and center arguments are NOT non-spoofable authorization; no production callers approved.';
COMMIT;
