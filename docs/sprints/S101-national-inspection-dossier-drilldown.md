# Sprint 101 — Exact-version national inspection dossier drill-down

## Business contract

The headquarters workbench needs to follow an aggregate technical finding to the immutable inspection records behind one exact national program version. This is a technical evidence view, not an approval or issuance workflow.

## Technical scope

- Add a read-only, keyset-paginated endpoint for one exact program/version using the existing `SECURITY INVOKER` inspection dossier projection.
- Return only request/note IDs, inspection timestamp, integrity booleans, current-source comparison state, and bounded aggregate change counts. Exclude center identifiers, center selections, source snapshots, digests, free-text notes, and claimed inspector identity.
- Require a distinct unapproved dossier authorization seam and a separately named `NATIONAL_INSPECTION_DOSSIER_READ_DATABASE_URL`. Both default to unavailable/deny; no runtime database grant or SQL role is created.
- Add a headquarters drill-down from each aggregate row. Validate the exact dossier contract, response target, page cursor, bounded fields, and all false decision/issuance flags before rendering.
- Exercise more than one page against restricted synthetic PostgreSQL data and cover scope, denial, cursor, response-integrity, and privacy boundaries in tests.

## Authority and privacy boundaries

B02 and T15 remain drafts. The new permission seam is deny-by-default, separate from aggregate-rollup access, and is not registered to an approved runtime grant. The database connection has its own configuration key and no migration creates or grants a production role. The endpoint and page expose technical identifiers/status only; they do not expose center identities, private evidence text, or source snapshots. Every human-review and issuance flag remains false. No status is interpreted as permission, approval, compliance, or authorization to issue.

## Acceptance

- Unauthenticated, subjectless, unapproved, malformed, and unavailable requests fail closed.
- Exact program/version filtering and deterministic `(inspection_recorded_at, note_id)` keyset pages return no duplicates in synthetic integration coverage.
- Browser rejects unknown contracts, mismatched target/cursor, malformed data, duplicate notes, and any asserted human-review/issuance state.
- Existing aggregate rollup, pagination, CSV schema/receipt checks, and center-private data exclusions remain intact.
- Full Foundation CI passes on the exact PR head; PR remains Draft/Open/Unmerged with no Stage, QA gate, release, or Production action.
