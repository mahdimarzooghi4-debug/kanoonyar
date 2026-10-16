# Sprint 75 — Semantic integrity of original source envelopes

## Business boundary

The national review inspection summary reports whether stored request evidence can be trusted for technical inspection. A structurally typed JSON object can still carry impossible center facts, blank or duplicated identifiers, or inconsistent registration flags. Sprint 75 strengthens that diagnostic while keeping all request records as unapproved technical intents.

This work does not define issuer or reviewer roles, decide what constitutes a valid human approval, authorize issuance, or create an operational write API.

## Technical contract

For an original source snapshot to pass the source-envelope component:

- Each selected center identifier is a nonempty trimmed string, at most 256 characters, without control characters.
- The center list contains no repeated identifiers.
- Each center entry has boolean registration and issuance facts.
- A center cannot be active or have an exact issuance unless the registry entry exists.
- Existing format, program, national version, publication fact, digest, and ordered selection checks remain in force.
- A failure sets the envelope diagnostic to false and consequently keeps the composite source-integrity result false. Other component diagnostics remain independently visible.

All checks are read-only and aggregate-only. No private snapshot, center identifier, requester text, or person identity is added to the summary view.

## Test contract

Disposable PostgreSQL fixtures with valid digests and valid audit/outbox pairs isolate three semantic envelope defects: a blank identifier, duplicate identifiers, and an active-but-unregistered center. The tests require the digest, ordered selection, authority flags, audit, and outbox components to remain valid while the envelope and composite checks fail.

Existing 80-center, history, privacy, and restricted-role tests remain in the integration chain.

## Backlog and release boundary

This is a technical integrity increment only. B02/T15 authorization and human-review contracts remain unapproved. The PR stays Draft/Open. CI and technical review do not constitute independent human approval. No Stage, QA gate, release approval, production deployment, or real issuance is performed.
