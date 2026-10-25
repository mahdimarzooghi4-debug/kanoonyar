# Sprint 83 — National technical inspection rollup

## Business
National oversight needs a compact view of technical inspection coverage across national program versions, while keeping center-level source evidence private and not confusing evidence with an operational decision.

## Technical
Migration `0063_national_issuance_national_inspection_rollup.sql` adds `programs.national_issuance_review_national_inspection_rollup`, a SECURITY INVOKER projection with one row per requested program/version. It reports request and note coverage, integrity consistency, source-comparison availability, current-source freshness categories, and the latest technical observation timestamp.

The PostgreSQL test compares the rollup with underlying request, note, and dossier rows for the synthetic national program/version fixtures; it also checks unique grouping, the fixed output shape, restricted read access, and the no-decision boundary.

## Boundaries
The rollup exposes no center, request, or note identifiers, private source snapshots, note text, or inspector identity. It adds no API, grants, roles, human review, permission, authorization, or issuance. Counts are technical evidence only.

## Delivery
Stacked on PR #82. Keep Draft/Open and unmerged. No Stage, QA gate, release approval, or Production. CI and technical review are not independent human approval.
