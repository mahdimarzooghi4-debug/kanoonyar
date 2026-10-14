# Sprint 46 — Canonical center class catalogue, including sessionless classes

Business draft B03 KY-LMS-01/02/03, B04 WF-02, B05 AC-LMS-01/05/06.
The immediate operational gap left by S43–S45 is that discovering classes
through S12 class-session sources omits genuine owner classes which have
not yet scheduled their very first session. A class already exists in
`learning.center_classes`; creating a duplicate class is not a remedy.

## Original-source read contract
- Introduce **GET /api/v1/centers/{centerId}/classes/catalogue** as an
  explicitly separate class identity *read*, not an activity-source
  delivery record. It reads directly from the original S12
  `learning.center_classes`, with no join to session records.
- Response: `{classes:[{classId:UUID,title:string}], nextBeforeClassId:UUID|null}`.
  Deterministic immutable UUID descending keyset pagination, at most
  20 items per page, exclusive `beforeClassId` cursor. No fabricated
  enrollment, session count, status, attendance or other inferred values.
- New independent `ICenterClassCatalogueAuthorization` contract with
  runtime **UnapprovedCenterClassCatalogueAuthorization = DENY**.
  No hardcoded role/admin bypass or Production grant provisioned.
  Signed subject and live active same-center membership are required.
- Read through existing restricted `CENTER_DRAFT_READ_DATABASE_URL`
  with SQL credential safety verification, transaction-local center
  RLS, membership subquery and a fixed prepared SQL statement. The
  original owner table, triggers, RLS and grants are unchanged; this
  is read-only. Other-center/revoked users observe no owner rows.
- The S12 class-session source catalogue grant is separate from this
  new class-owner directory grant. One does not authorize the other,
  S13 planning writes, S18 capacity/intake, S19 human review, S14
  occurrence claims or S35 educator preparation.

## Usable integrated center experience
- The S43 existing-class center workspace can retrieve the complete
  canonical class owner catalogue separately from its older S12 session
  discovery mode; both routes remain available and distinct.
- Classes are deduplicated by immutable classId, fail-closed on forged
  duplicate identities, title conflicts, malformed cursor/ordering or
  untrusted response. A missing class session is not a missing class.
- A real owner class with zero sessions opens the original S13 session
  planner to create its **first planned session** without creating a
  second class. Original S18 capacity and immutable REQUESTED-only
  registration-intent UI also remains available with independent grants.
- Per-session S45 preparations/claims are visible **only** for actual
  S12-discovered sessions with separately granted source access; new
  class catalogue data never becomes synthetic session source evidence.
- Re-selecting a different center/actor re-mounts the complete private
  workspace and invalidates prior session/class selections.
- Once the user has saved the first session via original S13, the
  independent S12 catalogue can be refreshed to discover the
  actual persisted session.

## Verification and non-goals
New backend tests exercise authentication, bad cursor, default denial,
failing grant, missing DB role and a restricted real PostgreSQL
isolated center with 22 sessionless classes, 20+2 paging, cross-center
and revoked-subject exclusion and unsafe SQL-login refusal.
Signed OIDC HTTP tests cover anonymous and signed administrator
without the separate class catalogue grant. New real Chromium tests
cover sessionless class -> S13 first planned session -> S18 source
capacity, page 2, independent denial, forged cursor and center switch.
All previous backend/PostgreSQL/Frontend/JWT/AI and secret checks
remain in the full CI.

No new database schema/migration/table, ownership copy, policy,
confirmed enrollment, seat reservation, age/guardian/consent decision,
learning delivery, actual attendance, public publication, grading,
certificate, national KPI or operational financial contract.

PR remains Draft/Open stacked on PR #45. A green technical CI or
automated COMMENT never equals independent human approval.
Stage/QA Gate/Release/Production and official 80-center launch are
DENY_BY_DEFAULT until explicit owner authorization and approved policy.
