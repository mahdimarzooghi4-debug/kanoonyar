# Sprint 84 — Default-deny national inspection rollup API

## Business
The technical rollup from Sprint 83 needs a stable, paginated API shape before a headquarters workbench can consume it. The real reader identity and permission policy remain unapproved.

## Technical
Adds `GET /api/v1/headquarters/national-issuance/review-rollup`. The endpoint requires authenticated OIDC subject presence, an independent authorization seam that is explicitly unapproved and defaults to deny, a separately configured read database URL, a restricted SQL login, and a repeatable-read, read-only transaction. It pages the Sprint 83 aggregate view by the composite program/version cursor and returns only the existing aggregate fields.

Unit tests cover unauthenticated and subjectless requests, default-deny behavior, malformed/repeated cursors and unavailable storage. The disposable PostgreSQL integration test creates 22 synthetic program/version groups and a CI-only read login/policy, then exercises multiple API pages and checks that private identifiers and source text are absent.

## Boundaries
No headquarters role, permission matrix, production grant, identity proof beyond the OIDC subject claim, human review, approval, issuance or operational decision is created. CI's synthetic grant and RLS policy are test fixtures on a disposable database only. The endpoint remains unusable in Production while the authorization contract is unapproved.

## Delivery
Stacked on PR #83. Keep Draft/Open and unmerged. No Stage, QA gate, release approval or Production. CI and technical review are not independent human approval.
