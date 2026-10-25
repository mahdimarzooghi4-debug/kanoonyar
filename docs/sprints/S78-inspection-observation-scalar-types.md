# Sprint 78 — Typed inspection observation scalars

## Business
Technical inspection evidence must retain JSON types when deciding whether an observed snapshot is structurally comparable to the request scope. Text extraction can make a numeric `programId` or string `nationalVersion` appear equal to the expected text.

## Technical
Migration `0058_national_issuance_observation_scalar_types.sql` adds explicit JSON type checks to the existing SECURITY INVOKER inspection dossier's comparability predicate: `programId` is a JSON string and `nationalVersion` is a JSON number. Existing digest, pinned-request, scope, audit/outbox, and aggregate drift behavior is retained.

Synthetic tests attach two immutable observations to the S77 malformed scalar fixtures. Each observation has a matching digest and valid audit/outbox pair; assertions require integrity checks to pass while comparability is false.

## Boundaries
This migration adds no human review, role, permission, API, authorization, issuance, or operational account. The fixture inspector is unverified synthetic text. CI and technical review are not independent human approval.

## Delivery
Stacked on PR #77. Keep Draft/Open and unmerged. No Stage, QA gate, release approval, or Production.
