-- A single atomic transaction for local planning state, audit, and outbox.
-- SECURITY INVOKER: the caller must already have passed verified OIDC authorization.
-- This function is not granted to an application role by this migration.
BEGIN;
CREATE FUNCTION programs.create_local_plan_with_audit(
    p_center_id text, p_plan_id uuid, p_program_id text, p_version integer,
    p_local_title text, p_activities jsonb, p_actor_subject text,
    p_audit_id uuid, p_event_id uuid, p_correlation_id text
) RETURNS void LANGUAGE plpgsql SECURITY INVOKER AS $fn$
BEGIN
    IF p_center_id IS NULL OR length(btrim(p_center_id)) = 0 OR
       p_program_id IS NULL OR length(btrim(p_program_id)) = 0 OR
       p_local_title IS NULL OR length(btrim(p_local_title)) = 0 OR
       p_actor_subject IS NULL OR length(btrim(p_actor_subject)) = 0 OR
       p_correlation_id IS NULL OR length(btrim(p_correlation_id)) = 0 OR
       p_version IS NULL OR p_version <= 0 OR
       p_activities IS NULL OR jsonb_typeof(p_activities) <> 'array' OR
       p_plan_id IS NULL OR p_audit_id IS NULL OR p_event_id IS NULL THEN
        RAISE EXCEPTION 'Invalid local plan command' USING ERRCODE = '22023';
    END IF;

    INSERT INTO programs.center_program_plans
      (center_id, plan_id, program_id, national_version, local_title,
       local_activities, created_by_subject, created_at)
    VALUES (p_center_id, p_plan_id, p_program_id, p_version, p_local_title,
            p_activities, p_actor_subject, transaction_timestamp());

    INSERT INTO governance.command_audit
      (audit_id, occurred_at, actor_subject_id, center_id, action_code,
       target_type, target_id, correlation_id)
    VALUES (p_audit_id, transaction_timestamp(), p_actor_subject, p_center_id,
            'CENTER_PROGRAM_PLAN_CREATED', 'center_program_plan', p_plan_id::text, p_correlation_id);

    INSERT INTO governance.outbox_events
      (event_id, audit_id, event_type, aggregate_type, aggregate_id, occurred_at, payload)
    VALUES (p_event_id, p_audit_id, 'CenterProgramPlanCreated', 'center_program_plan',
            p_plan_id::text, transaction_timestamp(),
            jsonb_build_object('center_id', p_center_id, 'program_id', p_program_id,
                               'national_version', p_version, 'plan_id', p_plan_id));
END;
$fn$;
-- PostgreSQL grants EXECUTE on new functions to PUBLIC by default; revoke explicitly.
REVOKE ALL ON FUNCTION programs.create_local_plan_with_audit(text,uuid,text,integer,text,jsonb,text,uuid,uuid,text) FROM PUBLIC;
COMMIT;
