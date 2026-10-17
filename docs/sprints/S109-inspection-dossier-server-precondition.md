# Sprint 109 — Enforce dossier match-set preconditions

## Business outcome

Reject a dossier continuation at the API boundary when the set of inspection notes no longer matches the fingerprint returned with the first page. A changed page is rejected before its rows are queried or returned.

## Technical contract

- Require `expectedMatchingNoteSetSha256` on JSON continuation requests and reject it on initial-page and CSV requests.
- Recompute the exact filtered note-ID count and SHA-256 within the request's repeatable-read transaction.
- Compare the supplied fingerprint with the current request snapshot before executing the page-row query; return HTTP 409 on mismatch.
- Bind the workbench's continuation request to the first page's fingerprint and show an explicit reload message for a conflict.
- Keep client-side count and fingerprint checks as defense in depth; bump the JSON contract version. Preserve CSV behavior.
- The comparison is per request snapshot. It does not reserve a snapshot or prevent data changing after the API read. The fingerprint is still an unkeyed membership consistency signal, not an authenticity proof.

## Verification

- PostgreSQL integration passes the first-page fingerprint to a continuation, exercises an empty cursor page, and verifies that a different valid fingerprint returns 409.
- Browser coverage verifies the continuation sends its expected fingerprint and handles the API conflict without displaying the returned page.
- Existing access, exact-scope, privacy, CSV, and default-deny checks remain required.

## Boundaries

No authority, role, approval, human decision, issuance, or production behavior is added. B02/T15 remain drafts.

## Release state

Draft PR only. No merge, Stage, QA gate, release approval, or Production action.
