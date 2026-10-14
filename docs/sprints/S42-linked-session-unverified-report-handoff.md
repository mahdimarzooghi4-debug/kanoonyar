# Sprint 42 — From a real S41 linked class session to the original unverified report workflow

**Business:** B04 WF-02 (class), WF-03 (national issuance/local draft), WF-04
(local work card and evidence); these Business documents are still DRAFT.
This is a staff-private integration of original S10 issuance trace, original S17
unverified Sourced Activity Report command and S16 independently authorized
session report dossier. There is deliberately no new report table,
synthetic status or domain write surface.

## Usable end-to-end workflow
1. Staff load the S40 local-plan workspace for a genuine saved draft revision
   and S41's true original work-card/class-session link list.
2. Only within an actual linked session, the new source-aware panel can load
   the original S10 national issuance → local plan trace. It refuses to offer
   report submission if no issuance exists, or if its **exact immutable
   pinnedRevision**, national program or national version differs from S40's
   real original selected plan revision. Later edits must not silently
   substitute a different version.
3. Staff explicitly author a short plaintext reported summary and opaque
   evidence reference and submit **a human UNVERIFIED assertion** through
   the existing S17 `POST .../activity-reports/sourced` command, binding the
   true sourceKind=CLASS_SESSION, original linked session ID, original draftId
   and original immutable issuance linkId. `claimedOccurredAt=null` always;
   missing observed time remains unknown rather than invented.
4. S17 retains all existing independent application, SQL and actor/grant
   checks and source/pinned link FKs, along with original atomic report +
   source binding + Audit + Outbox. This UI does not grant extra authority,
   cannot verify reports and does not modify S17 contract.
5. A separate button opens the **original S16 independently authorized**
   session report dossier. That dossier can contain reports from other
   local plans; every report identifies its own authentic plan/issuance.
   Seeing the original dossier does not grant report write rights.

## Fail-closed semantics
- Original S41 source link, S40 immutable pinned version, S10 trace and S17
  response are checked for exact IDs, program, national version and literal
  UNVERIFIED. Invalid/forged VERIFIED acknowledgments never count as success.
- 401/403 on S10, S16 or S17 are independently visible and do not produce
  a private fallback or inferred data; center/token switches clear sessions,
  saved reports and historical sensitive state.
- UUIDs are canonical five-segment format. Only S17-issued command can
  write; front-end-generated reportId remains stable across ambiguous
  retries, and edits generate a new reportId.
- No derived attendance, completed class, verified evidence, KPI, approval,
  publication, official national-program compliance or funding/stock status.
- No private participant/child data is added to this workflow.

## Validation
Real Chromium scenarios: original linked session → exact issuance trace →
UNVERIFIED submission to actual S17 route → separate S16 dossier; missing
issuance and mismatched pinned version block writing, forged VERIFIED
acknowledgment is rejected, separate operation 403 and source report denial
cannot be bypassed; center-switch privacy reset.
All existing backend and PostgreSQL source-owner integrity, concurrency,
JWT and isolated-role tests remain active in the complete CI matrix.

No Production rights, human approval, Stage, QA Gate, release or merge.
PR stays Draft/Open stacked on S41, which also remains Draft/Open.
