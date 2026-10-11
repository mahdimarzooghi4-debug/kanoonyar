-- Versioned local execution draft revisions. No runtime grants or authorization are implied.
BEGIN;
CREATE TABLE programs.center_plan_draft_revisions (
    center_id text NOT NULL REFERENCES centers.registry(center_id) ON DELETE RESTRICT,
    draft_id uuid NOT NULL,
    revision bigint NOT NULL CHECK (revision > 0),
    program_id text NOT NULL,
    national_version integer NOT NULL,
    local_title text NOT NULL CHECK (length(btrim(local_title)) > 0),
    local_activities jsonb NOT NULL CHECK (jsonb_typeof(local_activities) = 'array'),
    actor_subject text NOT NULL CHECK (length(btrim(actor_subject)) > 0),
    recorded_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
    PRIMARY KEY (center_id, draft_id, revision),
    FOREIGN KEY (program_id, national_version)
      REFERENCES programs.national_program_publications(program_id, version) ON DELETE RESTRICT
);
CREATE INDEX center_plan_draft_revisions_center_idx
    ON programs.center_plan_draft_revisions(center_id, recorded_at DESC);
ALTER TABLE programs.center_plan_draft_revisions ENABLE ROW LEVEL SECURITY;
ALTER TABLE programs.center_plan_draft_revisions FORCE ROW LEVEL SECURITY;
CREATE POLICY center_plan_draft_scope ON programs.center_plan_draft_revisions
    USING (center_id = nullif(current_setting('kanoonyar.center_id', true), ''))
    WITH CHECK (center_id = nullif(current_setting('kanoonyar.center_id', true), ''));
CREATE FUNCTION programs.reject_draft_revision_mutation() RETURNS trigger
LANGUAGE plpgsql AS $fn$
BEGIN
  RAISE EXCEPTION 'Draft revision history is immutable' USING ERRCODE = '23514';
END;
$fn$;
CREATE TRIGGER draft_revision_immutable BEFORE UPDATE OR DELETE
ON programs.center_plan_draft_revisions FOR EACH ROW
EXECUTE FUNCTION programs.reject_draft_revision_mutation();
REVOKE ALL ON TABLE programs.center_plan_draft_revisions FROM PUBLIC;
COMMIT;
