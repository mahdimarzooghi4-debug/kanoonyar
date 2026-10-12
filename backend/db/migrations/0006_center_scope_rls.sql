-- Tenant-isolated read surfaces for center plans and memberships.
-- A database setting is only a scope filter, never proof of authenticated identity.
BEGIN;
ALTER TABLE programs.center_program_plans ENABLE ROW LEVEL SECURITY;
ALTER TABLE programs.center_program_plans FORCE ROW LEVEL SECURITY;
CREATE POLICY center_plan_scope ON programs.center_program_plans
    USING (center_id = NULLIF(current_setting('kanoonyar.center_id', true), ''))
    WITH CHECK (center_id = NULLIF(current_setting('kanoonyar.center_id', true), ''));

ALTER TABLE centers.memberships ENABLE ROW LEVEL SECURITY;
ALTER TABLE centers.memberships FORCE ROW LEVEL SECURITY;
CREATE POLICY center_membership_scope ON centers.memberships
    USING (center_id = NULLIF(current_setting('kanoonyar.center_id', true), ''))
    WITH CHECK (center_id = NULLIF(current_setting('kanoonyar.center_id', true), ''));

-- Runtime role has no BYPASSRLS and is not the owner of these tables.
-- Its actual grants, OIDC authorization, and SET LOCAL scope are separate gates.
COMMIT;
