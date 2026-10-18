# Sprint 112 — Validate inspection dossier chronology

## Business outcome

Keep the headquarters dossier display consistent with the inspection timeline returned by the read API. A malformed or out-of-order payload must fail closed instead of presenting evidence in a misleading sequence.

## Technical contract

- Accept only canonical UTC timestamps emitted by the API in round-trip ISO format with seven fractional digits.
- Reject calendar-invalid timestamps even when the JavaScript date parser would normalize them.
- Require rows within each dossier page to be in descending inspection-time order, matching the server query.
- Keep the existing UUID cursor, match-set fingerprint, filters, privacy boundary, and CSV behavior unchanged.

## Verification

- Browser coverage supplies an ascending two-row response and verifies that the dossier table stays hidden and the contract error is shown.
- Foundation CI must pass backend, PostgreSQL integration, frontend/Chromium, AI contracts, and secret scan for the final commit.

## Boundaries

This validates the technical ordering of returned evidence. It is not a human review, approval, authorization, or issuance decision.

## Release state

Draft PR only. No merge, Stage, QA gate, release approval, or Production action.
