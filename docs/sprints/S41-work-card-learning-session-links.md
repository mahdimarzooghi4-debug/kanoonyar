# Sprint 41 — Explicit center-private class session to local work-card planning links

**Business references:** DRAFT B03 KY-LOC-04, KY-LMS-02, B04 WF-02/04.
Builds on S12 canonical center-owned classes/sessions, S33 source-pinned work
cards, S35 original educator preparations and S40 integrated local-plan workspace.
**This is an optional human planning reference**, not proof that the activity
is linked to the national program by policy, a real performance/attendance
record, an approved teaching plan, or an achieved outcome.

## Complete center workflow
1. Staff enter the existing S40 plan workspace; actual S33 cards belong
   to an exact immutable version of the original local plan.
2. Within an actual work card, staff load the paged S12 `CLASS_SESSION`
   source catalogue. They explicitly select a real same-center session
   and record a private link. Session/class/card content is never copied
   to a second shadow table and original owner records are never mutated.
3. The new immutable link is read with exact class/session titles and
   original scheduled-at (which may be NULL = unknown). Staff may open
   the existing original S35 session preparation editor; that editor
   retains its own source and write grants. No auto-assignment or
   automatic creation of educator material.
4. A duplicate link is a visible conflict, not a second success.
   Historical planning references are immutable (no fabricated
   cancellation/unlink lifecycle). A future removal/withdrawal action
   needs its own approved business/technical contract.

## API and invariants
POST /api/v1/centers/{centerId}/operations/work-cards/{workCardId}/session-links
body: `{sessionId}`. GET same path with optional exclusive
`beforeSessionId`: 20 per page, true owner session/class metadata.
All nested/root reply flags: PRIVATE_PLANNING, isPublished=false,
isExecuted=false, isAttendanceVerified=false, isDeliveryVerified=false.
Unknown schedule remains null. No claimed teaching result or attendance.

Database migration 0041 has exact composite same-center FKs to original
S33 work cards and S12 class sessions; immutable append-only trigger,
FORCE RLS on both source tables and link, restricted NOLOGIN SECURITY
DEFINER command owner, and atomic Link + Audit + Outbox.
Original source title/notes are NOT written into audit/outbox.
The function verifies same-center current active membership, exact
canonical source existence, and scoped transaction session; independent
writer has only function EXECUTE (no direct table modification).
Reader is original restricted SELECT-only role checked against
privileged roles/SET ROLE escalation, using REPEATABLE READ.

All operations require signed OIDC subject, active membership, and
four independent default-DENY rights:
WORK_CARD_SESSION_LINK_APPEND/LIST_READ, WORK_CARD_LIST_READ
for the exact source work card, LocalPlanDraftRead, and
CenterSourceCatalogRead. Neither a JWT admin role nor route-selected
center nor an RLS GUC is sufficient authorization.
GET checks true original card before listing, returns 404 for
foreign/nonexistent card and no foreign session data.
Frontend validates exact session catalogue IDs and pessimistic
private flags/cursors; it clears state on center/token changes.
User-controlled text uses only React escaped text nodes.

## Verification
Unit/HTTP OIDC, real disposable PostgreSQL center and foreign sources,
revoked active memberships, duplicate/racing insertion, source FKs,
immutability, exact Audit + Outbox, restricted writer/reader SQL roles,
21 distinct real source sessions + 20/1 keyset paging, invalid cursor,
and Chromium workflows for real selection, linkage, read, denial, forged
attendance/delivery and scoped reset.

## Deferred / not claimed
Publication, instructor entitlements, guardian consent, enrollment,
teaching delivery, attendance verification, actual resource allocation,
financial commitments, formal plan compliance and source-link withdrawal
remain outside this sprint. PR stays Draft/Open/unmerged;
independent human review/QA/Stage/Release/Production are not authorized.
