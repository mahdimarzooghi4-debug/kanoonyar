-- Center identity foundation: explicit center registry and membership evidence.
-- These rows are NOT authentication credentials or permission grants.
BEGIN;
CREATE SCHEMA IF NOT EXISTS centers;
CREATE TABLE centers.registry (
    center_id text PRIMARY KEY CHECK (length(btrim(center_id)) > 0),
    display_name text NOT NULL CHECK (length(btrim(display_name)) > 0),
    is_active boolean NOT NULL DEFAULT false,
    registered_by_subject text NOT NULL CHECK (length(btrim(registered_by_subject)) > 0),
    registered_at timestamptz NOT NULL
);
CREATE TABLE centers.memberships (
    center_id text NOT NULL REFERENCES centers.registry(center_id) ON DELETE RESTRICT,
    subject_id text NOT NULL CHECK (length(btrim(subject_id)) > 0),
    role_code text NOT NULL CHECK (length(btrim(role_code)) > 0),
    is_active boolean NOT NULL DEFAULT false,
    recorded_by_subject text NOT NULL CHECK (length(btrim(recorded_by_subject)) > 0),
    recorded_at timestamptz NOT NULL,
    PRIMARY KEY (center_id, subject_id, role_code)
);
CREATE INDEX center_memberships_subject_idx ON centers.memberships (subject_id, center_id);
ALTER TABLE programs.center_program_plans
    ADD CONSTRAINT center_program_plans_center_fk
    FOREIGN KEY (center_id) REFERENCES centers.registry(center_id) ON DELETE RESTRICT;
COMMIT;
