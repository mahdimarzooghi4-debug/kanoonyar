# Sprint 48 — Genuine private calendar to event evidence and session claim handoff

Source: Business B03 KY-EVT-01/03, KY-LMS-04, KY-LOC-03,
B04 WF-02/04/05, B05 AC-LMS-04/05/06, AC-X-01 and established
S12/S14/S15/S16/S17/S29/S30/S31 contracts. This is one integrated
existing-source **operator workflow**, not another event or report store.

## Operator workflow

- The center operator reads the original S31 private calendar with a
  declared, half-open UTC range, retaining its original independent
  `CENTER_PRIVATE_CALENDAR_READ` and S12 class-source grants,
  live OIDC subject, center membership, RLS and restricted read login.
  The calendar returns exactly canonical scheduled class sessions
  and the latest genuine S29 event-plan revisions. Missing schedule
  is unknown; records in the window are PRIVATE_PLAN, not delivered.
- Selecting an actual **CENTER_EVENT** opens the original
  `CenterEventSourceDossier` (S30) using the *same* sourceId,
  immutable local-plan draftId and pinned revision, exact event
  planRevision, source-owned title and current planned UTC instant.
  Its original S30 source-dossier endpoint performs its **separate**
  `EVENT_SOURCE_DOSSIER_READ` plus S11 activity-report read grants,
  real source/report FK+lineage and center isolation. A stale plan,
  changed prospective schedule, denied grant or forged verified report
  fails closed. S30 returns only UNVERIFIED claims linked to the
  exact original S29 pinned plan, not counted event completions.
- Selecting an actual **CLASS_SESSION** opens the original
  `SessionClaimReporter` (S14) pinned to that exact S12 session UUID.
  Original S14 write grants (occurrence vs attendee claim),
  S15 history rights and S16 lineage rights remain separate,
  revocable and default DENY. A calendar read never grants a
  write permission. A submitted observation is solely a human
  UNVERIFIED occurrence or pseudonymous reported presence;
  not verified attendance, learning outcome, enrollment or consent.
- Only **one** calendar-source operation can be open at once,
  keyed by canonical source kind+UUID; changing the UTC window,
  refreshing the calendar (including pagination), a denied/forged
  schedule response, or switching center/token clears the
  protected operation view. A calendar item cannot instantiate
  an S30 dossier for a class or an S14 session claim for an event.
  No cross-source or unapproved title/draft/plan revision guessing.

## Bounded engineering and proof

No new SQL migration, schema, role, API, parallel report, event/attendance
store or invented national reporting metrics; S31 API and all original
source/report mutation contracts are unchanged. This connects genuine
working tools into the private authenticated center workspace.

New actual Chromium tests exercise:
- S31 original class session and latest-version S29 event in the
  same date window → exact canonical S30 UNVERIFIED dossier, then
  exact S14 original occurrence claim via existing endpoint/JSON;
- independently denied S30 read and S14 write, despite an otherwise
  allowed calendar read; no implicit grant inheritance;
- forged approved/VERIFIED report and stale S29 plan revision after
  calendar read cannot become accepted evidence;
- changing UTC date window, rereading with permission revoked, and
  center switch remove stale event/session operator workspaces.
Existing S14/S15/S16/S29/S30/S31 frontend and Backend/real PostgreSQL
integration suites must also pass through full CI.

## Explicit non-goals / approval boundary

No public calendar, event publicity, signup, capacity/admission,
real-person/guardian data, participant consents, class/event execution
verification, official attendance or program outcome, event settlement,
payment, donation, national KPI or production reporting. All Business
policies for those operations remain open; no AI may fill missing
evidence, infer delivery or make a human decision.

PR #48 remains Draft/Open stacked on Sprint 47. Automated technical
COMMENT is not independent human Code Review or Business approval.
No Ready for Review, Merge, Stage, QA Gate, Release Approval or
Production across the 80 centers without explicit owner instruction.
