# Sprint 10 — National Program Issuance → Center Local Plan Trace

## Implementation
- Migration 0014 enforces exact center+national version agreement between
  immutable issuance and pinned local draft revision using composite FKs.
- An append-only link is recorded with matching audit and outbox in the same
  transaction. No runtime principal is granted permission to create links.
- GET /api/v1/centers/{centerId}/local-plan-drafts/{draftId}/execution-trace
  returns the immutable issuance ID, originally pinned draft revision,
  latest local draft revision, program/version identity, and planned activities.
- Missing or invisible links return null, without revealing whether any
  foreign-center link exists. Read requires authenticated subject, independent
  operation read grant, active current membership and restricted SQL reader.
- Center UI renders this lineage on demand. It explicitly labels activities
  as PLANNED; no delivery/outcome/evidence is claimed.
- Security tests: actual restricted PostgreSQL + handler reads, audit/outbox
  cardinality, draft version progression, cross-center and revoked identities,
  signed JWT default-deny, malformed payloads, center-switch clearing in Chromium.

## Explicit boundaries
- Runtime read/write grants remain DENY until separately configured and verified.
- The SQL GUC is spoofable by an independent credential holder and cannot
  establish authenticated tenant identity. Test-only SQL grants are not Production.
- Linked draft data is not proof an activity was executed or evaluated.
- No Stage, QA Gate, merge, release or Production approval; PR stays Draft.