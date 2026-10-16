# Sprint 88 — Attendance history query path

## Business
The center history reader must keep predictable page retrieval as immutable attendance claims accumulate across all 80 centers. The Sprint 87 proof established correct 20-row keyset pages; this sprint aligns PostgreSQL's access path with that exact center/session/occurrence filter and timestamp/claim ordering.

## Technical
Add the composite B-tree index `(center_id, session_id, occurrence_report_id, recorded_at DESC, claim_id DESC)` for the attendance history query. Apply it in the CI API fixture and in the isolated PostgreSQL integrity suite. Exercise the continuation query shape with `EXPLAIN (FORMAT JSON)` and assert PostgreSQL can use the named index while preserving the existing filter and order.

## Acceptance
- Fresh PostgreSQL migration creates a valid page index on the immutable attendance-claim source table.
- CI executes the existing audited occurrence/attendance integrity checks and fails if the keyset continuation query plan does not use the supporting index.
- The API integration fixture also applies the index before running the restricted-reader pagination coverage.
- No history columns, claim values, RLS policies, SQL roles, grants, or verification semantics change.
- All rows remain UNVERIFIED; the index provides query access only.

## Boundaries
No synthetic test result is represented as attendance verification or national operational authorization. No live or production data is touched. Query-plan evidence on a disposable test database does not guarantee production latency; production performance still requires measured representative workload and monitoring.

## Delivery
Stacked on Sprint 87. Keep Draft/Open/unmerged. No Stage, QA gate, release approval, or Production.
