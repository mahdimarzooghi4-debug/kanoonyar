# Sprint 24 — Source-backed campaign report association (UNVERIFIED)

Business KY-CMP-03, AC-CMP-03, AC-X-01 and WF-04 require reuse of
source-owned records. This Sprint **associates** a previously submitted
typed activity report with an explicitly chosen, versioned planned
campaign activity. A report is neither observed execution nor accepted
participation; no counts, progress or success percentage are generated.

Contract:
- Input: existing campaign and **exact campaign revision**, its planned
  `activityKey`, and a real `reportId` from the existing
  `center_activity_reports` owner. The report must have a genuine
  `activity_report_source_bindings` typed `CLASS_SESSION` or
  `CENTER_EVENT` source and a genuine local plan issuance link;
  `draftId` and **pinned local plan revision** must match the campaign.
  Reports with legacy opaque origin or a mismatching plan are rejected.
- An immutable association is stored, not a copy of the report, session,
  event, evidence or attendee record. A report can appear only once
  within a campaign (including across revisions); no tally of actual
  execution or participants is made.
- POST
  `/api/v1/centers/{centerId}/campaigns/{campaignId}/report-links`
  (associationId, campaignRevision, activityKey, reportId).
  GET same route with optional exclusive `beforeAssociationId`.
  Read returns source-owned report ID/kind/source reference/claimed time/
  reported summary as *UNVERIFIED* along with exact campaign revision
  and activity key; max 20 per page. No public route.
- Separate `ICenterCampaignReportLinkAuthorization` per operation,
  plus independent `ICenterActivityReportReadAuthorization` for
  report content **on both read and association**. Both runtime
  implementations remain default DENY. Active membership, authenticated
  OIDC subject and restrictive SQL accounts required. No JWT role
  auto-grant or GUC trust.
- UI: select a campaign, load its legitimate source-plan reports through
  existing authorized `local-plan-drafts/{draftId}/activity-reports`
  endpoint, explicitly select an original report and a proposed planned
  activity key from the exact current campaign revision, then link.
  Historical read shows original report lineage with UNVERIFIED label.
  No form that creates a new report or counts it as delivery.
- FORCE RLS, same-center FKs for campaign/report/plan revision, immutable
  links and Audit/Outbox atomicity. Missing source/authority is fail
  closed. SQL/CI test data are synthetic. Campaign publication and
  legal/financial/participation/consent contracts remain unresolved.

No Ready for Review, merge, Stage, QA Gate, Release or Production. This
PR remains Draft/Open stacked on Sprint 23 and will need independent
human review.
