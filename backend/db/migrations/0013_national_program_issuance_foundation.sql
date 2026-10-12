-- Sprint 09: evidence that one published national program VERSION was issued
-- to one explicitly named center. No authorization, publication, receipt, approval,
-- delivery, compliance, or local execution state is created by this migration.
-- The issuer subject is an UNVERIFIED argument until a future trusted command path.
BEGIN;
CREATE TABLE programs.national_program_issuances (
    issuance_id uuid PRIMARY KEY,
    center_id text NOT NULL REFERENCES centers.registry(center_id) ON DELETE RESTRICT,
    program_id text NOT NULL CHECK (length(btrim(program_id)) > 0),
    national_version integer NOT NULL CHECK (national_version > 0),
    issued_by_subject text NOT NULL CHECK (length(btrim(issued_by_subject)) > 0),
    issued_at timestamptz NOT NULL,
    CONSTRAINT national_issuance_exact_publication_fk
      FOREIGN KEY (program_id, national_version)
      REFERENCES programs.national_program_publications(program_id, version) ON DELETE RESTRICT,
    CONSTRAINT national_issuance_center_exact_version_unique
      UNIQUE(center_id, program_id, national_version)
);
CREATE INDEX national_program_issuances_version_idx
  ON programs.national_program_issuances(program_id, national_version, center_id);
CREATE TRIGGER national_program_issuances_append_only
  BEFORE UPDATE OR DELETE ON programs.national_program_issuances
  FOR EACH ROW EXECUTE FUNCTION programs.reject_version_or_publication_mutation();
REVOKE ALL ON TABLE programs.national_program_issuances FROM PUBLIC;

-- No policy: every non-bypass role, even with accidentally granted SELECT/INSERT,
-- is denied until an explicit, independently approved access design is deployed.
ALTER TABLE programs.national_program_issuances ENABLE ROW LEVEL SECURITY;
ALTER TABLE programs.national_program_issuances FORCE ROW LEVEL SECURITY;

-- Atomically record immutable issuance plus audit/outbox. The invoker must already
-- have an independently verified human HQ operation grant; NO runtime EXECUTE granted.
CREATE FUNCTION programs.record_national_program_issuance(
    p_issuance_id uuid, p_center_id text, p_program_id text,
    p_national_version integer, p_issuer_subject text,
    p_audit_id uuid, p_event_id uuid, p_correlation_id text
) RETURNS uuid LANGUAGE plpgsql SECURITY INVOKER
SET search_path = pg_catalog, pg_temp
AS $fn$
BEGIN
    IF p_issuance_id IS NULL OR p_audit_id IS NULL OR p_event_id IS NULL
       OR p_center_id IS NULL OR btrim(p_center_id) = ''
       OR p_program_id IS NULL OR btrim(p_program_id) = ''
       OR p_national_version IS NULL OR p_national_version <= 0
       OR p_issuer_subject IS NULL OR btrim(p_issuer_subject) = ''
       OR p_correlation_id IS NULL OR btrim(p_correlation_id) = '' THEN
        RAISE EXCEPTION 'Invalid national issuance record' USING ERRCODE = '22023';
    END IF;

    INSERT INTO programs.national_program_issuances(
      issuance_id, center_id, program_id, national_version,
      issued_by_subject, issued_at)
    VALUES (p_issuance_id, p_center_id, p_program_id, p_national_version,
            p_issuer_subject, transaction_timestamp());

    INSERT INTO governance.command_audit(
      audit_id, occurred_at, actor_subject_id, center_id, action_code,
      target_type, target_id, correlation_id, metadata)
    VALUES (p_audit_id, transaction_timestamp(), p_issuer_subject, p_center_id,
      'NATIONAL_PROGRAM_ISSUANCE_RECORDED', 'national_program_issuance',
      p_issuance_id::text, p_correlation_id,
      jsonb_build_object('program_id', p_program_id, 'national_version', p_national_version));

    INSERT INTO governance.outbox_events(
      event_id, audit_id, event_type, aggregate_type, aggregate_id,
      occurred_at, payload)
    VALUES (p_event_id, p_audit_id, 'NationalProgramIssuanceRecorded',
      'national_program_issuance', p_issuance_id::text, transaction_timestamp(),
      jsonb_build_object('issuance_id', p_issuance_id, 'center_id', p_center_id,
        'program_id', p_program_id, 'national_version', p_national_version));
    RETURN p_issuance_id;
END;
$fn$;
REVOKE ALL ON FUNCTION programs.record_national_program_issuance(
  uuid,text,text,integer,text,uuid,uuid,text) FROM PUBLIC;
COMMENT ON FUNCTION programs.record_national_program_issuance(uuid,text,text,integer,text,uuid,uuid,text) IS
  'DDL-only atomic issuance evidence. No runtime role may execute until verified HQ human grants and trust boundary are approved.';
COMMIT;
