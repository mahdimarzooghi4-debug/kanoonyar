# T11 — Audited command and outbox persistence foundation

Migrations `0004_command_audit.sql` and `0005_transactional_outbox.sql` introduce durable append-only command audit and an event outbox referencing an existing audit entry. PostgreSQL constraints require explicit actor, action, target, correlation and event lineage; orphan events are rejected. CI runs these migrations against disposable PostgreSQL and tests append-only behavior, foreign-key integrity and rollback of rejected writes.

This is **storage only**, not a working command workflow. No authenticated principal, Keycloak configuration, role policy, authoritative command handler, atomic business-write/audit/outbox transaction, outbox delivery, retry/dispatch, RLS or cross-center read authorization has been implemented. Outbox rows are deliberately immutable and have no invented delivery state; a future dispatcher requires a separate reviewed delivery-tracking design. Audit metadata/payload must never be used for minors' personal data or unreviewed free text. A database actor string is not proof of verified identity.

Next: implement verified OIDC integration and authoritative center/role permission checks; then a transactional command path which writes business state, audit and outbox together with concurrency and tenant isolation tests. Do not expose writes or claim production authorization before those gates.
