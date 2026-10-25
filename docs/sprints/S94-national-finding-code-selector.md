# Sprint 94 — National inspection finding-code selector

## Business
The headquarters technical inspection workbench can already locate all national program/version groups with at least one diagnostic code. Review staff also need to narrow that result set to one exact, stable technical code across every page.

## Technical
- Add a strict optional `findingCode` query parameter using only the ten existing S80/S90 diagnostic code identifiers.
- Apply the selected code predicate in SQL before keyset pagination. A selected code requires or implies the findings-only filter; contradictory explicit `findingsOnly=false` is rejected.
- Add a selector to the headquarters workbench, enabled only when findings-only is active. Reset to the first page when the selected code changes and preserve the exact code on subsequent page requests.
- Validate malformed, repeated, unknown, and contradictory query parameters before authorization/storage access.
- Extend disposable PostgreSQL-to-handler coverage to prove one exact code returns only matching groups across the multi-page fixture. Add browser coverage for code selection and removal of other finding groups.

## Acceptance
- Existing unfiltered and findings-only behavior remains unchanged when `findingCode` is omitted.
- Every accepted code maps to exactly its existing aggregate count; unknown codes are rejected.
- SQL filters before page limit/cursor processing, and selected filters remain stable across pages.
- The UI states that codes are technical diagnostics only and offers no ranking or decision.

## Boundaries
Read-only diagnostic discovery only. No severity, reviewer decision, identity/authority grant, or issuance behavior is added. Runtime authorization remains default-deny; all fixtures are synthetic and disposable. CI and a technical COMMENT are not independent human approval.
