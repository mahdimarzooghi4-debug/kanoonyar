-- Sprint 02: authenticated-subject-scoped membership lookup primitive.
-- This does NOT grant any operational permission. Caller must use a trusted DB role;
-- caller-supplied subject alone is not proof of authentication.
BEGIN;
CREATE INDEX IF NOT EXISTS center_memberships_active_subject_idx
    ON centers.memberships (subject_id, center_id)
    WHERE is_active;

CREATE OR REPLACE FUNCTION centers.list_active_memberships_for_subject(p_subject_id text)
RETURNS TABLE(center_id text, display_name text, role_code text)
LANGUAGE sql STABLE SECURITY INVOKER
SET search_path = pg_catalog, centers
AS $$
    SELECT c.center_id, c.display_name, m.role_code
    FROM centers.memberships AS m
    JOIN centers.registry AS c ON c.center_id = m.center_id
    WHERE p_subject_id IS NOT NULL
      AND length(btrim(p_subject_id)) > 0
      AND m.subject_id = p_subject_id
      AND m.is_active AND c.is_active
    ORDER BY c.center_id, m.role_code;
$$;
REVOKE ALL ON FUNCTION centers.list_active_memberships_for_subject(text) FROM PUBLIC;
COMMENT ON FUNCTION centers.list_active_memberships_for_subject(text) IS
    'Subject-scoped active membership evidence only; no permission or token verification.';
COMMIT;
