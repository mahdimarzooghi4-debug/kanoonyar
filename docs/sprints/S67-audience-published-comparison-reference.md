# Sprint 67 — Shareable published-version comparisons

## Product contract

A visitor can share the exact comparison between two versions of one national program that are currently available from the public published-version source. The public portal builds the link from the selected earlier version and latest published version.

The comparison URL accepts only one program identifier, one earlier version, and one current version. It rejects duplicate or unknown query parameters, malformed identifiers or versions, and a pair where the earlier version is not lower than the current version. URL values are a request only; they do not establish publication.

On opening a comparison link, the audience portal separately reads both exact versions from the published-version endpoint with no-store, anonymous GET requests. It verifies that each response matches the requested program and version and validates only the public field allowlist. If either read fails, is malformed, or mismatches, neither version nor comparison is shown. No latest-version fallback is allowed.

## Verification

Browser tests cover generated links, strict query validation, both exact source reads, identity mismatch, and failure of either source read. Existing published-only, pagination, text-comparison, and no-enrollment checks remain in the suite.

This is a read-only public audience capability. It does not introduce identity, accounts, registration, center-specific offerings, permissions, decisions, national issuance, or operational authority. The comparison remains literal text equality and does not infer legal or implementation meaning. CI and technical COMMENT are not independent human approval.
