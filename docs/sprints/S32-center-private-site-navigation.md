# Sprint 32 — Private ordered website navigation drafts

Business: KY-WEB-01/02/03 and AC-WEB-01/02/03/05. All source
Business texts remain DRAFT; this is a technical site-building capability
which does **not** approve/publicly serve content, delegate rights or assert
ownership/licensing of source material.

## Bounded product behavior
- Center editor selects actual source-owned S20 plain-text page draft identities
  and exact immutable page revisions, sets explicit link order, removes/reorders
  entries and saves a new whole-navigation version; an empty navigation is
  possible only by an explicit save. No arbitrary URLs, HTML, user CSS or
  copying content into the navigation domain.
- The original S20 page UUID+slug and immutable page revision are owned by
  `web.center_pages`/`web.page_draft_revisions`. S32 adds one immutable
  `web.center_site_navigation_revisions` per center/version, plus ordered
  `web.center_site_navigation_items` with composite FKs binding both
  center and exact page revision. Changes to the page's latest draft never
  silently change historic navigation references.
- POST `/api/v1/centers/{centerId}/site/navigation/revisions` body
  `{expectedRevision,items:[{pageId,pinnedPageRevision},...]}` creates a
  whole new atomic version via revision-CAS/advisory transaction lock, audit
  + outbox in the same transaction; stale or foreign source fails closed.
- GET `/api/v1/centers/{centerId}/site/navigation` returns
  `{revision:null,items:[],visibility:"PRIVATE_DRAFT",isPublished:false}`
  if explicitly not created, never a fabricated default. Otherwise returns
  pinned title/slug/revision/position from true source.
  GET `/api/v1/centers/{centerId}/site/navigation/revisions?beforeRevision=N`
  returns at most 20 historical immutable navigation revisions, each with its
  exact pinned original page titles, plus keyset cursor.
- Three independent operation rights SITE_NAVIGATION_APPEND / READ /
  HISTORY_READ plus the **separate** source page LIST_READ right.
  Signed OIDC subject, live active center membership, explicit default DENY,
  FORCE RLS, scoped NOLOGIN SECURITY DEFINER owner, EXECUTE-only
  command credential and read-only SQL role, no public routes.
- Preview renders only text labels of ordered draft pages within the
  authenticated center workspace. No public links, publication, forms,
  audience PII, site domain binding or template licensing.

## Evidence and governance
- SQL integration exercises actual same-center pinned source revisions,
  duplicate/foreign/missing pins, stale concurrent revisions, immutable rows,
  audit/outbox, multi-tenant reader visibility, restricted writer/reader.
- Chromium exercises page choice, reorder, private save/history, denial,
  forged publication and center-token scope clearing.
- Actual policy for content publication, moderation, national brand,
  template licensing and site hosting remains undecided. No Ready, Merge,
  Stage, QA Gate, Release or Production; PR stays Draft/Open.
