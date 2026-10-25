# Sprint 81 — Observation diagnostic reason codes

## Business
A generic non-comparable finding does not tell a technical reviewer which evidence shape needs inspection. Provide deterministic reason codes while preserving the existing S80 finding contract.

## Technical
Migration `0061_national_issuance_observation_diagnostic_reasons.sql` adds the SECURITY INVOKER projection `programs.national_issuance_review_observation_diagnostics` with contract `S81_OBSERVATION_DIAGNOSTIC_V1`. It classifies invalid request evidence, invalid observation digest/change metadata, malformed source envelopes, invalid or duplicate center identifiers, inconsistent center facts, exact ordered scope mismatch, and an explicit unclassified fallback.

The projection returns only request/note identifiers, program/version, contract version, and diagnostic code. It does not expose source snapshots, center identifiers, free-text notes, or inspector subjects. PostgreSQL tests verify all four semantic fixture mappings, deterministic one-row classification, projection shape, and restricted reads.

## Boundaries
Diagnostics identify technical evidence shape only. They do not establish human review, identity, permission, approval, authorization, or issuance. No grants, API, operational account, or workflow decision is introduced.

## Delivery
Stacked on PR #80. Keep Draft/Open and unmerged. No Stage, QA gate, release approval, or Production. CI and technical review are not independent human approval.
