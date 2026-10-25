# Sprint 96 — Filtered national inspection CSV export

## Business
Headquarters reviewers need a portable, spreadsheet-friendly copy of the current national inspection rollup, including every result page that matches their active diagnostic filters.

## Technical
- Add an explicit CSV download to the read-only National Inspection Rollup workbench.
- Restart the read at page one and preserve findings-only, selected finding code, and exact program/version filters on every request.
- Validate every response with the existing contract parser; reject repeated group keys, cursor loops, page failures, and exports beyond 10,000 rows / 500 pages. Do not create a partial download.
- Export only program/version, aggregate counts, and latest technical observation time. Exclude center identifiers, request/note identifiers, source payloads, and technical note text.
- Quote every CSV cell, escape embedded quotes, prefix formula-like values (including values preceded by control/space characters), and include a UTF-8 BOM for spreadsheet compatibility.
- Add browser coverage for filtered multi-page traversal, complete row count, formula injection handling, and exclusion of private fields.

## Acceptance
- Export re-fetches the complete result set from page one using the filters active when export starts.
- Every page passes the strict response parser and duplicate/cursor checks before the browser receives a file.
- Failures and cap overflow leave the user without a partial file and provide a safe recovery message.
- The export contains aggregate rollup fields only and makes formula-like text inert in spreadsheet applications.

## Boundaries
This is a read-only convenience over authorized aggregate diagnostics. It does not change authentication, runtime authorization, source visibility, human review, authority, program status, or issuance behavior. Test data is synthetic. CI and technical COMMENT are not independent human approval; keep this PR Draft/Open/unmerged and do not advance Stage, QA, release, or Production.
