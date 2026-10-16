# Sprint 79 — Semantic inspection observation integrity

## Business
A structurally typed observation can still contain impossible center facts. Blank or noncanonical identifiers, duplicate center IDs, and active or issued centers marked absent must not be treated as comparable evidence.

## Technical
Migration `0059_national_issuance_observation_semantic_integrity.sql` extends the existing SECURITY INVOKER dossier comparability predicate. It requires trimmed bounded control-free center identifiers, unique IDs, and registry existence whenever a center is marked active or issued. Digest validity, exact requested scope, audit/outbox integrity, aggregate drift fields, and existing non-authority guarantees remain intact.

Four synthetic PostgreSQL fixtures cover blank ID, duplicate ID, active-without-registry, and issued-without-registry observations. Each carries a matching digest, exact ordered scope, valid audit/outbox pair, and false authority flags.

## Boundaries
No human decision, identity attestation, role, permission, API, authorization, issuance, or operational account is added. Test actors and source records are synthetic. CI and technical review are not independent human approval.

## Delivery
Stacked on PR #78. Keep Draft/Open and unmerged. No Stage, QA gate, release approval, or Production.
