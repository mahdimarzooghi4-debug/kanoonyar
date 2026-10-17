# Sprint 100 — National inspection CSV schema validation

## Business contract

The headquarters aggregate export must be interpretable against an explicit, versioned column contract. Before a browser download begins, the client verifies the declared schema, the exact ordered header, every logical record's field count, the server-reported data-row count, and the existing SHA-256 receipt. Quoted commas, doubled quotes, and CRLF inside quoted values remain valid CSV content.

## Technical scope

- The export endpoint declares `S100_NATIONAL_INSPECTION_CSV_SCHEMA_V1` alongside the existing S98 receipt headers.
- The browser accepts only the exact 23-column aggregate header and 23 fields for each logical record.
- Backend PostgreSQL coverage asserts the schema header and 23-column record shape.
- Browser coverage uses the actual aggregate header shape and rejects unknown schema versions and malformed field counts before creating a download.

## Boundaries

The export remains aggregate-only and excludes center-level identifiers and private notes. Schema validation, row counts, and SHA-256 establish format and byte consistency only. They do not establish evidence truth, authorization, human review, approval, or issuance. Existing authorization remains fail-closed; no new grants, decisions, or operational issuance are introduced.

## Acceptance

- Valid S100 CSV with quoted/multiline cells passes header, field-count, row-count, and digest checks before download.
- Unknown schema versions, mismatched ordered headers, malformed CSV, wrong field counts, row-count disagreement, and digest disagreement do not start a download.
- Backend export retains its existing read authorization, transaction snapshot, aggregate projection, and maximum-row limit.
- Backend, PostgreSQL integrity, frontend/Chromium, AI-contract, and secret-scan CI gates pass for the exact PR head.
- PR remains Draft/Open/Unmerged; no Stage, QA gate, release approval, or production action occurs.
