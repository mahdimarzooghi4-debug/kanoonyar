-- Sprint 02: transaction-scoped DB identity guard for center membership evidence.
-- This is defense in depth, not a substitute for verified JWT and trusted DB role.
BEGIN;
CREATE OR REPLACE FUNCTION centers.list_current_session_memberships()
RETURNS TABLE(center_id text, display_name text, role_code text)
LANGUAGE sql STABLE SECURITY INVOKER
SET search_path = pg_catalog, centers
AS $$
  SELECT c.center_id, c.display_name, m.role_code
  FROM centers.memberships m
  JOIN centers.registry c ON c.center_id = m.center_id
  WHERE m.subject_id = nullif(current_setting('kanoonyar.authenticated_subject', true), '')
    AND m.is_active AND c.is_active
  ORDER BY c.center_id, m.role_code;
$$;
REVOKE ALL ON FUNCTION centers.list_current_session_memberships() FROM PUBLIC;
COMMENT ON FUNCTION centers.list_current_session_memberships() IS
  'Read-only active membership evidence from transaction-local trusted subject; no permission grants. Custom GUC is spoofable by untrusted DB roles.';
COMMIT;
