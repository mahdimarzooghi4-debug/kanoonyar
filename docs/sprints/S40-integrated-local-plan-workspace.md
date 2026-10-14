# Sprint 40 — Integrated source-owned local-plan operational workspace

**Source of truth**: Original center local-plan draft revisions (S08/S10),
S33 owner work cards with exact pinned plan revision, and S39 owner resource
planning needs. This is a cohesive staff-private *workflow view*, not an
additional shadow ledger or a new workflow authority.

## User pathway
1. Staff select an **actual** saved local plan and exact immutable revision.
2. Server composes the plan title/national-program lineage, work cards
   pinned to **that** exact plan revision and their latest human-authored
   resource needs in **one PostgreSQL REPEATABLE READ transaction**.
3. Work-card list pages by 20 immutable IDs; each card includes the latest
   five need records and `nextBeforeNeedId` when there are more.
   Staff open the existing S39 original resource-need editor inside the
   card, which uses its **own separate write grant** and complete
   need pagination/history. This is a bounded preview, never an
   inferred absence of resource requirements or a fulfillment score.
4. A later plan revision is a different versioned workspace: old S33
   pinned cards do not migrate automatically to the new plan revision.
   All data is source-owned, read-only through the integration endpoint.

## Contract / security
GET `/api/v1/centers/{centerId}/operations/local-plan-workspace/{draftId}/{planRevision}`
(optional exclusive `beforeCardId`), returns immutable plan lineage,
exact source card identity + revision and latest needs, with
`PRIVATE_PLANNING`, `executionStatus=UNKNOWN`,
`fulfillmentStatus=UNKNOWN`, isExecuted/isAllocated/isPurchased/isFulfilled=false.
No approval, grading, actual allocation, purchase or delivery claims.

A signed OIDC subject, live active center membership, independent
LocalPlanDraftRead, WORK_CARD_LIST_READ and WORK_RESOURCE_NEED_LIST_READ
grants are required; additionally EVERY returned card is checked
separately for its original card and need read grants; any denial
blocks the complete page, not only the hidden row.
This prevents aggregate wildcard scopes from bypassing card grants.
An elevated/admin SQL login fails restricted SELECT-only original-owner
role checks; existing original source tables all enforce FORCE RLS;
requested center ID and SQL GUC never become authority.
The client validates full nested private/non-executed flags,
same plan lineage, bound page sizes/cursors and center/token state
changes; React escapes all user-authored content.

## Evidence
Backend DTO/cursor/security tests and signed-JWT superadmin-without-grants
denial; real disposable PostgreSQL with original national/local
program fixture, two plan revisions, 21 actual work cards, 6 actual
resource needs, latest immutable need revision, foreign center,
revoked actor, per-card denied grant, limited preview cursor,
read-only SQL credentials; real Chromium private view, seamless
transition to existing need editor, forgery rejection and denial.

## Blocked
Business/Technical workflow and responsible assignment, resource
allocation/procurement, public dashboards, outcome validation,
personal/child data rights and Production permission grants remain
unapproved. No synthetic real activity claims. PR stays Draft/Open,
no Ready for Review, Merge, Stage, QA Gate, Release or Production.
