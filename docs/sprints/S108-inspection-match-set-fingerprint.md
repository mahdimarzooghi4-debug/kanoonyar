# Sprint 108 — Fingerprint the exact dossier match set

## Business outcome

Detect same-count changes to the set of inspection notes matching an exact national program version and active finding filter, so paginated dossier reads can reject a changed match set.

## Technical contract

- In the dossier request's repeatable-read transaction, stream matching note IDs in deterministic UUID order and count them without materializing the complete set in memory.
- Compute SHA-256 over the UTF-8 sequence of lowercase canonical UUID strings, each followed by one line-feed byte.
- Return `matchingNoteSetSha256` alongside `matchingNoteCount` in the S108 page contract; return the SHA-256 of the empty byte sequence when there are no matches.
- The workbench validates the digest format and compares both count and digest across page requests; mismatch stops pagination and hides the dossier table.
- Keep the CSV contract unchanged. The fingerprint is a membership consistency signal only, not a signature, authenticity proof, or check of other mutable source-comparison fields.
- The application streams IDs without materializing the complete list; this does not reduce database scan work or introduce a new query timeout.

## Verification

- PostgreSQL integration recomputes the digest from all synthetic note IDs and verifies it on first, last, and empty-cursor pages; a filtered one-note result has its own expected digest.
- Browser coverage simulates a same-count but different-match-set response and verifies that the next page is rejected.
- Existing exact-scope, restricted-role, privacy, CSV, and default-deny coverage remains required.

## Boundaries

This digest conveys no evidence truth, identity, human review, approval, permission, or issuance. It does not pin one database snapshot across multiple HTTP requests. B02/T15 remain drafts.

## Release state

Draft PR only. No merge, Stage, QA gate, release approval, or Production action.
