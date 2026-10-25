# Sprint 86 — Eighty-center learning evidence isolation

## Business
National rollout depends on protecting each center's learning evidence. The existing S50 proof covers original classes and sessions; Sprint 86 extends synthetic PostgreSQL evidence to immutable occurrence claims and attendance claims.

## Technical
Create exactly 80 synthetic center identities, substitute subjects, classes, sessions, occurrence claims, and attendance claims in the required disposable PostgreSQL integration database. Seed claims only through the existing audited database functions. Exercise both history read handlers through the restricted read SQL role. Verify each center reads only its own claims; cross-center requests return no records; a member valid in two centers receives only the explicitly requested center; membership revocation and center inactivation suppress reads. Every returned claim remains UNVERIFIED.

## Acceptance
- Required CI fails if its disposable admin/read database URLs are missing.
- Exactly 80 unique occurrence claims and 80 unique attendance claims remain isolated to their synthetic centers.
- Neighboring center IDs, participant references, notes, and claim values never appear in the response.
- Dual-center access remains scoped to the center explicitly requested.
- Revocation and inactive registry state block each corresponding history read.
- No participant identity or verified attendance fact is created.

## Boundaries
No real center or participant data, public enrollment, registration, attendance verification, KPI, operational permission, or Production grant is added. The test uses a synthetic operation grant only inside its test assembly; runtime authorization stays default-deny. This is bounded isolation evidence, not onboarding or production readiness.

## Delivery
Stacked on Sprint 85. Keep Draft/Open/unmerged. No Stage, QA gate, release approval or Production.
