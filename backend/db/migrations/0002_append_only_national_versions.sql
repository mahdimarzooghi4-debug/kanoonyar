-- Append-only guard for national definitions and publication evidence.
-- This migration must be applied after 0001, before any runtime writes.
BEGIN;
CREATE FUNCTION programs.reject_version_or_publication_mutation()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'National program versions and publications are append-only'
        USING ERRCODE = '23514';
END;
$$;
CREATE TRIGGER national_program_versions_append_only
BEFORE UPDATE OR DELETE ON programs.national_program_versions
FOR EACH ROW EXECUTE FUNCTION programs.reject_version_or_publication_mutation();
CREATE TRIGGER national_program_publications_append_only
BEFORE UPDATE OR DELETE ON programs.national_program_publications
FOR EACH ROW EXECUTE FUNCTION programs.reject_version_or_publication_mutation();
COMMIT;
