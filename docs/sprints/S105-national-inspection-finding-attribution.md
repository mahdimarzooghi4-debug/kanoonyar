# Sprint 105 — Finding attribution in inspection dossiers

## Business outcome

When headquarters analysts inspect or export a filtered dossier, show the exact technical finding codes attached to each immutable inspection note. This makes the S104 filters explainable at the row level.

## Technical contract

- Read note-level codes from the existing versioned `national_issuance_review_inspection_findings` view, ordered deterministically.
- Include the code list in each exact-version dossier page and validate it against the same four-code allowlist in the browser.
- Add a final `finding_codes` CSV column, using semicolon-separated stable codes and the existing CSV cell escaping.
- Version the JSON page contract and the CSV schema/receipt; retain the complete exact-version export and its row cap, repeatable-read snapshot, row count, and SHA-256 verification.
- Do not include center identities, source snapshots, inspector claims, private note text, or human decisions.

## Verification

- PostgreSQL integration checks code attribution for a known synthetic finding in both JSON and full CSV.
- Browser tests verify filter attribution is displayed and the versioned export schema is accepted only after row-count and digest validation.
- Existing exact-target, restricted-role, privacy, limit, and default-deny authorization coverage remains in place.

## Boundaries

Finding codes remain technical diagnostics; they are not approvals, permissions, or issuance decisions. B02/T15 remain drafts. Production authorization stays default-deny.

## Release state

Draft PR only. No merge, Stage, QA gate, release approval, or Production action.
