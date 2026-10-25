# Sprint 89 — Validate paginated history responses in the center UI

## Business
A center user reading unverified session evidence must receive complete pages and must not mistake a malformed continuation for trustworthy history. The backend now tests database keyset order and index compatibility; the client also needs to validate each response before rendering it.

## Technical
Validate that every occurrence and attendance response page has unique IDs and strict descending order by UTC timestamp then UUID. Normalize PostgreSQL/.NET UTC timestamp strings with variable fractional precision before comparing them. Require a continuation cursor to be paired, valid, and equal to the page's final record; require each following page to be strictly older than the cursor. Reject malformed pages without displaying their rows.

Browser-test the valid 43-claim, three-page path using synthetic responses with sub-millisecond timestamps, as well as mismatched cursors and duplicate claim IDs.

## Acceptance
- The center UI renders all valid records across 20/20/3 pages without duplication.
- Each outgoing continuation request uses the exact timestamp and ID from the preceding page's final record.
- UTC timestamps with sub-millisecond differences are ordered correctly even when they share the same JavaScript millisecond.
- Mismatched continuation cursors, duplicate IDs, or non-descending attendance/occurrence pages clear the affected history and show the existing unavailable state.
- No attendance verification, permissions, claims, backend behavior, or production policy changes.

## Boundaries
All browser responses and participant references are synthetic. Rows remain explicitly UNVERIFIED. No live access grant, real participant data, Stage, QA gate, release approval, or Production is introduced.

## Delivery
Stacked on Sprint 88. Keep Draft/Open/unmerged.
