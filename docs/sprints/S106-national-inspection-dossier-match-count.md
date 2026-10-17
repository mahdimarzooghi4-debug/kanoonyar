# Sprint 106 — Exact-filter dossier match count

## Business outcome

Give headquarters analysts the total number of technical inspection notes matching the exact national program version and active dossier finding filter, including when a keyset cursor points to an empty page.

## Technical contract

- Compute the total from the same filtered relation and PostgreSQL repeatable-read transaction as the page query.
- Keep cursor pagination separate from the matching total, so an empty page still reports the total for that request's snapshot.
- Return `matchingNoteCount` in the S106 dossier JSON contract and reject missing, negative, unsafe, or smaller-than-page counts in the browser.
- Display the count as belonging to the current request snapshot. Separate page requests may observe newer database snapshots; the UI must not imply a multi-request pinned snapshot.
- Preserve exact program/version and finding-code authorization scope, restricted read-only database access, CSV contracts, privacy exclusions, and default-deny authorization.

## Verification

- PostgreSQL integration covers first and last pages, an empty cursor page with a nonzero match total, and a filtered exact-version result.
- Browser coverage verifies the count is shown and is contract-validated.
- Existing authorization, integrity, pagination, privacy, CSV, and 80-center synthetic data coverage remains required.

## Boundaries

The count is technical query metadata only. It is not a review decision, approval, permission, or issuance state. B02/T15 remain drafts; no policy, grant, human authority, or operational issuance behavior is created.

## Release state

Draft PR only. No merge, Stage, QA gate, release approval, or Production action.
