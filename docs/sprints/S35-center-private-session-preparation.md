# Sprint 35 — Private class session preparation (KY-LMS-05 technical foundation)

Business reference: B03 KY-LMS-05 (educational content / assignments and assessment
**only if an approved contract exists**), B04 WF-02, B05 AC-LMS-04/05/06.
All Business texts remain DRAFT. This sprint implements only **private educator
preparation**, not student-facing materials, assignments, grades or publication.

## Durable contract
- Reuse S12 canonical `learning.class_sessions` and `learning.center_classes`
  as source owners. A preparation is uniquely keyed to a genuine same-center
  class session, including the original class ID; no invented standalone
  class/session data, independent timing assumptions or mirrored attendance.
- `learning.session_preparations` identity is immutable and FK-constrained.
  `learning.session_preparation_revisions` appends bounded educator-authored
  plaintext title (1–160), teaching outline (<=4000), and materials note
  (<=2000). No HTML/embedded remote URL execution, uploads, student PII,
  external content license, publishing, grading or outcome inference.
- POST `/api/v1/centers/{centerId}/learning/session-preparations/revisions`
  requires explicit `sessionId,classId,expectedRevision,title,teachingOutline,
  materialsNote`. Scoped SQL SECURITY DEFINER NOLOGIN command role checks
  exact canonical source, active actor membership, original class binding,
  optimistic revision CAS behind per-session advisory transaction lock;
  preparation+Audit+Outbox are one transaction, and every revision immutable.
- GET `.../learning/session-preparations?beforeSessionId=...`: at most 20
  latest private preparations, keyset by immutable session UUID, joined to
  authentic class/session titles. GET `.../learning/session-preparations/
  {sessionId}/revisions?beforeRevision=N`: 20 at a time, immutable
  historical authored content, true original class+session. A missing
  preparation is 404, not a fabricated empty teaching success.
- Required: signed OIDC subject and live current center membership, a separate
  revocable operation grant `SESSION_PREPARATION_APPEND|LIST_READ|HISTORY_READ`
  **plus** independently approved `ICenterSourceCatalogReadAuthorization`;
  both deny by default. PostgreSQL FORCE RLS on both tables, writer EXECUTE-only,
  reader restricted SELECT-only, no public route, and no role/session-GUC-only
  authorization.
- All responses explicitly `visibility=PRIVATE_PREPARATION`,
  `isPublished=false`, `deliveryVerified=false`,
  `isAttendanceVerified=false`, `isAssessed=false`. No delivery or
  achievement inferred from preparation.
- Center UI selects genuine paginated source-catalog session IDs/class IDs,
  writes/revises teaching notes, reads version history and resets on
  center/token change; strictly rejects any forged delivered/published or
  assessed status.

## Verification and deferred decisions
- Unit + signed JWT superadmin-denial tests; disposable PostgreSQL original-source
  and same-center FK tests, foreign/missing session, class mismatch, revocation,
  one-winner revision races, RLS, immutable history, Audit/Outbox, privilege guards;
  real Chromium positive and negative privacy tests.
- Learner publication, instructor role and grant policy, student assignments,
  marking rubric, licensed templates, age-related consent and outcome evaluation
  remain blocked until Business/Technical approval. No Ready for Review, Merge,
  Stage, QA Gate, Release or Production. PR stays Draft/Open.
