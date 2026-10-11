-- KanoonYar national programs: initial PostgreSQL persistence schema.
-- DDL only. Applying migrations and exposing write APIs require separate operational gates.
BEGIN;
CREATE SCHEMA IF NOT EXISTS programs;

CREATE TABLE programs.national_program_versions (
    program_id text NOT NULL CHECK (length(btrim(program_id)) > 0),
    version integer NOT NULL CHECK (version > 0),
    title text NOT NULL CHECK (length(btrim(title)) > 0),
    national_objectives jsonb NOT NULL CHECK (jsonb_typeof(national_objectives) = 'array'),
    mandatory_requirements jsonb NOT NULL CHECK (jsonb_typeof(mandatory_requirements) = 'array'),
    created_by_subject text NOT NULL CHECK (length(btrim(created_by_subject)) > 0),
    created_at timestamptz NOT NULL,
    PRIMARY KEY (program_id, version)
);

CREATE TABLE programs.national_program_publications (
    program_id text NOT NULL,
    version integer NOT NULL,
    publisher_subject text NOT NULL CHECK (length(btrim(publisher_subject)) > 0),
    published_at timestamptz NOT NULL,
    PRIMARY KEY (program_id, version),
    FOREIGN KEY (program_id, version)
        REFERENCES programs.national_program_versions (program_id, version) ON DELETE RESTRICT
);

CREATE TABLE programs.center_program_plans (
    center_id text NOT NULL CHECK (length(btrim(center_id)) > 0),
    plan_id uuid NOT NULL,
    program_id text NOT NULL,
    national_version integer NOT NULL,
    local_title text NOT NULL CHECK (length(btrim(local_title)) > 0),
    local_activities jsonb NOT NULL CHECK (jsonb_typeof(local_activities) = 'array'),
    created_by_subject text NOT NULL CHECK (length(btrim(created_by_subject)) > 0),
    created_at timestamptz NOT NULL,
    revision bigint NOT NULL DEFAULT 1 CHECK (revision > 0),
    PRIMARY KEY (center_id, plan_id),
    FOREIGN KEY (program_id, national_version)
        REFERENCES programs.national_program_publications (program_id, version) ON DELETE RESTRICT
);
CREATE INDEX center_program_plans_national_version_idx
    ON programs.center_program_plans (program_id, national_version);

-- These tables are never writable directly by the application runtime role.
-- The future command layer must use an explicit privileged transactional path,
-- authorized human identity, audit/outbox, and center isolation policy.
-- DDL alone cannot establish the above runtime guarantees.
COMMIT;
