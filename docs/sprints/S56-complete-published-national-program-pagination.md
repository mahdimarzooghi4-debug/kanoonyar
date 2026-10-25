# Sprint 56 — Complete published national program catalogue without silent 500-row truncation

## Actual defect and bounded Business scope

The original S04 public `GET /api/v1/programs/published` read joins immutable
`programs.national_program_publications` to its matching real
`programs.national_program_versions`, but was capped at **500 rows**
without any `next` indicator. Both the Headquarters view and new
S53–S55 audience discovery inferred that those first 500 were the
*complete* publication history. If a single program has more versions
or publication count exceeds 500, "latest published version" or
national obligations could silently refer to the wrong record.

This is a source-completeness defect under B05 AC-NAT-04,
AC-EVL-03, AC-X-07 and B05A AC-REL-06. This Sprint corrects that
defect without adding an authorization policy, national program write,
private center data or fabricated local implementation.

## Backend contract (backwards compatible)

- Unchanged public `GET /api/v1/programs/published` returns the
  first **up to 500** immutable *published* rows in deterministic
  `(program_id ASC, version ASC)` order, plus a new nullable
  `nextCursor`. `nextCursor = null` means the query found no
  further published version after the returned page.
- When another row exists (queried using the 501st sentinel),
  `nextCursor` contains the actual **500th persisted published**
  `afterProgramId` and `afterVersion`. Caller fetches the next
  page with **both** URL query parameters; the handler uses an
  exclusive `(program_id,version) > (@id,@version)` comparison
  against the original publication join. Both values are passed
  as typed PostgreSQL parameters, never SQL interpolation.
- Missing half a cursor, duplicate keys, invalid/zero/noncanonical
  version, empty/noncanonical ID or unsafe values fail 400. Missing
  original public read configuration or database failure remains 503.
  The endpoint still returns **no unpublished versions**, original
  center-local plans, center permissions or real participant data.
- Existing HTTP callers with no cursor still receive the original
  `programs` array, now with additive pagination metadata.
  Existing direct `ListPublished` contract is retained for tests.

## Whole-list correctness in both existing experiences

- Audience S53 and Headquarters S05 use the same immutable original
  public read. Each retrieves all paged results **before** declaring
  the list complete or claiming the latest published version.
- Per-page validation is bounded at 500, requires cursor to be the
  **exact last returned row** on a full 500-row page, and rejects
  duplicate `(programId,version)` between pages or a repeated
  cursor. A 500-row legacy response without an explicit
  `nextCursor` is rejected as potentially truncated.
- A failed continuation, stale fetch, invalid/different cursor
  or untrusted published value clears all staged data rather than
  showing a partial list as complete, a false "latest" version,
  invented center delivery, public class, registration or result.
- Existing S53 audience search, genuine published version comparison,
  S54 self-only test harness isolation and S55 nondeployable CI
  bundle safety remain unchanged.

## Test evidence

- A new **real restricted PostgreSQL** API-handler integration
  inserts **502 synthetic published versions** of an original
  program, plus an **unpublished version 503**. Reads use a separate
  login granted SELECT on only the two original publication tables.
  It verifies exact 500+continuation traversal, no duplicate IDs,
  zero loss, original source identity, and exclusion of unpublished
  version 503. A parameterized invalid-cursor test matrix rejects
  half, duplicate, zero, negative, padded and oversized cursor values.
- The normal full CI PostgreSQL job executes this integration under
  `CENTER_DRAFT_INTEGRATION_REQUIRED=1`; the base backend suite,
  all previously present PostgreSQL contracts, signed OIDC, AI
  rules and Secret Scan still run.
- New actual Playwright Chromium tests in **both** Headquarters and
  Audience verify multi-page 502-version source completeness,
  no premature latest-version display, 503 on later page
  discarding the entire incomplete result, and forged cursor denial.

## Limits and governance

Keyset traversal is bounded by individual pages, not an invented
national programme count, approval, legal status or operational KPI.
If fresh legitimate publications appear while a browser is traversing
pages, this independently queried live read is *not* a transactional
cross-page snapshot; no as-of snapshot guarantee is claimed. A
consistent snapshot endpoint/version and retention policy would
require separate Business/Technical approval.

No public center directory, centre course offers, admissions,
personal profiles, accounting, national promotion, final grant
matrix, payment, real 80-center onboarding or BFF login is created.
All PRs remain Draft/Open/unmerged; no Stage, QA, Release Approval
or Production without explicit owner instruction.
