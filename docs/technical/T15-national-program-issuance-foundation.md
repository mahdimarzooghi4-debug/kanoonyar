# T15 — National Program Issuance Foundation (Draft Technical Contract)

Authority: B01 §3/5.1, B04 WF-03, B05A AC-REL-02; these Business documents are still DRAFT.
Implementation: Sprint 09 stacked Draft PR on Sprint 08. This is **not** an authorization to issue programs.

## Explicit structural evidence
- A national program *definition* is different from publication and center-specific issuance.
- Issuance is a separate immutable row for an exact (program_id, national_version, center_id),
  referring only to a previously published national version and an authoritative center ID.
- A newer national version does not rewrite old publication or issuance history.
- The issuer subject is recorded as source metadata, **not yet cryptographically verified**.
- Duplicate issuance of the same program version to the same center is rejected at DB level.
  No reassignment, re-issuance, cancellation, receipt or supersession transitions are invented.
- The national objectives/mandatory requirements are owned by the published immutable version;
  the issuance has no copy of those fields and cannot change them.
- Local-plan association has a pure structural boundary, but the existing runtime local
  draft endpoints are NOT silently changed to assume historical issuance.

## Durability and security
- 0013 migration adds FK and unique constraints, append-only trigger, FORCE RLS with NO
  policy, and explicit revocation of PUBLIC privileges. No runtime grant or HTTP route.
- SECURITY INVOKER record_national_program_issuance (PUBLIC EXECUTE revoked) writes
  issuance + command_audit + outbox_events atomically, with an explicit correlation ID.
- Function has no business-role verification: **do not grant EXECUTE until headquarters
  authority, operation-specific resource scope, revocation and verified identity are approved**.
- Unknown center, unpublished version, duplicate command, broken audit linkage and
  attempted mutation fail closed; test DB remains disposable.
- Real PostgreSQL integration exercises 80 **synthetic** center identities as a
  tenant-count test only. It neither provisions nor authorizes the actual 80 centers.
- The no-policy RLS is intentionally default deny; the product has no issuer or receiver
  operational view yet, and issuance is not proof of acceptance, compliance or execution.

## Outstanding Business and technical decisions
- Who at HQ may issue, to which centers, under what delegation and audit/revocation rules.
- Whether an issue needs receipt, amendment, withdrawal, acknowledgement, or review stages.
- Definition of local-plan compliance with central mandatory requirements and approvals.
- Trusted Keycloak/BFF identity, non-spoofable PostgreSQL command path, grant lifecycle.
- Do not enable new runtime API, merge, Stage, QA, Production or a partial national launch.