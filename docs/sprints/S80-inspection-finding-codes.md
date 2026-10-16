# Sprint 80 — Inspection finding code contract

## Business
Inspection evidence needs stable technical diagnostics so downstream review tooling can identify which evidence layer needs attention without interpreting private center-level source data.

## Technical
Migration `0060_national_issuance_inspection_finding_codes.sql` adds a SECURITY INVOKER, read-only projection with the versioned contract `S80_INSPECTION_FINDING_V1`. It maps existing dossier checks to stable codes for original-request integrity, observation integrity, audit/outbox integrity, and source comparability.

The projection contains only request/note identifiers, program/version, contract version, and the finding code. It exposes no center identifiers, source snapshots, notes, or inspector subjects. PostgreSQL tests verify its shape, denied public/restricted reads, and the four semantic observation fixtures.

## Boundaries
Finding codes are technical diagnostics. They do not attest identity, establish human review, grant permission, authorize issuance, or trigger any operation. No production grants, API, actor role, or decision workflow is introduced.

## Delivery
Stacked on PR #79. Keep Draft/Open and unmerged. No Stage, QA gate, release approval, or Production. CI and technical review are not independent human approval.
