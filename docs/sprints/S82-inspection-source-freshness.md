# Sprint 82 — Inspection source freshness diagnostics

## Business
Technical reviewers need to distinguish an observation that still matches current source data from one that became stale after capture. When the observation is incomparable, freshness is unknown and drift claims must be withheld.

## Technical
Migration `0062_national_issuance_source_freshness_diagnostics.sql` adds SECURITY INVOKER projection `programs.national_issuance_review_source_freshness_diagnostics` with contract `S82_SOURCE_FRESHNESS_V1`. It classifies unavailable comparison, current-source match, publication change, center-registry change, exact-issuance change, and unclassified source drift. Aggregate counts are included only for comparable evidence and their unit is specific to the diagnostic code.

PostgreSQL tests verify a historical issuance drift, unchanged observations, and suppression of change counts for all four S79 incomparable fixtures. Output excludes source snapshots, center identifiers, notes, and inspector subjects.

## Boundaries
Freshness diagnostics are read-only technical evidence, not a decision, approval, permission, authorization, or issuance. No operational API or grant is introduced.

## Delivery
Stacked on PR #81. Keep Draft/Open and unmerged. No Stage, QA gate, release approval, or Production. CI and technical review are not independent human approval.
