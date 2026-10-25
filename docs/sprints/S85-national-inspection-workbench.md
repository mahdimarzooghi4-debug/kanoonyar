# Sprint 85 — National inspection coverage workbench

## Business
Headquarters needs a readable, bounded view of S83 technical inspection aggregates and the S84 paginated API, without exposing center-level evidence.

## Technical
Add a headquarters workbench reading the existing review-rollup endpoint with same-origin credentials and no-store requests. Validate the fixed S83 response contract and 20-row keyset cursor. Show one page at a time with explicit previous/next navigation, aggregate coverage counts and latest technical observation only. Distinguish authentication, authorization and unavailable responses; render no stale or partial data after invalid responses.

## Acceptance
- 401/403/503 and malformed contract responses display safe, distinct messages and no rows.
- Cursor matches the last row; duplicate rows, unsafe counts, unknown contract versions, and non-false human-review or issuance flags fail closed.
- Navigation follows the exact cursor and fetches later pages only when requested.
- Summary says counts are technical evidence, not human approval, authorization or issuance.
- Browser tests cover default-deny 403, cursor navigation, and rejection of a response asserting a decision.

## Boundaries
No login flow, role, permission matrix, identity proof, human review, approval, issuance, or production grant is added. The API remains default-deny pending independent approval. CI and technical review are not human authorization.

## Delivery
Stacked on Sprint 84. Keep Draft/Open/unmerged. No Stage, QA gate, release approval or Production.
