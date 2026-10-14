# Sprint 33 — Center private work-card planning foundation

**Business:** KY-ORG-04 shared tasks and follow-ups, KY-LOC-02/04
planning within the known B04 WF-04. An unassigned private *plan*
card, not a work-order, staff allocation, claim of actual performance
or national program compliance result.

- Each immutable `operations.center_work_cards` identity is pinned by
  composite FOREIGN KEY to the exact `(center_id,draft_id,revision)`
  owner record of the true `programs.center_plan_draft_revisions`.
  Each card has appended immutable task planning revisions with a bounded
  plain-text title (1–160), planning note (up to 1200), and nullable UTC
  planned due time. No fake due date, assignee, priority, completion,
  execution status, remuneration, SLA, alert, audit decision or scoring.
  Source plan edits never silently repin a card; a new card identity
  is required for an independently pinned source.
- POST `/api/v1/centers/{centerId}/operations/work-cards/revisions`
  with `workCardId,expectedRevision,draftId,pinnedPlanRevision,title,
  planningNote,plannedDueAt` performs command-only, role restricted
  advisory locked revision CAS and Audit + Outbox in one transaction.
  No automatic authorizations, or mutable UPDATE/DELETE pathways.
- GET `.../operations/work-cards?beforeCardId=<uuid>` lists at most
  20 current cards, keyset on immutable card identity; GET
  `.../operations/work-cards/{workCardId}/revisions?beforeRevision=<N>`
  lists at most 20 immutable revisions and the exact local-plan pin.
  All responses `PRIVATE_DRAFT`, `executionStatus:"UNKNOWN"`,
  `isExecuted:false`, no inference of zero activity.
- All three handlers demand signed OIDC subject and two separate
  effective revocable permissions (operation-specific independent work
  card grant and independent local plan draft read grant), default DENY.
  Database adds live membership, FORCE RLS, NOLOGIN audit-owning function
  role, EXECUTE-only command login and isolated safe reader. A center-id
  request, role claim or session variable alone does not authorize.
- UI fetches genuine local plan drafts from the source read API and
  stores only an explicitly chosen draft/version. It supports new
  cards, revisions, optional UTC due time, source-first history,
  conflict/denial and scoped cancellation on center/credential switches.
- Tests use a dedicated disposable center, real PostgreSQL/CAS/revoked
  actors/foreign source, Audit/Outbox/immutability/reader denial,
  handler validation and browser source selection/forgery/denial.

**Unresolved and deliberately out of scope:** human assignment
permissions/roles, tasks status vocabulary, claims of execution,
verification, reminders, participant records and public delivery.
Business contract and policy approvals are prerequisites to those
features. No Ready for Review, merge, Stage, QA Gate, Release or
Production without explicit owner direction. PR Draft/Open.
