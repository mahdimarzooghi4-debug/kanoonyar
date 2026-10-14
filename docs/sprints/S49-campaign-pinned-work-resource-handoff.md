# Sprint 49 — Genuine private campaign → pinned local-plan work/resource workflow

Business DRAFT: B03 KY-CMP-01 and KY-LOC-02/03/04, B04 WF-04,
B05A AC-CMP-01/03/04, and source-owned S23/S33/S39/S40 contracts.

## Operational gap and delivery

A genuine S23 planned campaign already pins the immutable center-local
plan identity `(draftId, pinnedPlanRevision)`. The original S40
integrated local-plan workspace exposes actual same-version S33
work cards and S39 private resource-needs through independently
authorized owner records. Previously staff had to exit the campaign,
load an all-plan list again, manually reselect the correct version,
and could accidentally inspect a different plan revision.

Sprint 49 directly opens the **existing S40 CenterLocalPlanWorkspace**
from a selected authentic S23 campaign. Its new optional `pinnedPlan`
prop accepts only the saved S23 source `draftId` and exact
`pinnedPlanRevision`, hides manual alternate-plan selection, and calls
the original `GET /api/v1/centers/{centerId}/operations/local-plan-workspace/{draftId}/{planRevision}`.
The original S40 server fully rechecks active center membership, source
plan + exact revision, independent LocalPlanDraftRead,
WORK_CARD_LIST_READ and WORK_RESOURCE_NEED_LIST_READ grants, per-card
rights, original RLS/read-only SQL and source-owned nested data.
S23 campaign listing or campaign authoring permission **never
authorizes** S40 or S39 reads/writes.

From a genuinely visible source work card the existing original S39
resource-need editor can be opened; its APPEND, READ and HISTORY
permissions remain independently default DENY. No inferred procurement,
inventory, financial balance, supplier, allocation, purchase,
fulfillment, assigned worker, execution, campaign participant,
accepted activity, verified evidence or central KPI is introduced.

The general S40 workspace remains unchanged for standalone use:
authorized staff may separately select an actual local plan and its
version. Campaign-scoped mode can neither choose an alternate local
plan, fall back to a newer revision, nor fabricate missing plan
attributes. User must explicitly open the drilldown; selecting another
campaign, reloading genuine campaign sources, center/actor changes
or closing the drilldown unmount its private state. No new schema,
API, role, persistence, record mapping or shadow aggregate.

## Evidence and acceptance

New real Chromium cases cover:
1. S23 private real source campaign → original S40 endpoint with exact
   immutable pinned local plan identity and original S33 owner work
   card with S39 private needs; original S39 editor reads genuine need.
   No global local-plan dropdown read required.
2. Permitted S23 campaign read but independently denied S40 aggregate
   access never reveals a work card/need.
3. Forged cross-plan `sourcePlan.draftId` and forged purchased S39
   nested state fail closed with no output.
4. Selecting a different real campaign/version closes previous
   workspace, prevents silent plan substitution; switching centers
   drops all protected data.
Existing S23/S33/S39/S40 backend authorization, PostgreSQL integrity,
and original Chromium suites remain the source-of-truth tests and
must all pass unchanged in full PR CI.

## Deferred / explicit governance boundaries

Campaign publication, legally valid donation/payment, purchased or
allocated resources, real identities and consent, real task
assignment, executed local program, attendance, campaign outcome,
fundraising totals, national KPI and public performance reports
require separately approved authoritative Business/Technical/Finance
contracts. UNKNOWN stays UNKNOWN and PRIVATE_PLANNING is not
real-world fulfillment.

Keep Sprint 49 PR Draft/Open stacked on Sprint 48. An automated
technical COMMENT or green CI is not independent human Code Review,
Stage Admission, QA/Release approval or Production authorization.
No Ready for Review, merge, Stage, QA Gate, Release or nationwide
launch for 80 centers without the owner's explicit instruction.
