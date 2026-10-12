# Sprint 09 — National Program Issuance Persistence (Draft)

Baseline: stacked on Draft PR #8; no approved HQ issuer policy.
Scope: versioned issuance data contract, immutability, atomic durable audit/outbox,
schema-level cross-reference and negative integration tests, 80 synthetic center IDs in CI.

Acceptance: exact published version + known center; new version does not rewrite old;
duplicate or unknown references fail; failed audit transaction rolls back issuance;
no PUBLIC EXECUTE or table write, no authorized runtime route, no production activation.

Out of scope: receipt/acknowledgement, delegation, real center imports, compliance
criteria, statutory authority, automatic issue-to-all, local draft mutation or rollout.

Parent process: Business -> Technical -> Backlog -> Sprint -> Code -> Code Review
-> Stage -> QA -> Release Approval -> Production -> Monitoring -> Improvement.
Business remains DRAFT; this sprint is an isolated engineering foundation only.