# Sprint 28 — Center campaign source register (private, unverified)

The verified baseline S23–S27 covers private draft campaign, real typed
source activity report associations, human notes, clarification questions
and answers, and a single-source review workspace. Approved B04 WF-04
and WF-05 / AC-X-01 require traceable reusable source records and
separation between source claims and accepted outcomes.

This sprint provides a **read-only per-campaign navigation register**.
It is neither a national/staff dashboard nor an approved indicator,
performance ranking, work queue, case disposition or proof of activity.
National central staff observation and metrics require separately approved
cross-center grants and indicator contracts; none are invented.

GET /api/v1/centers/{centerId}/campaigns/{campaignId}/evidence-register
  ?beforeAssociationId=uuid&sourceKind=CLASS_SESSION|CENTER_EVENT&activityKey=key

Optional source-kind and planned activity key filters operate over real
owner data. UUID-exclusive page cursor returns at most 20 associations.
Each row has original source owner report and typed lineage,
exact issued plan revision and pinned campaign revision/activity key,
and **only the metadata** of the most recently recorded original human
NOTE, REQUEST or RESPONSE: event type, entry UUID and timestamp.
No copied note/question/response text in the register. No inferred
open/closed state, count, attendance, participation, success or action
required. The user can explicitly open the S27 detailed review workspace
for the original association. `isPageEnd` indicates *only* pagination.

Security: signed OIDC subject, current active membership in the center,
all four separately authorized operations CAMPAIGN_EVIDENCE_REGISTER_READ,
CAMPAIGN_SOURCE_REPORT_READ, CAMPAIGN_REPORT_INSPECTION_READ and
CAMPAIGN_INSPECTION_FOLLOWUP_READ and original report source-owned READ.
Default DENY, no membership/JWT role-only authorization. Reuses the
existing restricted read-only PostgreSQL credential and attests all
three source read roles. A single repeatable-read snapshot checks the
exact campaign/source typed binding and pinned plan revision. Missing
or cross-center records 404; no all-centers query or public route.

Frontend: private register for one selected campaign, filters only on
original known types or a planned activity, source-lineage rows,
latest human activity metadata with explicit UNVERIFIED badges in plain
text, paginated history and direct opening of S27 source workspace.
Scope reset on token, center or campaign change; rejects forged
VERIFIED/APPROVED/EXECUTED/RESOLVED, incompatible source/pins and
invalid cursor/order.

Evidence: Backend permission/cursor unit tests; signed admin JWT role
denial; genuine source-linked disposable PostgreSQL integration with
multiple typed reports and review history, restricted reader, foreign
center/revoked user and filter/cursor; Chromium report register, forced
verification rejection and scope change. No new persistence or data
mutation. No Stage, QA, merge or Production; PR remains Draft/Open
stacked on Sprint 27 until independent human review and explicit merge
authorization.
