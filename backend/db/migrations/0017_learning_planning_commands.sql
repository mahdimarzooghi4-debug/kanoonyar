-- Sprint 13: technical class/session creation commands; NO occurrence or attendance.
-- The DB role protects audit completeness, not actor authenticity or business grants.
BEGIN;
DO $role$
BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_learning_command_owner') THEN
   CREATE ROLE kanoonyar_learning_command_owner NOLOGIN NOINHERIT NOSUPERUSER
     NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION;
 END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_learning_command_owner'
   AND NOT rolcanlogin AND NOT rolsuper AND NOT rolbypassrls
   AND NOT rolcreaterole AND NOT rolcreatedb AND NOT rolreplication) THEN
   RAISE EXCEPTION 'Unsafe learning command owner';
 END IF;
END;$role$;
GRANT USAGE ON SCHEMA learning,centers,governance TO kanoonyar_learning_command_owner;
GRANT SELECT ON centers.registry,centers.memberships TO kanoonyar_learning_command_owner;
GRANT SELECT,INSERT ON learning.center_classes TO kanoonyar_learning_command_owner;
GRANT INSERT ON learning.class_sessions TO kanoonyar_learning_command_owner;
GRANT INSERT ON governance.command_audit,governance.outbox_events TO kanoonyar_learning_command_owner;

CREATE FUNCTION learning.record_class_plan(
 p_center text,p_class uuid,p_title text,p_actor text,
 p_audit uuid,p_event uuid,p_correlation text
) RETURNS uuid LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $fn$
BEGIN
 IF p_center IS NULL OR btrim(p_center)='' OR p_class IS NULL
    OR p_title IS NULL OR btrim(p_title)='' OR
    p_actor IS NULL OR btrim(p_actor)='' OR p_audit IS NULL OR p_event IS NULL OR
    p_correlation IS NULL OR btrim(p_correlation)='' THEN
   RAISE EXCEPTION 'Invalid class plan request' USING ERRCODE='22023';
 END IF;
 IF p_center IS DISTINCT FROM NULLIF(current_setting('kanoonyar.center_id',true),'') OR
    NOT EXISTS(SELECT 1 FROM centers.memberships m
     JOIN centers.registry c ON c.center_id=m.center_id
     WHERE m.center_id=p_center AND m.subject_id=p_actor AND m.is_active AND c.is_active) THEN
   RAISE EXCEPTION 'Center scope or active membership absent' USING ERRCODE='42501';
 END IF;
 INSERT INTO learning.center_classes(center_id,class_id,title)
 VALUES(p_center,p_class,p_title);
 INSERT INTO governance.command_audit(audit_id,occurred_at,actor_subject_id,center_id,
   action_code,target_type,target_id,correlation_id,metadata)
 VALUES(p_audit,transaction_timestamp(),p_actor,p_center,'CLASS_PLAN_RECORDED',
  'class_plan',p_class::text,p_correlation,jsonb_build_object('class_id',p_class));
 INSERT INTO governance.outbox_events(event_id,audit_id,event_type,aggregate_type,
   aggregate_id,occurred_at,payload)
 VALUES(p_event,p_audit,'ClassPlanRecorded','class_plan',p_class::text,
   transaction_timestamp(),jsonb_build_object('class_id',p_class,'center_id',p_center,
   'delivery_verified',false));
 RETURN p_class;
END;$fn$;
ALTER FUNCTION learning.record_class_plan(text,uuid,text,text,uuid,uuid,text)
 OWNER TO kanoonyar_learning_command_owner;
REVOKE ALL ON FUNCTION learning.record_class_plan(text,uuid,text,text,uuid,uuid,text) FROM PUBLIC;

CREATE FUNCTION learning.record_class_session_plan(
 p_center text,p_session uuid,p_class uuid,p_title text,
 p_scheduled timestamptz,p_actor text,p_audit uuid,p_event uuid,p_correlation text
) RETURNS uuid LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $fn$
BEGIN
 IF p_center IS NULL OR btrim(p_center)='' OR p_session IS NULL OR p_class IS NULL OR
    p_title IS NULL OR btrim(p_title)='' OR p_actor IS NULL OR btrim(p_actor)='' OR
    p_audit IS NULL OR p_event IS NULL OR
    p_correlation IS NULL OR btrim(p_correlation)='' THEN
  RAISE EXCEPTION 'Invalid session plan request' USING ERRCODE='22023';
 END IF;
 IF p_center IS DISTINCT FROM NULLIF(current_setting('kanoonyar.center_id',true),'') OR
    NOT EXISTS(SELECT 1 FROM centers.memberships m JOIN centers.registry c
      ON c.center_id=m.center_id
      WHERE m.center_id=p_center AND m.subject_id=p_actor AND m.is_active AND c.is_active) THEN
   RAISE EXCEPTION 'Center scope or active membership absent' USING ERRCODE='42501';
 END IF;
 INSERT INTO learning.class_sessions(center_id,session_id,class_id,session_title,scheduled_at)
 VALUES(p_center,p_session,p_class,p_title,p_scheduled);
 INSERT INTO governance.command_audit(audit_id,occurred_at,actor_subject_id,center_id,
   action_code,target_type,target_id,correlation_id,metadata)
 VALUES(p_audit,transaction_timestamp(),p_actor,p_center,'CLASS_SESSION_PLAN_RECORDED',
  'class_session_plan',p_session::text,p_correlation,
  jsonb_build_object('class_id',p_class,'scheduled_at',p_scheduled));
 INSERT INTO governance.outbox_events(event_id,audit_id,event_type,aggregate_type,
   aggregate_id,occurred_at,payload)
 VALUES(p_event,p_audit,'ClassSessionPlanRecorded','class_session_plan',p_session::text,
   transaction_timestamp(),jsonb_build_object('center_id',p_center,
   'session_id',p_session,'class_id',p_class,'scheduled_at',p_scheduled,
   'delivery_verified',false));
 RETURN p_session;
END;$fn$;
ALTER FUNCTION learning.record_class_session_plan(text,uuid,uuid,text,timestamptz,text,uuid,uuid,text)
 OWNER TO kanoonyar_learning_command_owner;
REVOKE ALL ON FUNCTION learning.record_class_session_plan(text,uuid,uuid,text,timestamptz,text,uuid,uuid,text) FROM PUBLIC;
COMMIT;
