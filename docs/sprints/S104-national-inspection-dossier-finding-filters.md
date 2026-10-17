# Sprint 104 — Exact-version dossier finding filters

## Business outcome

Let a headquarters analyst narrow an exact-version technical dossier to notes carrying a known inspection finding code, then continue through the filtered result set without losing its exact target or filter.

## Technical contract

- Reuse only the four stable note-level codes in `programs.national_issuance_review_inspection_findings`.
- Support all notes, any note with a finding, or one allowlisted finding code on the paginated dossier endpoint.
- Bind `findingsOnly` and `findingCode` into the existing exact-target authorization scope for each page request.
- Reject unknown codes and any filter on complete CSV export; the export remains a full exact-version dossier.
- Version the page response contract and echo the exact filter so the browser rejects mismatched or stale responses.
- Keep current-source and immutable inspection evidence read-only. Finding codes remain technical diagnostics and do not describe human decisions, permissions, or issuance.

## Verification

- API tests cover exact filter scope, unknown-code rejection before authorization, and rejection of filtered CSV requests.
- PostgreSQL integration verifies an exact-version filtered result against the existing finding view.
- Browser tests verify filter parameters, contract echo, and exact target preservation.
- Production authorization remains default-deny; integration tests use only an explicit in-process test adapter.

## Boundaries

B02/T15 remain drafts. This sprint creates no role, grant, identity authority, review decision, or issuance workflow.

## Release state

Draft PR only. No merge, Stage, QA gate, release approval, or Production action.
