# Sprint 97 — Atomic national inspection CSV export

## Business
Provide headquarters with a complete, filter-preserving CSV of the national inspection rollup without combining pages read from different database snapshots.

## Technical
- Add an authenticated export route beside the paginated aggregate read endpoint.
- Reuse the existing independent read-authorization contract, which remains default-deny.
- Apply the active findings-only, finding-code, and exact program/version filters in the database query.
- Reject cursor and unknown parameters for export; export is a single bounded query.
- Read the rollup under one read-only PostgreSQL REPEATABLE READ transaction and verify the restricted database role.
- Fetch at most 10,001 groups; return HTTP 413 when the result exceeds 10,000, before constructing a file.
- Export only program/version, aggregate diagnostic counts, and latest technical observation time. Quote every cell, escape quotes, neutralize formula prefixes, include a UTF-8 BOM, and use a fixed filename.
- The browser waits for the complete response, verifies CSV content type and BOM, and starts no download after an error response.
- Add API validation, synthetic PostgreSQL reader/export, and Chromium success/413 coverage.

## Boundaries
This is a read-only technical inspection export. It does not grant access: runtime authorization remains default-deny and the route also requires authentication. No private center/request/note fields are exported. No authority, human review, issuance, or operational decision is created. Synthetic fixtures are used in CI. CI and technical COMMENT are not independent human approval.

## Acceptance
- Existing paginated API behavior and filters remain unchanged.
- Export preserves active filters and rejects cursors/unknown parameters.
- The backend returns one snapshot-consistent CSV or a safe error; partial files are never downloaded.
- Results over 10,000 groups return HTTP 413.
- Both export route and existing API remain Draft-only work until separate authority and operational decisions are approved.

## Delivery
PR remains Draft/Open/unmerged. No Stage, QA gate, release approval, or Production action is included.
