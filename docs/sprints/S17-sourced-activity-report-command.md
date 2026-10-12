# Sprint 17 — Atomic Source-linked Activity Report Submission

## Contract
Complete the existing read-only report provenance flow by allowing a separately
authorized operator to **submit an unverified activity assertion**, bound to
one immutable center-owned class session or event and the exact pinned
national issuance / local draft link.

`POST /api/v1/centers/{centerId}/activity-reports/sourced` with:
`reportId` (client-stable UUID), `draftId`, `linkId`, `sourceKind`
(`CLASS_SESSION` / `CENTER_EVENT`), `sourceId`, `evidenceRef` (opaque,
required), `reportedSummary`, `claimedOccurredAt` (nullable explicit UTC).
Successful storage returns the exact IDs and literal `UNVERIFIED`.

This is not an accepted event, approved evidence object, verified delivered
session, attendance record, enrollment, KPI, compliance judgement or financial event.

## Implementation
- Migration 0019: SECURITY DEFINER command owned by a NOLOGIN, NOBYPASSRLS,
  limited owner, delegating to the existing owner-source report/binding writer.
  Inserts report + binding + audit + outbox atomically in one transaction.
  Checks current center GUC, live active membership and exact center's
  immutable source and pinned plan link. GUC/SQL actor are NOT authentication.
  DB FK, RLS and duplicate evidence/ID checks fail closed.
- New separate operation-specific authorization
  `ICenterSourcedActivityReportWriteAuthorization`; application binds only
  `UnapprovedCenterSourcedActivityReportWriteAuthorization` (always DENY).
  No role code/JWT membership automatically permits report submission.
- Dedicated SQL connection `CENTER_ACTIVITY_REPORT_WRITE_DATABASE_URL`
  verified for audited EXECUTE-only privileges; distinct CI-only restricted
  login, no Production grants or credential values.
- Center React trace view uses the existing separately authorized source catalog,
  explicit session/event choice and pinned link/draft from trace; user submits
  summary and opaque evidence reference. Response must match exact IDs and
  `UNVERIFIED`; stale/denied responses and center/identity switches never
  preserve protected state. Stable reportId on ambiguous retry; conflict is
  shown and no automatic duplicate submission.
- Existing S11 report reader and S16 session dossier consume the new rows
  through owner tables; no replicated source store or schema fake.

## Acceptance / guardrails
Atomic consistency across reports, bindings, audit and outbox. Unsupported
kind, absent/mismatched link, unknown/cross-center source, repeated evidence,
duplicate report ID, revoked/cross-center actor and audit collision fail
without partial changes. SQL credential cannot INSERT or SET ROLE to owner.
API unit and restricted PostgreSQL tests plus Playwright test create, denial,
forged VERIFIED acknowledgement and state switch. Actual operation grants
remain default DENY; all user data/evidence is synthetic in CI.
No Ready for Review, Merge, Stage, QA Gate, Release or Production.
