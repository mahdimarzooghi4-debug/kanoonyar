# Sprint 14 — Session Occurrence and Attendance Claims

## Design and source of truth
- Separate planned class session (Sprint 13) from a newly submitted UNVERIFIED
  session occurrence report. An occurrence report is evidence of a **claim**,
  not a verified delivered session or official numerator for reporting.
- An attendance claim references that exact center, occurrence and session.
  `REPORTED_PRESENT` and `REPORTED_ABSENT` explicitly denote unverified human
  assertions; no attendance row means UNKNOWN, never absent or zero.
- Participant reference is opaque and must originate from an authorized
  participant identity/consent source before any real-person usage. The
  integration fixtures are synthetic, not real persons or enrollments.
- `claimed_started_at` and `claimed_ended_at` remain NULL when unavailable.
  A supplied end cannot precede a supplied start. `evidence_ref` is opaque
  and is not proof that a protected source object has been verified.

## Technical guarantees
- Migration 0018 adds immutable center-scoped FK-linked occurrence reports
  and per-occurrence attendance claims with unique participant ref per report.
- FORCE RLS, audit/outbox atomic command wrappers, no table/public command
  write grants and restricted SQL owner. Repeated/conflicting claim within one
  occurrence is blocked; later independent reports never silently overwrite it.
- Negative PostgreSQL tests: wrong center/session FK, illegal verified state,
  conflict/duplicate, audit rollback, append-only updates, and cross-center read.
- All actual operation grants and public access remain DENY until separate
  verified identity, privacy and resource authorization exist.

## Excluded
No attendance score, aggregate KPI, automatically verified occurrence,
certificate, enrollment, approval, payment, instructor authorization or
actual customer/private identity data. No Stage, QA, release, merge or Production.