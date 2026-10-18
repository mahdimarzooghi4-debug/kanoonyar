# Sprint 110 — Verify dossier continuation anchors

## Business outcome

Prevent keyset pagination from continuing from a stale or altered inspection timestamp, even when the matching note-ID set itself has not changed.

## Technical contract

- After validating the expected match-set fingerprint, verify that the supplied cursor note ID, recorded timestamp, target program/version, and active finding filter still identify one exact dossier row.
- Perform the anchor check in the same read-only repeatable-read transaction and return HTTP 409 before querying page rows when the anchor no longer matches.
- Require the fingerprint precondition as established in Sprint 109; preserve the JSON count/hash checks and CSV behavior.
- Bump the JSON dossier contract version. The client continues to send the first-page fingerprint and uses the existing conflict/reload path.

## Verification

- PostgreSQL integration verifies valid continuation, missing-precondition rejection, changed-set conflict, changed-timestamp anchor conflict, and an empty page after the actual final-row cursor.
- Browser fixtures use the S110 contract and keep the server conflict/reload behavior covered.
- Existing exact-target, restricted-role, privacy, CSV, and default-deny checks remain required.

## Boundaries

This detects cursor-anchor drift within each request snapshot. It does not pin a snapshot across requests or make the fingerprint an authenticity proof. No human decision, authority, grant, or issuance behavior is added.

## Release state

Draft PR only. No merge, Stage, QA gate, release approval, or Production action.
