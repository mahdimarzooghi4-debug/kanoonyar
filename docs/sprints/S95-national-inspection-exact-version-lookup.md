# Sprint 95 — Exact national program/version lookup

## Business
The headquarters inspection workbench needs a direct way to open the aggregate evidence for one exact national program version, without paging through unrelated program/version groups.

## Technical
- Add an optional exact target pair, `targetProgramId` and `targetNationalVersion`, to the read-only rollup API. Require both, validate a canonical positive version and bounded non-empty program identifier, and reject a target combined with a pagination cursor.
- Apply exact target predicates in SQL before the page limit. The lookup may be combined with findings-only and a selected stable finding code.
- Add a small accessible lookup form to the headquarters workbench. Changing/submitting a target resets results to the first page, preserves existing diagnostic filters, and offers a clear action to return to all groups.
- Cover malformed/partial/repeated targets and cursor conflicts without storage access; verify exact matching, missing targets, and combined finding-code lookup through the restricted PostgreSQL reader; add browser coverage.

## Acceptance
- Omitting target parameters preserves the existing paginated result set.
- A complete valid target returns only the exact program/version group, or an empty page when it does not exist.
- Target and diagnostic predicates are parameterized and applied before pagination.
- Exact lookup does not change authentication, authorization, decision flags, or issuance behavior.

## Boundaries
Aggregate diagnostic lookup only. No private source details, severity, review decision, authority, or issuance behavior is added. Runtime authorization remains default-deny. All test data is synthetic and disposable; CI and technical COMMENT are not independent human approval.
