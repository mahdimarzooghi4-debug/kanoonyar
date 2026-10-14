# Sprint 43 — Existing-class registration requests and human-review workspace

Business drafts B03 KY-LMS-03/KY-AUD-03, B04 WF-02 and S18/S19 contracts govern this
slice. Staff need to work with **previously created** classes, not only an S13
class created in their present browser session. This is a practical
owner-preserving integration, not a new enrollment subsystem.

## Operational workflow
1. A center staff member can browse actual same-center S12
   `CLASS_SESSION` source records using the independently authorized
   canonical source catalogue. The view groups sessions by their
   original immutable classId and reads the original classTitle.
   It does not claim to enumerate classes with **no** recorded sessions.
   Source pagination is 20 true sessions, with an exclusive sourceId cursor.
   It deduplicates classId across pages, checks conflicting source titles,
   repeated session IDs and forged pagination.
2. Staff choose an original discovered class and open the **existing S18
   ClassCapacityIntake** component, using its original independent
   `CLASS_INTAKE_READ`, `CLASS_CAPACITY_REVISE`,
   `CLASS_REGISTRATION_INTENT_SUBMIT` permissions and original SQL tables.
   Plan capacity revisions and opaque UUID-only applicant request intents,
   with no confirmed enrollment or capacity reservation.
3. Existing, immutable S18 `REQUESTED` entries can open the existing S19
   RegistrationReviewRequirements component with original independent
   `REGISTRATION_REVIEW_REQUIREMENT_READ/RAISE` permissions and source
   SQL. The four topic values are **questions**, never resolved proof:
   IDENTITY_AUTHORITY, ELIGIBILITY_CRITERIA, CONSENT_AUTHORITY,
   ADMISSION_POLICY. No approve/reject, placement or consent assertion.
4. In the S40→S41 linked-session card view, distinct source-owned classIds
   from genuinely linked sessions also open the same S18/S19 workflow.
   Viewing a private planning link does NOT bestow enrollment, capacity,
   guardian, eligibility or review-operation rights; the original API
   always checks each independent grant.
5. Center/actor switching remounts all original private data, stale
   requests and selected reviews. For malformed response data claiming
   confirmedEnrollmentCount or APPROVED/reviewed consent, the original
   S18/S19 strict UI validators fail closed.

## No new domain authority
No database migration, new API, new operation grant, shadow class/request
store, new participant PII, synthetic enrollment evidence, invented
admission criteria, threshold, priority or seat count. Existing S12, S18,
S19 and S41 contracts remain authoritative. If a class has no session,
the source-session discovery view won't show it; direct source-owned class
management via a future independent class catalogue may be designed
when its contract is approved.

## Evidence
Real Chromium: previously saved source class → versioned capacity → request
REQUESTED → independent review topic REVIEW_REQUIRED; source-linked S41
class follows same original S18 path; forged confirmedEnrollmentCount and
fake legal consent/admission decisions are rejected; source/operation grant
denial and center switch clear private state; 21-session/2-page source
pagination and class deduplication. All backend, restricted PostgreSQL,
JWT, AI-contract and secrets gates of earlier sprints remain active.

No Stage, QA Gate, human approval, Merge, Release or Production.
PR remains Draft/Open stacked on S42; official 80-center release is
DENY_BY_DEFAULT.
