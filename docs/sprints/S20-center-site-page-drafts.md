# Sprint 20 — Tenant-Scoped Website Page Draft Editor

## Product contract / bounded scope
Business KY-WEB-01/02/03 and AC-WEB-01/02/03/05 establish per-center
page creation, versioning, and prevention of unauthorized public publication.
Content publication rights and legal moderation policies remain undecided:
this Sprint **does not implement publication, public viewing, uploads, forms,
branding, shared content templates, payment, PII capture or consent**.

The `web` context owns two tables: immutable `center_pages` (per-center
stable slug and UUID identity) and append-only `page_draft_revisions`
(plain-text title and body). Slugs are strictly lowercase ASCII letters,
digits and hyphens; fixed after creation. Title up to 200 chars, plain body
up to 20,000 chars. No HTML rendering or remote resources.

- `POST /api/v1/centers/{centerId}/site/drafts/revisions` with pageId,
  expectedRevision, slug, title, bodyText. Compare-and-swap revision via
  transaction-scoped advisory lock, exact unchanging slug and tenant scope.
  Returns pageId, slug, new revision, `visibility: PRIVATE_DRAFT`,
  `isPublished: false`.
- `GET /api/v1/centers/{centerId}/site/drafts?beforePageId=<uuid>` returns
  at most 20 latest private page drafts, UUID keyset cursor.
- `GET /api/v1/centers/{centerId}/site/drafts/{pageId}/revisions?beforeRevision=<positive>`
  returns at most 20 private immutable revisions, exclusive revision cursor.
- **No public content GET** and no publication or approval command.

Every API requires authenticated OIDC `sub`, an independent
`ICenterSiteDraftAuthorization` operation grant (LIST_READ, HISTORY_READ,
APPEND), and live center membership. All runtime grants default DENY and
no role or membership automatically yields page authoring access.
Writer connects only with `CENTER_SITE_DRAFT_WRITE_DATABASE_URL` and
the EXECUTE-only SQL login; reader with the safe read connection.
Neither a center GUC nor an actor parameter is a substitute for authorization.

Append is atomic across page identity, revision, Audit and Outbox. Row-level
FORCE RLS, same-center FKs, append-only triggers, a NOLOGIN restrictive
function owner and unique slug protect persisted history. Concurrent stale
writes return conflict, not silent overwrite; duplicates do not add events.
No page content is copied into Audit or Outbox.

The center frontend provides a local page draft form, editing selected
versions, private list and historical comparison. It only renders plain
React text, never raw HTML; strict validation rejects forged PUBLIC
or isPublished=true server responses, clears protected data on token
or center change and displays authorization and conflict failures.

Acceptance: real disposable PostgreSQL migration/integrity and
privilege tests; restricted-login handler tests including cross-center,
revocation, same-slug collision, concurrent CAS and audit/outbox;
signed JWT default-deny; Chromium editor create+revise, denial,
scope-switch and fake public response rejection. No human QA,
Ready for Review, Merge, Stage, Release or Production is authorized.
