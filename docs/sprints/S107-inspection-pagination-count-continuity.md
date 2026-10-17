# Sprint 107 — Detect dossier pagination count drift

## Business outcome

Prevent the headquarters inspection workbench from presenting pages as one continuous dossier when separate page requests report different totals for the same exact version and active finding filter.

## Technical contract

- Compare each newly fetched page's S106 `matchingNoteCount` with the first loaded page for the same dossier target and filters.
- On a mismatch, stop pagination, do not append the fetched page, hide the mixed page table, and ask the analyst to reload the dossier.
- Keep duplicate-note detection and the existing exact-target cursor contract.
- Describe this as count-drift detection only; equal counts do not prove that independently read pages came from one database snapshot.
- Preserve per-request REPEATABLE READ, current authorization scope, default-deny behavior, and CSV contracts.

## Verification

- Browser test simulates a count change between page requests and verifies that the second page is not shown or appended.
- Existing backend, PostgreSQL, privacy, filter, and export tests remain required.

## Boundaries

A count mismatch is a technical consistency signal. It does not establish evidence truth, human review, approval, permission, or issuance. No snapshot store, role, grant, or operational decision is introduced.

## Release state

Draft PR only. No merge, Stage, QA gate, release approval, or Production action.
