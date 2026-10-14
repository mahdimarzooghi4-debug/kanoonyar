-- S52: immutable, unapproved proposals for a future human permission review.
-- This schema never grants, activates, revokes or evaluates an operation.
-- Not a delegation policy: B02/T04 approval and an independent authorizer remain absent.
BEGIN;
CREATE SCHEMA identity_access;
REVOKE ALL ON SCHEMA identity_access FROM PUBLIC;

CREATE TABLE identity_access.permission_change_review_intents (
  intent_id uuid PRIMARY KEY CHECK (intent_id <> '00000000-0000-0000-0000-000000000000'::uuid),
  center_id text NOT NULL REFERENCES centers.registry(center_id) ON DELETE RESTRICT,
  requested_by_subject text NOT NULL CHECK (length(btrim(requested_by_subject)) BETWEEN 1 AND 256),
  proposed_subject text NOT NULL CHECK (length(btrim(proposed_subject)) BETWEEN 1 AND 256),
  proposed_operation text NOT NULL CHECK (length(btrim(proposed_operation)) BETWEEN 1 AND 128),
  proposed_resource_ref text NULL CHECK (proposed_resource_ref IS NULL OR
    length(btrim(proposed_resource_ref)) BETWEEN 1 AND 256),
  stated_reason text NOT NULL CHECK (length(btrim(stated_reason)) BETWEEN 1 AND 800),
  recorded_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
  -- Explicitly non-authorizing. No SQL, workflow or API can turn this to true.
  permission_effective boolean NOT NULL DEFAULT false CHECK (permission_effective = false),
  review_approved boolean NOT NULL DEFAULT false CHECK (review_approved = false)
);
CREATE INDEX permission_review_center_cursor
 ON identity_access.permission_change_review_intents(center_id,intent_id DESC);
CREATE INDEX permission_review_subject
 ON identity_access.permission_change_review_intents(center_id,proposed_subject,intent_id DESC);
CREATE TRIGGER permission_review_append_only BEFORE UPDATE OR DELETE
 ON identity_access.permission_change_review_intents FOR EACH ROW
 EXECUTE FUNCTION programs.reject_version_or_publication_mutation();
ALTER TABLE identity_access.permission_change_review_intents ENABLE ROW LEVEL SECURITY;
ALTER TABLE identity_access.permission_change_review_intents FORCE ROW LEVEL SECURITY;
CREATE POLICY permission_review_center_scope ON identity_access.permission_change_review_intents
 USING (center_id = NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK (center_id = NULLIF(current_setting('kanoonyar.center_id',true),''));
REVOKE ALL ON identity_access.permission_change_review_intents FROM PUBLIC;

DO $role$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_permission_review_owner') THEN
  CREATE ROLE kanoonyar_permission_review_owner NOLOGIN NOINHERIT NOSUPERUSER
   NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION;
 END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_permission_review_owner'
  AND NOT rolcanlogin AND NOT rolsuper AND NOT rolbypassrls
  AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication) THEN
  RAISE EXCEPTION 'Unsafe review-only intent owner role';
 END IF;
END;$role$;
GRANT USAGE ON SCHEMA identity_access,centers,governance,programs
 TO kanoonyar_permission_review_owner;
GRANT SELECT ON centers.registry,centers.memberships
 TO kanoonyar_permission_review_owner;
GRANT SELECT,INSERT ON identity_access.permission_change_review_intents
 TO kanoonyar_permission_review_owner;
GRANT INSERT ON governance.command_audit,governance.outbox_events
 TO kanoonyar_permission_review_owner;

-- SECURITY DEFINER allows atomic proposal + audit + outbox under a restricted
-- no-login command owner. No EXECUTE privilege is granted to any runtime role.
-- p_actor is not trustworthy from arbitrary SQL callers; an eventual HTTP
-- caller MUST independently verify its OIDC subject and proposal-create right.
CREATE FUNCTION identity_access.record_permission_change_review_intent(
 p_intent uuid,p_center text,p_actor text,p_proposed_subject text,
 p_operation text,p_resource_ref text,p_reason text,
 p_audit uuid,p_event uuid,p_correlation text
) RETURNS uuid LANGUAGE plpgsql SECURITY DEFINER
 SET search_path=pg_catalog,pg_temp AS $fn$
BEGIN
 IF p_intent IS NULL OR p_intent='00000000-0000-0000-0000-000000000000'::uuid
  OR p_center IS NULL OR btrim(p_center)=''
  OR p_actor IS NULL OR length(btrim(p_actor)) NOT BETWEEN 1 AND 256
  OR p_proposed_subject IS NULL OR length(btrim(p_proposed_subject)) NOT BETWEEN 1 AND 256
  OR p_operation IS NULL OR length(btrim(p_operation)) NOT BETWEEN 1 AND 128
  OR (p_resource_ref IS NOT NULL AND length(btrim(p_resource_ref)) NOT BETWEEN 1 AND 256)
  OR p_reason IS NULL OR length(btrim(p_reason)) NOT BETWEEN 1 AND 800
  OR p_audit IS NULL OR p_event IS NULL OR p_correlation IS NULL
  OR btrim(p_correlation)='' THEN
  RAISE EXCEPTION 'Invalid non-authorizing permission review intent' USING ERRCODE='22023';
 END IF;
 IF p_center IS DISTINCT FROM NULLIF(current_setting('kanoonyar.center_id',true),'')
  OR NOT EXISTS(
   SELECT 1 FROM centers.memberships m
   JOIN centers.registry c ON c.center_id=m.center_id
   WHERE m.center_id=p_center AND m.subject_id=p_actor
    AND m.is_active AND c.is_active
  ) THEN
  RAISE EXCEPTION 'Center scope or live requesting membership absent'
   USING ERRCODE='42501';
 END IF;
 -- Unique intent UUID rejects duplicate replay atomically. No accepted request
 -- implies approval, grant, resource access, delegated role or verified policy.
 INSERT INTO identity_access.permission_change_review_intents
 (intent_id,center_id,requested_by_subject,proposed_subject,
  proposed_operation,proposed_resource_ref,stated_reason)
 VALUES (p_intent,p_center,p_actor,p_proposed_subject,
  btrim(p_operation),p_resource_ref,btrim(p_reason));

 INSERT INTO governance.command_audit
 (audit_id,occurred_at,actor_subject_id,center_id,action_code,
  target_type,target_id,correlation_id,metadata)
 VALUES(p_audit,transaction_timestamp(),p_actor,p_center,
  'PERMISSION_CHANGE_REVIEW_INTENT_RECORDED',
  'permission_change_review_intent',p_intent::text,p_correlation,
  jsonb_build_object('proposed_operation',btrim(p_operation),
   'permission_effective',false,'review_approved',false));

 INSERT INTO governance.outbox_events
 (event_id,audit_id,event_type,aggregate_type,aggregate_id,occurred_at,payload)
 VALUES(p_event,p_audit,'PermissionChangeReviewIntentRecorded',
  'permission_change_review_intent',p_intent::text,transaction_timestamp(),
  jsonb_build_object('intent_id',p_intent,'center_id',p_center,
   'permission_effective',false,'review_approved',false));
 RETURN p_intent;
END;$fn$;
ALTER FUNCTION identity_access.record_permission_change_review_intent(
 uuid,text,text,text,text,text,text,uuid,uuid,text)
 OWNER TO kanoonyar_permission_review_owner;
REVOKE ALL ON FUNCTION identity_access.record_permission_change_review_intent(
 uuid,text,text,text,text,text,text,uuid,uuid,text) FROM PUBLIC;
COMMENT ON TABLE identity_access.permission_change_review_intents IS
 'Unapproved append-only review intents only. No operational grant, approval or runtime authority is ever derived from these rows.';
COMMENT ON FUNCTION identity_access.record_permission_change_review_intent(
 uuid,text,text,text,text,text,text,uuid,uuid,text) IS
 'No production EXECUTE grant. Membership and center SQL-supplied facts are not trusted authorization; future B02 role/actor approval separately required.';
COMMIT;
