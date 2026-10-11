-- Durable audit trail for center and program commands.
-- This schema does not authorize any command or imply that supplied actor IDs are verified.
BEGIN;
CREATE SCHEMA IF NOT EXISTS governance;
CREATE TABLE governance.command_audit (
    audit_id uuid PRIMARY KEY,
    occurred_at timestamptz NOT NULL,
    actor_subject_id text NOT NULL CHECK (length(btrim(actor_subject_id)) > 0),
    center_id text NULL REFERENCES centers.registry(center_id) ON DELETE RESTRICT,
    action_code text NOT NULL CHECK (length(btrim(action_code)) > 0),
    target_type text NOT NULL CHECK (length(btrim(target_type)) > 0),
    target_id text NOT NULL CHECK (length(btrim(target_id)) > 0),
    correlation_id text NOT NULL CHECK (length(btrim(correlation_id)) > 0),
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb CHECK (jsonb_typeof(metadata) = 'object')
);
CREATE INDEX command_audit_center_time_idx ON governance.command_audit (center_id, occurred_at DESC);
CREATE INDEX command_audit_actor_time_idx ON governance.command_audit (actor_subject_id, occurred_at DESC);
CREATE TRIGGER command_audit_append_only
BEFORE UPDATE OR DELETE ON governance.command_audit
FOR EACH ROW EXECUTE FUNCTION programs.reject_version_or_publication_mutation();
COMMIT;
