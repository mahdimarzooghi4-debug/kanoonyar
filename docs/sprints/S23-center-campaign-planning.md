# Sprint 23 — Center Campaign Planning, Private Versioned Drafts

Business scope: KY-CMP-01 (campaign, objective, audience and related
program), AC-CMP-01/02/04, KY-LOC-04, AC-X-01. This is a campaign
*planning workspace*, not public launch, participation accounting or
verified execution.

- Each center owns campaign UUID and stable slug. Creating a campaign
  **requires an actual center-owned local-plan draft ID and exact
  immutable plan revision** (same-center relational FK). An existing
  national issuance is not inferred; a local plan is not approved or
  executed merely because it is linked. A linked campaign revision
  cannot change its pinned plan identity.
- Planned objectives, aggregate audience *description* and planned
  activity labels only. A planned activity is an object with exactly
  `activityKey` and `title`. One to twelve distinct keys per revision;
  no observed counts, attendance, fundraising or completed status.
- Three authenticated API routes: POST
  `/api/v1/centers/{centerId}/campaigns/drafts/revisions`;
  GET `/api/v1/centers/{centerId}/campaigns/drafts` (20, UUID keyset);
  GET `/api/v1/centers/{centerId}/campaigns/drafts/{campaignId}/revisions`
  (20, revision cursor). All replies visibly indicate
  `PRIVATE_DRAFT`, `isPublished=false`, `isExecuted=false`.
- Operation-specific `ICenterCampaignAuthorization` defaults DENY;
  JWT role or center membership alone is never an operational grant.
  Each operation also verifies live membership. A restricted EXECUTE
  writer and strictly-read-only reader are validated before use.
  FORCE RLS, immutable rows, advisory-locked CAS, same-center FK and
  atomic Audit/Outbox are mandatory. Audit excludes campaign content,
  field labels and audience description.
- In the center workspace, a user selects a **real local-plan draft**
  using its existing authorized local-plan read endpoint, designs
  campaign and activities, revises it, browses history and views a
  private planning summary. No public campaign route or public form.
- SQL/handler/Chromium CI uses synthetic center data. No real payment,
  donor, beneficiary identity, publication rights, consent, status
  transition, actual execution or participation aggregation exists.
  Those require independently approved contracts and source records.

Keep the Sprint 23 PR Draft/Open/Unmerged pending independent human
Code Review. No Stage, QA Gate, Release or Production without explicit
authorization. Green CI alone is not human approval.
