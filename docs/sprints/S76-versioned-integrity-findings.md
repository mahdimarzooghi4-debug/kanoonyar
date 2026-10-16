# Sprint 76 — Versioned technical integrity findings

## Business boundary

The S74/S75 integrity components are useful for technical inspection, but separate booleans are awkward for a future authorized review workspace to aggregate consistently. Sprint 76 maps failed components to stable finding codes with an explicit contract version.

Finding codes only describe data-integrity conditions. They are not workflow outcomes, human-review decisions, evidence of approval, or instructions to issue a national program.

## Technical contract

The SECURITY INVOKER view `programs.national_issuance_review_integrity_findings` emits one aggregate row per failed component with only:

- Contract version
- Request identifier
- Program identifier
- National version
- Stable finding code

Codes cover digest, semantic source envelope, ordered center selection, authority flags, audit evidence, and outbox evidence. Valid components create no row. Null/unknown component values are treated as findings. Rows contain no center identifier, source snapshot, technical note, requester/inspector identity, or approval decision.

The view is read-only, has no PUBLIC SELECT grant, and is not connected to an API or operational account. Existing restricted SQL roles remain denied.

## Test contract

The PostgreSQL suite verifies exact finding counts and component-to-code mapping against the synthetic S74/S75 corruption fixtures, emits no findings for known-valid baseline requests, checks the fixed contract version and five-column shape, rejects private fields, and confirms denied SELECT privileges for PUBLIC and restricted accounts.

## Backlog and release boundary

This adds a technical projection only. B02/T15 authority and independent human-review contracts remain unresolved. CI and technical review are not independent human approval. The PR remains Draft/Open with no Stage, QA gate, release approval, Production deployment, or real issuance.
