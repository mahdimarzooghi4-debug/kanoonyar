# Sprint 90 — Aggregate national inspection finding codes

## Business and technical contract
The national inspection workbench needs enough grouped technical evidence to locate recurring data-integrity causes across program versions while preserving center and request privacy. This increment extends the S83 program/version rollup with stable S76 request-finding and S80 inspection-finding counts.

## Scope
- Add SECURITY INVOKER view `programs.national_issuance_review_national_inspection_rollup_v2` with contract `S90_NATIONAL_INSPECTION_ROLLUP_V2`.
- Preserve every S83 aggregate and add six request-component counts plus four inspection-note-component counts. Each count is derived from the existing stable finding views and grouped only by program/version.
- Point the existing default-deny read endpoint at V2 and return the component counts through its existing repeatable-read, read-only, restricted-role path.
- Validate count shape and per-component bounds in the headquarters workbench; show technical finding-code counts beside existing coverage metrics.
- Add PostgreSQL shape, privacy, permission, source-parity and false-decision-flag checks, plus a restricted PostgreSQL API integration assertion with synthetic clean records.
- Keep S83 unchanged for existing consumers.

## Boundaries
Counts may overlap across diagnostic components and do not rank, decide, approve, authorize, issue, or imply human review. This sprint creates no real identities, permissions, approval rules, center/request identifiers, private note text, or operational workflow. The API authorization remains default-deny; no production grants are added. CI fixtures are synthetic and disposable. A green CI and technical review comment are not independent human approval or release evidence.

## Acceptance
- V2 has an exact versioned, aggregate-only column contract and is SECURITY INVOKER.
- Every component count equals the corresponding source finding-code count for its program/version and is bounded by the associated request or note total.
- No center, request, note, snapshot, inspector, or requester identifiers are exposed.
- PUBLIC and restricted test roles remain unable to read without explicit test-only provisioning.
- All human-review and issuance flags remain false.
- API and UI consume the same V2 contract and malformed counts are rejected.
