# Sprint 47 — Original class-scoped session source and operational workspace

References: Business draft B03 KY-LMS-01/02/04/05, B04 WF-02,
B05 AC-LMS-04/05/06; approved technical boundaries S12/S13/S14/S15/
S16/S35/S43–S46. This closes an operational gap: a mature center with
many classes cannot retrieve **all sessions of one class** by relying on
20-row global S12 source pages; an old session may be far away from the
first page even if staff know its original classId.

## Canonical read source (no derived store)
- **GET /api/v1/centers/{centerId}/classes/{classId}/source-sessions**
  with optional exclusive UUID `beforeSourceId` and hard limit 20,
  returns `{sourceKind:"CLASS_SESSION",classId,classTitle,
  records:[{sourceId,classId,title,classTitle,scheduledAt:null|UTC}],
  nextBeforeSourceId:null|UUID}`.
- Reads **only** original S12 `learning.center_classes` and
  `learning.class_sessions`, with fixed prepared SQL and no new
  schema/migration, materialized copy, source event or operation grant.
  Class must be genuine, same-center and visible through currently active
  center membership. A genuine class with zero sessions yields
  `records:[]`; missing, foreign or revoked class yields 404.
- Uses the existing **independently revocable S12
  `ICenterSourceCatalogReadAuthorization`**; the independent S46 class
  catalogue authorization cannot grant session listing. Signed OIDC
  subject, restricted read login verification, transaction-local RLS and
  active membership are checked afresh. No role/JWT-only override.
- Returns exactly source-owned, immutable IDs and original planned
  times (nullable), never an inferred attendance, delivery, count of
  classes actually held, consent, official KPI, source verification
  or enrollment decision. Cursor is descending UUID, 20+1 keyset, and
  cannot skip a class's earlier sessions because another class has newer
  IDs.

## Practical per-class center operations
- Within the **same** S43/S46 existing-class workspace, operators
  may open a genuine class found either via S46 original owner directory
  or S12 original session catalogue and explicitly request its
  class-scoped source list, independent of global catalogue pagination.
- Previously visible S12-source sessions remain usable immediately
  with their original ID, preserving older browser flows. Refresh from
  class-scoped canonical endpoint discards previously shown rows and
  replaces them with the complete class-only, properly paginated result.
- UI validates classId and immutable original classTitle, exact session
  identity, page bounds, monotone cursor, duplicate rejection and
  optional schedule. Forged unrelated sessions, malformed records,
  contradictory class owner, duplicate/replayed pages or unreadable
  source fail closed and clear previously displayed session actions.
- Per-session original S35 **private preparation** or S14 **UNVERIFIED
  human occurrence/attendance claim** can open only from a genuine S12
  session. Original S15 claim history and S16 report lineage are
  available solely through their own existing independent permissions.
  No UX source read can elevate any mutation grant.
- Center/actor switch remounts all private data and stale requests.
  The actual original S13 create-first/follow-up session and S18/S19
  REQUESTED/REVIEW_REQUIRED class intake remain intact.

## Verification and limits
C# denial/malformed/cursor tests; signed JWT administrator and
anonymous route denial; restricted real PostgreSQL integration creates
22 sessions in a class and 24 unrelated newer sessions in another
class, then checks exact 20+2 scoped pagination, legitimate
sessionless class, revoked/other-center cross-scope refusal,
missing class, unsafe DB credential and default-DENY grant.
Real Chromium asserts 22 sessions without global source reads,
empty genuine class, independent S12 refusal, forged class/title/
cursor rejection, S35 session pin and center-switch invalidation.
Full original backend, PostgreSQL, frontend, AI-contract and secret
gates remain applicable.

No class schedule correction, cancellation, session verification,
real person/guardian data, confirmed enrollment, published learning
content, finance, authorized national indicator or Production usage.
Do not infer missing enrollment or attendance as zero.

Draft/Open PR stacked on Sprint 46 only. Automated Code Review
COMMENT is not independent human approval; no Ready for Review,
Merge, Stage, QA Gate, Release Approval or nationwide Production
without explicit owner authorization.
