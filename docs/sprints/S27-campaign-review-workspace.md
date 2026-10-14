# Sprint 27 — Source-linked campaign inspection workspace

B04 WF-05 / AC-X-01: a real **read-only**, source-owned inspection
workspace per private campaign report association. Shows the original typed
activity report and its exact pinned local-plan revision, together with the
actual human inspection notes, clarification questions and responses as a
single deterministic, descending time/entry-kind/UUID timeline. No copies,
new status, work assignment, synthetic counts, KPI, completion, closure,
approval, source verification or report execution are created.

GET /api/v1/centers/{centerId}/campaigns/{campaignId}/report-links/{associationId}/review-workspace
query cursor: `beforeAt` (offset-aware timestamp), `beforeKind` (NOTE,
REQUEST, RESPONSE), `beforeId` (UUID) must be supplied together. Return
20 entries/page max, cursor taken from last returned row; `isComplete`
is only a pagination indicator, **never** evidence completeness.
The projection binds S24 source report, its typed source binding, issuance
link, S23 pinned local plan and activity key, S25 notes, S26 request/response.
The query uses one read-only repeatable-read snapshot; no write side effect,
no inferred attendance, event occurrence, successful campaign or closure.

Backend requires OIDC subject, live membership, exact resource/operation
read grants for campaign report, human inspections AND human followups plus
independent original report read. All grants default DENY. Restricted SQL
read-only preflight covers all owning tables and prohibited command
EXECUTE/mutation privileges. Scope GUC is defense in depth, not a
substitute for grants. Foreign, missing, stale or unowned sources produce
privacy-preserving not found, not fabricated empty success.

Center UI embeds a collapsible source review panel on **each existing**
authorized association and renders plain-text, provenance-linked entries
with explicit UNVERIFIED/isApproval=false/isExecuted=false/isResolved=false.
Rejects forged VERIFIED/APPROVED/EXECUTED/RESOLVED response, incorrect
source identifiers, broken sort/cursor or leaked cross-center data;
resets protected state on campaign, center, token or source switch.
Existing scoped note and followup mutation controls remain separate.

Tests must cover genuine PostgreSQL source lineage and mixed timeline,
cursor page boundaries and ties, role-only JWT denial, revoked member,
wrong association/campaign, credential privilege boundaries, Chromium
source view/cursor/forged decision/scope-switch. Full Push+PR CI and
technical COMMENT review required. Keep PR Draft/Open stacked on #26.
No Stage/QA Gate/Release/Production or independent human approval.