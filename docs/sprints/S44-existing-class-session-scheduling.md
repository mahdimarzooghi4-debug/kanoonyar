# Sprint 44 — Schedule additional sessions for genuine existing classes

Business draft B03 KY-LMS-01, B04 WF-02 and the already implemented
S12/S13/S43 technical contracts govern this practical center workflow.
Previously the S13 UI scheduled sessions only for a newly created class
in the current browser session. The S43 workspace now permits existing
source-owned classes to plan their next genuine session without recreation.

## Source-backed human workflow
1. A center staff member independently reads the original S12 center
   CLASS_SESSION catalogue. S43 validates actual source sessionId,
   classId, classTitle, 20-row cursor order, duplicates, and title
   consistency. Source catalogue access remains a separate default-DENY grant.
2. The operator opens an original class discovered from those sessions.
   S13's **same existing** `CenterClassSessionPlanner` is reused in
   existing-class mode, pinned to its authentic classId and title.
   The new-class create action is absent and cannot run in this mode.
3. The operator writes a session title and optional proposed time.
   The original `POST /api/v1/centers/{centerId}/classes/sessions`
   receives a new random UUID sessionId, existing immutable classId,
   title and UTC scheduledAt or explicit null. A subsequent session has
   another UUID; it does not update earlier immutable sessions.
4. The backend's existing, independently revocable
   `CLASS_SESSION_PLAN_CREATE` write grant, active center membership,
   real PostgreSQL class ownership FK, audit/outbox, SQL role restrictions
   and FORCE RLS remain the only decision authorities. Reading a class
   source is **never** a grant to create a session.
5. The original S13 response must acknowledge exactly the proposed
   session UUID and class UUID, with a matching scheduled instant or null.
   A mismatched acknowledgement fails closed and never shows success.
   Center/actor changes remount all class and session state.
6. S43's S18/S19 capacity / REQUESTED intake / REVIEW_REQUIRED human
   review remains independently accessible alongside this new action.
   The operator can later reopen the real session from S12 once stored.

## Deliberate boundaries
- No new API, database table, schema, role, admission decision, identity,
  guardian/consent assertion, or shadow class/session registry.
- The S12 session catalogue does not show classes with **zero** sessions.
  This is not a complete standalone class directory; do not invent one.
- Scheduled is not delivered. No occurrence verification, attendance,
  classroom outcomes, consent, enrollment, capacity reservation,
  publication, rank or national compliance is asserted.
- Historic planned session updates and cancellation are not included:
  those require their own versioned Business/Technical contract.

## Evidence and gates
Real Chromium tests cover old S12 class -> multiple S13 session commands,
no duplicate class creation, independently denied S13 session write,
forged classId/scheduledAt acknowledgement, source read denial and
center-switch private-state clearance. Existing original S13/S43 suites
are preserved and full Backend/PostgreSQL/Frontend/JWT/AI/Security CI
continues to apply.

PR remains Draft/Open stacked on S43. Automated technical review is
COMMENT-only, not human approval. Business completion, independent
human review, Stage, QA Gate, release and Production for 80 centers
remain DENY_BY_DEFAULT until explicit owner authorization.
