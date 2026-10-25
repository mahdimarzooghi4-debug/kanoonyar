# Sprint 77 — Source envelope scalar JSON types

## Business
Integrity diagnostics must distinguish JSON scalar types. PostgreSQL text extraction with `->>` erases the distinction between a JSON number and a JSON string, so equality alone can accept malformed source evidence.

## Technical
Migration `0057_national_issuance_source_envelope_scalar_types.sql` requires the source `programId` to be a JSON string and `nationalVersion` to be a JSON number before comparing their textual representations to the immutable request columns. Existing semantic envelope diagnostics and the stable findings view retain their shape.

PostgreSQL integration fixtures each corrupt exactly one scalar type while keeping the snapshot digest, selected center, audit, outbox, and non-authority flags valid. Assertions require the envelope finding only.

## Boundaries
This change adds no role, permission, human review, authorization, issuance, API path, or operational account. Fixtures are synthetic and test-only. CI and this technical review are not independent human approval.

## Delivery
Stacked on PR #76. Keep the PR Draft/Open and unmerged. No Stage, QA gate, release approval, or Production.
