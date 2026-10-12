# Sprint 08 — restricted draft command SQL trust-boundary assessment

Status: TECHNICAL DRAFT. No human/business authorization or deployment admission.

## Verified vulnerability prior to migration 0012
Earlier CI-only writer grants allowed direct INSERT to the revision table and governance
audit/outbox. A holder of SQL credentials could bypass the atomic command and create
an unaudited revision. RLS enforced the caller-selected session center GUC, but the GUC
alone did not guarantee the authenticated actor, authorized operation, or source center.

## Technical mitigation now implemented
- Migration 0012 replaces the append function with a SECURITY DEFINER command owned by
  a dedicated NOLOGIN, non-superuser, NOBYPASSRLS role with exact required table writes.
- The definer function pins search_path to pg_catalog, pg_temp, validates request inputs,
  requires exact GUC center equality plus active same-center membership, and writes the
  revision, command audit and outbox in the same transaction.
- The SQL runtime command login is intended to have only EXECUTE on that function plus
  SELECT of live center/membership evidence; no direct INSERT on revisions/audit/outbox.
- VerifyWriteCommandOnlyAsync refuses dangerous login role capabilities and unexpected
  direct table writes, including attempts to SET ROLE to privileged/table-owner roles.
- PostgreSQL SQL/handler/HTTP tests use disposable roles and data, asserting legitimate
  test-only approved writes still persist exactly one revision, audit and outbox event.
- Adversarial tests deny SQL direct INSERT, forged audit INSERT and SET ROLE, and check
  missing/mismatched GUC and inactive membership cannot produce partial records.

## Remaining decisive blockers
- IMPORTANT: SECURITY DEFINER does NOT prove caller authenticity. A holder of the
  function EXECUTE credential can set the custom center GUC and supply any actor
  subject; the membership check can be satisfied using another person's known ID.
  Consequently no Production grant or runtime write enablement follows from this.
- Identity/authorization must be tied to an independently trusted, revocable
  operation-specific grant and a database trust boundary that SQL credential holders
  cannot spoof. The exact contract and mechanism need an approved Business/Technical ADR.
- DB network isolation, transport encryption, credential custody/rotation and
  deployment identity are not established by these disposable CI experiments.
- The operational approval matrix, delegation rules, revocation race contract,
  real Keycloak/BFF and end-to-end staging admission remain explicitly unresolved.

Do not merge PR #8, mark it Ready, deploy Stage/Production, or activate actual draft
reads/writes without owner instruction and the missing independent approvals.