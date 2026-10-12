# Sprint 22 — Center-owned Private Form Definition Builder

**References:** KY-WEB-02, US-FRM-01, AC-FRM-01..03, and KY-AUD
consent constraints. **Stacked on Draft PR #21.**

This is a bounded, useful **form definition editor**, not a response
collector. A center can create a form, edit its fields, view immutable
history and preview an accessible read-only rendition. Forms cannot
be published and cannot collect any values, including personal data,
consent assertions, or child/guardian information.

## Concrete contract
- Owner: ContentWeb (`web.center_forms`, `web.center_form_draft_revisions`);
  center and immutable ASCII slug identify the form.
- Supported schema-only fields: `SHORT_TEXT`, `LONG_TEXT`, and
  `SINGLE_SELECT`; at most 12 fields, per-form unique stable
  ASCII key, human-readable bounded label, design-only `required`
  indicator; `SINGLE_SELECT` has 2–8 bounded unique options. No
  file, date-of-birth, phone, email, payment, consent-checkbox,
  conditional logic, arbitrary schema or executable code.
- Each revision is append-only, tied to form and center; optimistic
  compare-and-swap with transactional advisory locking. Same-center
  FKs, FORCE RLS and unique slug protect data. Atomic Audit/Outbox
  carry revision/field count but no field labels/response values.
- API:
  `POST /api/v1/centers/{centerId}/site/forms/drafts/revisions`
  includes formId, expectedRevision, slug, title, fields.
  `GET /api/v1/centers/{centerId}/site/forms/drafts` returns
  max 20 latest private definitions with exclusive UUID keyset cursor.
  `GET /api/v1/centers/{centerId}/site/forms/drafts/{formId}/revisions`
  returns 20 historical definitions with exclusive version cursor.
  Every response must say `visibility: PRIVATE_DRAFT`,
  `isPublished: false` and `acceptsSubmissions: false`.
- Operation-specific per-center independent human grants (default
  DENY): FORM_DRAFT_APPEND, FORM_DRAFT_LIST_READ and
  FORM_DRAFT_HISTORY_READ. Active center membership alone, JWT role
  or a center GUC is not an operational grant. Separate EXECUTE-only
  SQL writer, restricted read login, default-deny auth.
- Frontend: native plain-text field editor with add/remove and
  bounded choice options, immutable version selection, read-only
  non-submitting preview. No HTML injection, fetch of public URL,
  rendered submit button or request for form response.

Testing: SQL integrity, concurrent CAS via restricted PostgreSQL,
revoked/cross-center isolation, Audit/Outbox consistency, JWT
default DENY and Chromium create/edit/history, forbidden grants,
malformed or forged PUBLIC/submittable payload rejection and
center/token switch clearing.

**Not done:** submission storage, responses, public form,
privacy consent, lawful retention, workflow automation, approvals,
publish rights. These are explicit future contracts, not defaults.
No Stage/QA Gate, merge, Ready for Review or Production.
