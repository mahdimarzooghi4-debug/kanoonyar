# Sprint 37 — Integrated center-private assembled website preview

**Business references:** KY-WEB-01/02/03, S20 page drafts, S21 design
versions, S32 immutable navigation page pins. B03/B04/B05/T05 still DRAFT.
This is **a read-only preview within the authenticated center panel**;
it is not publication, public routing, external content access,
licensing approval or audience-visible delivery.

## Source-owned composition and version correctness
- GET `/api/v1/centers/{centerId}/site/assembled-preview`
  composes the saved latest *real* private design and navigation and
  the **exact source-owned page-draft revision IDs** pinned by each
  navigation item; no page text copied to a second persistent table.
- All composition is within the same PostgreSQL REPEATABLE READ
  transaction. Subsequent 20-item pages explicitly pin both the
  navigation and design revision and use ordered `afterPosition`
  (exclusive keyset); navigation can contain any number of pages.
  Existing immutable page references are followed, never silently
  substituted with newer page revisions.
- Missing design/navigation is explicitly `ready:false` with
  missing flags, never a fake default. An explicitly saved empty
  navigation remains valid but empty. Unknown historical versions
  return 404. The client rejects inconsistent cursors/lineage,
  published/delivered forged payloads and incorrect page order.
- Signed OIDC subject, live current membership, and **three separate
  approved grants** SITE_NAVIGATION_READ, SITE_DRAFT_LIST_READ and
  SITE_DESIGN_READ are mandatory. All effective grants default DENY.
  Query uses the verified restricted SELECT-only reader with the
  existing three owner-table FORCE RLS policies and checks for
  elevated writable/role-swappable principals. The requested
  center identifier or SQL session GUC is never authority.
- All responses and nested content are `PRIVATE_DRAFT`,
  `isPublished:false`, `deliveryVerified:false`.
  The frontend renders plain text via React text nodes (no
  dangerouslySetInnerHTML, Markdown HTML execution or outbound links).
  It presents palette, layout, header/footer, navigable pinned pages
  and version labels, and resets private state when credentials or
  selected center change. No synthetic actors or runtime records.
- Tests cover negative validation and signed role-only HTTP denial,
  disposable PostgreSQL read from genuine source-owner revisions
  including historical pinned content after editing a page, missing
  input, immutable explicit version reads, wrong center, revoked
  membership, restricted SQL role and independent grant denial, plus
  Chromium visible preview, missing-input, scope reset, forged delivery
  and denied permission.

**Blocked:** public publication, site host/domain, moderation,
legal content rights, workflow approvals, accessibility/performance
production standards, caching and real rollout. Draft/Open PR only,
no human Code Review approval, merge, Stage, QA Gate or Production.
