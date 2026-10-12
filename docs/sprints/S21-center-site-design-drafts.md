# Sprint 21 — Per-Center Private Site Design Drafts & Safe Component Preview

**Parent:** PR #20; **Business:** KY-WEB-01/02/03, AC-WEB-01/02/03/05/06.

This sprint adds **only** tenant-owned site design revisions, a restricted
set of built-in local layout presets and a private preview of existing
source-owned page drafts. Business approvals for public publishing,
copyright/shared-template licensing, branding standards, external media,
forms/PII and moderator roles are **not** invented or activated.

The source model is `web.center_site_design_revisions`: immutable, append-only,
`(center_id, revision)`, explicit required selections (no default approval).
The built-in UI-only presets are `EDITORIAL`, `HERO`, `COMPACT`,
palettes `OCEAN`, `FOREST`, `EARTH`, typography `SYSTEM`, `SERIF`.
The fixed component choices are an introductory/header component and
a simple footer component, individually toggled. These are bounded
presentation capabilities, **not** approved national brand guidelines.
Only plain text header/footer and enumerations are persisted; no raw CSS,
HTML, arbitrary components, templates shared between centers or external URLs.

A center can append a revision by compare-and-swap `expectedRevision`;
advisory lock and unique PK serialize concurrent writes. Security:
FORCE RLS, live center membership, separate operation-specific human
authorization (default DENY), restricted EXECUTE-only writer and dedicated
read-only credential. The function writes design revision plus
Audit and Outbox atomically without storing private header/footer text
in Audit/Outbox. No synthetic production grants.

Contracts:
- `POST /api/v1/centers/{centerId}/site/design/revisions`: requires
  `expectedRevision, layoutKey, paletteKey, typeKey, headerText, footerText,
  showIntro, showFooter`. Reply: new revision + `PRIVATE_DRAFT`,
  `isPublished=false`.
- `GET /api/v1/centers/{centerId}/site/design`: returns latest design or
  explicit `revision:null, design:null` (not an invented default),
  always PRIVATE_DRAFT and not published.
- `GET /api/v1/centers/{centerId}/site/design/revisions?beforeRevision=N`
  yields max 20 immutable private versions, keyset pagination.

Preview is rendered *inside the authenticated center workspace* using a
page already obtained by the source-owned S20 page-list endpoint and the
design obtained through this module. React renders plain text and
bounded CSS constants, never dangerouslySetInnerHTML or arbitrary styles
from server/client. Preview doesn't publish or establish legal content
rights. A page modification remains independent of a design revision;
neither silently overwrites a published version (no publishing exists).

Required evidence: disposable PostgreSQL SQL integrity including same-center
RLS and append-only audit, restricted-login handler CAS and runtime
authorization, JWT admin denied without independent grant, Chromium safe
preview and rejection of forged publication plus scope invalidation.
No Ready for Review, merge, Stage/QA Gate/Release/Production permitted.
