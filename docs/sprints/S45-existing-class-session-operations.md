# Sprint 45 — Existing-class session teaching and evidence operations

**Business references:** B03 KY-LMS-01/02/04/05, B04 WF-02,
B05 AC-LMS-04/05/06. Business remains DRAFT. This sprint is a
usable integration of authentic S12/S14/S15/S16/S35/S43/S44 paths,
not another session/attendance data store.

## Human center workflow
1. Through S43's independent S12 original CLASS_SESSION source catalogue,
   the operator discovers same-center existing classes (source-backed
   classId/title). S12 20-row exclusive cursor, source identifiers,
   ordering, uniqueness and stable class title remain validated.
2. Under a selected class, the operator opens its **real** existing
   sessions, only among already retrieved pages. Each original session
   remains pinned to its genuine sourceId, classId and scheduledAt
   (nullable). This is a partial browser view, **not a full class
   timetable** until all pages are loaded.
3. For that exact session the operator can open the **original S35
   CenterSessionPreparations**. An optional pinned source session
   constrains the editor to the selected canonical session, preserves
   original CAS revision, immutable history, Audit/Outbox and restricted
   `SESSION_PREPARATION_APPEND/LIST_READ/HISTORY_READ` grants.
   A pinned source cannot be replaced by a different session merely
   by changing a form. Original preparation listing is filtered
   client-side to this session; its server read grant is independent.
4. Alternatively the same session can open **original S14
   SessionClaimReporter**, including its separately permitted S15
   history and S16 report lineage. Any occurrence or participant
   attendance remains a human **UNVERIFIED** claim, not proof of
   delivery or verified attendance. Original independent operation
   grants remain default-DENY at the server.
5. Center/actor changes and refresh of S12 source results drop all
   opened teaching/reporting tools and private contextual selections.
   A class source read gives no authority to append a preparation,
   occurrence, attendance or report. S18/S19 intake, S13 scheduled
   sessions and S40/S41 work-card sessions remain separately available.

## Explicitly unresolved
No new API, SQL table, grant, policy, participant account, identity,
guardian authority, consent, admission, verified attendance, real
person reference, grade, published teaching content, certificate,
count, national quality KPI or compliance claim. Participant opaque
references must not be used as real identities before an authorized
real consent/identity source exists. No inferential zero where
attendance is missing.

## Verification and governance
New real Chromium positive flow covers original class and exact
session to separately authorized S35 private preparation and S14
UNVERIFIED human occurrence with original API/body, plus denied
grants and center-switch invalidation. Existing S12/S13/S14/S15/S16/
S35/S43/S44 tests and all original backend/PostgreSQL/AI/secret
gates continue to run.

PR is Draft/Open, stacked on PR #44. Automated technical review is
COMMENT only and not human authorization. No Ready for Review, merge,
Stage, QA Gate, Release Approval or Production. Nationwide operation
for all 80 centers remains DENY_BY_DEFAULT.
