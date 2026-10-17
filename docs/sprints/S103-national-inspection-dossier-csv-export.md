# Sprint 103 — Exact-version inspection dossier CSV export

## Business outcome

Let an authorized headquarters analyst preserve a complete, bounded technical inspection dossier for one exact national program version. The export supports offline technical examination without expanding the browser page size or exposing center identities and private evidence.

## Technical contract

- Add a separate exact-version CSV export endpoint and an explicit `InspectionDossierCsvExport` authorization operation.
- Reuse the restricted read-only PostgreSQL connection and one repeatable-read transaction for the full export.
- Reject page cursors on export; require one exact program ID and national version.
- Cap export at 10,000 rows and return a safe 413 response instead of a partial file.
- Publish a stable 17-column schema, row count, contract/schema identifiers, and SHA-256 receipt.
- Escape CSV formula prefixes, quote cells, emit UTF-8 BOM/CRLF, and omit center IDs, source snapshots/digests, inspector claims, and note text.
- Validate schema, row count, UTF-8 marker, and SHA-256 in the workbench before starting the download.

## Verification

- Restricted PostgreSQL integration fixture verifies a complete 51-note export, exact target, receipt digest, and private-field exclusion.
- API tests assert that CSV export receives its distinct exact-target scope and rejects paging cursors before authorization.
- Browser tests cover successful integrity-checked download, corrupt digest rejection, and safe over-limit behavior.
- Production authorization remains `UnapprovedNationalInspectionReadAuthorization`; tests use only an explicit in-process test adapter.

## Boundaries

B02/T15 remain drafts. No access role, export grant, identity authority, human review, approval, or issuance is created. The endpoint remains unavailable through the production default-deny adapter. Technical review and CI are not independent human approval.

## Release state

Draft PR only. No merge, Stage, QA gate, release approval, or Production action.
