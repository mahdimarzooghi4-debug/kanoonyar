# Sprint 38 — Private center-local reusable page template library

Business grounding: B03 KY-WEB-04 reusable content/templates and KY-WEB-03
source page drafts; S20 private page identities/versions; S37 private
integrated preview. Source material and Business/Technical rights remain
**DRAFT**. This is only same-center private reuse, not national/public
template distribution, licensing, publication, audience data, moderation
or shared proprietary material.

## Real workflow
1. Staff with separately approved rights select an actually persisted S20
   same-center page source and its immutable positive revision.
2. Staff explicitly name a PRIVATE_TEMPLATE. Each template ID has an
   immutable original source-page identity and append-only revisions of
   (label, pinned source revision). Changing label/pin requires the exact
   optimistic template revision, is auditable, and never rewrites old pins.
3. GET returns at most 20 source-derived local templates with exact
   original title/body/slug from the pinned page version, within one
   REPEATABLE READ transaction; GET history returns immutable historical
   pinned revisions (20/page).
4. Staff may explicitly reuse a selected template's text in a new,
   editable *private* page with a new slug. This calls the existing
   S20 POST /site/drafts/revisions with expectedRevision=0, and is
   authorized **independently** by SITE_DRAFT_APPEND. A template write
   grant does not confer page creation. The resulting page does not
   falsely claim a persisted derivation link, formal approval or a
   content license; the origin template itself stays unchanged.

## Security / consistency
- Migration 0038: composite FKs source page & exact revision, immutable
  template identity, immutable append-only template revisions, FORCE RLS,
  NOLOGIN restricted SECURITY DEFINER command actor validating current
  active center membership and same-center source, per-template advisory
  lock and CAS, atomic Audit + Outbox without source body text.
- Verified OIDC sub, independently scoped operation-specific grants
  PRIVATE_PAGE_TEMPLATE_APPEND / LIST_READ / HISTORY_READ and
  SITE_DRAFT_LIST_READ, all default DENY. Restricted SQL writer has only
  EXECUTE, reader has only SELECT, denies SET ROLE/elevated credentials.
  No cross-center read or source revision fallback. Data is bounded plain
  text rendered only through React escaped text nodes.
- All template responses carry PRIVATE_TEMPLATE, isPublished=false,
  isShared=false. New-page acknowledgement remains PRIVATE_DRAFT,
  isPublished=false. Client rejects forged public/sharing statuses,
  unrelated source, unexpected versions and invalid keyset cursors.
  Center and token changes clear component state.
- Unit, signed-JWT role-only 403, disposable PostgreSQL same-center
  source pins, foreign/missing versions, revocation, concurrent CAS,
  historical content after editing source, RLS, audit/outbox count and
  restricted credential tests. Real Chromium positive reuse with separate
  independent page-creation grant, denial and forged statuses.

## Unresolved / blocked
Legally distributable templates, cross-center sharing, international
content licenses, AI-assisted authoring, automatic content reuse,
public publishing, audience PII, brand/moderation workflow and
operational permission grant policy all require separately approved
Business and Technical contracts. PR stays Draft/Open/unmerged.
No Ready for Review, Merge, Stage/QA Gate, Release or Production.
