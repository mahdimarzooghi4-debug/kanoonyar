# Sprint 26 — Source-inspection clarification requests and responses

Grounded in B04 WF-05 human feedback/follow-up and B05 AC-EVL-02/03.
This is **not** an approval process, task assignment, deadline, verified
execution, accepted attendance or a dispute-resolution decision.
Authorities, reviewer qualifications, closure rights, retention and
notification rules remain unapproved, hence intentionally absent.

A human with independent campaign inspection FOLLOWUP_APPEND and
source-report READ grants may request clarification about an existing
immutable S25 inspection note. An independently authorized human can
append a response to the exact original request. Each request/response
pins center, campaign, existing report association and note; changing
these lineages or responding to another campaign's question is
impossible even through direct elevated DB writes (composite FKs).
A request has no automatically assigned person, deadline, state or
implicit resolution. Responses do not automatically close requests.

Write:
 POST /api/v1/centers/{centerId}/campaigns/{campaignId}/report-links/{associationId}/inspection-notes/{noteId}/followups
 {entryId, entryKind: REQUEST, content} or
 {entryId, entryKind: RESPONSE, requestId, content}.
Read:
 GET same route, type=requests&beforeEntryId=<uuid>;
 GET same route, type=responses&requestId=<uuid>&beforeEntryId=<uuid>.
Both read types are keyset-paginated with 20 items/page, with no
aggregate counts or status transitions. All replies retain
UNVERIFIED/isApproval=false/isExecuted=false. The response read path
checks original request belongs to note + campaign before returning
any result, including empty results.

Operation-specific independent campaign report link authorization
plus separately required original report read permission, signed OIDC
subject and real live membership are mandatory for every operation.
Runtime grant remains DENY, never role claim or SQL GUC alone.
Restricted NOLOGIN SECURITY DEFINER writer; EXECUTE-only API account
and read-only API account; FORCE RLS, immutable source-bound tables and
audit/outbox in the same transaction. Audit excludes question/response
free text and any participant data.

Center UI: choose an existing inspection note and append a clarification
question or response for a specifically selected request. Show
request/response lineage and explicit UNVERIFIED labels; fail closed
on forged approval/status, wrong center or missing authority.
No public pages, push notifications, participant consent assumptions,
financial actions, outcomes, KPIs, approvals or source record changes.

Contract -> tests -> code -> CI -> technical COMMENT review.
PR Draft/Open stacked on #25; no merge/Stage/QA/Release/Production.