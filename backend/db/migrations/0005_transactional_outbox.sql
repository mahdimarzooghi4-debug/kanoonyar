-- Transactional outbox storage for future audited commands.
-- Events are never published or dispatched by this migration alone.
BEGIN;
CREATE TABLE governance.outbox_events (
    event_id uuid PRIMARY KEY,
    audit_id uuid NOT NULL REFERENCES governance.command_audit(audit_id) ON DELETE RESTRICT,
    event_type text NOT NULL CHECK (length(btrim(event_type)) > 0),
    aggregate_type text NOT NULL CHECK (length(btrim(aggregate_type)) > 0),
    aggregate_id text NOT NULL CHECK (length(btrim(aggregate_id)) > 0),
    occurred_at timestamptz NOT NULL,
    payload jsonb NOT NULL CHECK (jsonb_typeof(payload) = 'object')
);
CREATE INDEX outbox_events_aggregate_idx ON governance.outbox_events (aggregate_type, aggregate_id, occurred_at);
CREATE TRIGGER outbox_events_append_only
BEFORE UPDATE OR DELETE ON governance.outbox_events
FOR EACH ROW EXECUTE FUNCTION programs.reject_version_or_publication_mutation();
COMMIT;
