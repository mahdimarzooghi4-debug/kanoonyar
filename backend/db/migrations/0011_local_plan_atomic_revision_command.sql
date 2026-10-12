-- Trusted command adapter only: no PUBLIC EXECUTE or runtime grants.
-- Authorization must be established outside this function using a non-spoofable identity boundary.
BEGIN;
CREATE FUNCTION programs.append_local_plan_draft_revision(
  p_center_id text, p_draft_id uuid, p_expected_revision bigint,
  p_program_id text, p_national_version integer, p_local_title text,
  p_activities jsonb, p_actor_subject text, p_audit_id uuid,
  p_event_id uuid, p_correlation_id text
) RETURNS bigint LANGUAGE plpgsql SECURITY INVOKER AS $fn$
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
REVOKE ALL ON FUNCTION programs.append_local_plan_draft_revision(text,uuid,bigint,text,integer,text,jsonb,text,uuid,uuid,text) FROM PUBLIC;
COMMIT;
