# Sprint 93 — National technical finding-code filter

## Business
The headquarters read-only inspection workbench needs a direct way to find national program/version groups with recorded technical finding codes across the full result set, rather than scanning clean groups page by page.

## Technical
- Add an optional strict `findingsOnly=true|false` API query parameter and apply it against the ten stable S90 request/inspection component counts before keyset pagination.
- Keep the default response unchanged when the filter is omitted or false. Reject malformed, repeated, or unknown query parameters.
- Add a workbench toggle that restarts pagination when changed and sends the filter on every page request.
- Explain that the filter locates diagnostic codes only; it does not rank severity, imply review, or make decisions.
- Add restricted PostgreSQL-to-API coverage with 21 synthetic inspection records missing their matching audit/outbox evidence, producing a known stable finding code across two filtered pages. Verify clean source-drift-only groups are excluded, the known code groups are included, and filtered pagination advances.
- Add browser coverage for toggling the filter and retaining the safe technical-only context.

## Acceptance
- Unfiltered pagination remains compatible and returns the existing aggregate groups.
- Filtered API pages contain only groups with at least one S90 component finding count above zero.
- The seeded synthetic finding is returned with its matching component count; a clean group with source drift but no S90 code is not returned.
- Malformed filter syntax is rejected before storage access.
- No private identifiers or source details are returned; all human-review and issuance flags remain false.

## Boundaries
Diagnostics only; no severity model or human review policy is introduced. Fixtures are intentionally synthetic and confined to disposable CI. Runtime authorization remains default-deny. CI and technical COMMENT do not constitute independent human approval or permission to merge, stage, release, or deploy.
