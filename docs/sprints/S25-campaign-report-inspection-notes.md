# Sprint 25 — Human Inspection Notes for Campaign Source Reports

Business references: B04 WF-05 human qualitative review and feedback,
B05 AC-EVL-02/03, B05A AC-CMP-03 and AC-X-01. Review status
transitions, thresholds, verifier qualifications and rights remain
UNDECIDED; no `VERIFIED`, `APPROVED`, delivered count or outcome.

A separately authorized human may append **plain text inspection notes**
to an existing immutable campaign-report association from S24. The
report, exact pinned plan, campaign revision, activity key, original
source, claimed event, evidence and verification status remain unchanged.
The note is neither evidence verification nor quality judgement. It
is a provenance-linked annotation with a named OIDC human actor.

Write: POST
`/api/v1/centers/{centerId}/campaigns/{campaignId}/report-links/{associationId}/inspection-notes`
with `noteId` (UUID) and `noteText` (1–1000 trimmed chars).
Read: GET same path with optional exclusive UUID `beforeNoteId`,
max 20 per page. Both always label notes
`verificationStatus: UNVERIFIED`, `isApproval: false`,
`isExecuted: false`.

The write must require BOTH independent
`ICenterCampaignReportLinkAuthorization` with operation
`CAMPAIGN_REPORT_INSPECTION_APPEND` and the existing separately
authorized `ICenterActivityReportReadAuthorization`. Read must
require the analogous `CAMPAIGN_REPORT_INSPECTION_READ` and
source report read permission; all runtime authorization remains DENY.
Live active membership, independently authenticated OIDC subject,
restricted NOLOGIN SECURITY DEFINER writer + EXECUTE-only login,
separate read-only login, FORCE RLS and immutable same-center FKs.
Atomic Audit+Outbox only store note/association IDs and explicit
UNVERIFIED/non-approval flags, never free text.

The UI renders notes only in the private campaign-report workspace
with an explicit privacy warning (do not enter participant PII).
No public access, no report mutation, no counts, no admin grant from JWT
roles, no AI review, no automatic human verification.

SQL integrity tests, restricted PostgreSQL handler tests,
signed role-only JWT denial and Chromium read/write/denial/scope reset
must pass. Keep Draft/Open PR stacked on #24, no merge/Stage/QA/Release
or Production. Independent human review remains required.