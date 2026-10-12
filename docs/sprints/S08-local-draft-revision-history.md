# Sprint 08 extension — immutable local draft history reader

## Backend contract
- GET /api/v1/centers/{centerId}/local-plan-drafts/{draftId}/revisions lists versions
  for a single draft, ordered by strictly descending revision.
- Optional beforeRevision is an exclusive positive numeric cursor.
- Response contains draftId, up to 20 revisions, nullable nextBeforeRevision.
- The fixed 20-row cap is a technical payload limit, NOT a Business rule or threshold.
- Cursor appears only when more older revisions exist, enabling complete retrieval.
- Each item contains revision, programId, nationalVersion, localTitle,
  localActivities, and recordedAt. No author identity, publication or approval is inferred.

## Authorization and technical evidence
- Authenticated OIDC subject and independent read-operation authorization are required.
- Runtime authorization remains DENY by default, irrespective of membership or JWT role.
- The restricted read-only DB login, FORCE RLS, active center membership and center scope
  are mandatory. The SQL session center setting alone is not trusted authorization.
- Nonexistent, revoked, or cross-center draft history returns an empty page, not existence data.
- No write endpoint, restore, rollback or re-publication is exposed by this feature.
- Backend validation/unit tests and real PostgreSQL integration test 22 consecutive revisions,
  pagination, cross-center isolation, no duplicate pages and audit/outbox counts.
- Signed RSA JWT HTTP test with synthetic local issuer verifies the route, still not Keycloak.
- React view supports read-only history and older-page navigation.
- Chromium browser tests use mocked responses; they verify denial, malformed payload and
  clearing history on center change but are not browser-to-real-DB acceptance.

## Remaining gates
- Grant/delegation/revocation matrix awaits Business approval.
- Non-spoofable DB trust boundary, real Keycloak and end-to-end runtime authorization
  are not proven by disposable CI SQL roles or mock authorization adapters.
- No actual user read/write enablement, merge, Ready for Review, Stage, QA Gate,
  Release Approval, or Production is authorized.