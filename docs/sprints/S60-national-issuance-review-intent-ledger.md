# Sprint 60 — Original-source national issuance review-intent ledger (NOT approval / issuance)

## Parent contracts and readiness

B03 KY-NAT-02, B04 WF-03, B05 AC-NAT-01..04 and technical T15 define
central issuance of an immutable published national-program version to
explicitly named centers. S58 implemented a source-only batch preflight;
S59 added versioned tamper-detection and change detection, neither of
which is an HQ operational grant. B02/T15 issuer identity, human reviewer
authority and the role × operation × center scope × conditions matrix
remain **DRAFT**. A valid source check does NOT authorize issuance.

## Concrete durable increment

Migration `0043_national_issuance_review_intent_ledger.sql` creates
`programs.national_issuance_review_intents`: an append-only **request
to review**, storing exact national `(program_id,national_version)`,
the original explicitly selected and ordered center IDs, a database-
built original-source snapshot, local format version, SHA-256 digest,
reason, unverified proposed requester, and append-only recorded time.

All four technical flags are constrained in PostgreSQL to **false**:
`requester_authorized`, `human_review_recorded`,
`issuance_authorized` and `issuance_executed`. No review, human
decision, promotion, issue, center delivery, receipt, compliance
decision or success status is possible within this table.

A **SECURITY INVOKER** SQL function
`programs.capture_national_issuance_review_intent` accepts only a
nonempty exact program/version and nonempty, exact, distinct ordered
center selections, an unverified actor string, stated request reason,
and explicit UUIDs for request, audit and outbox. It requires an
explicit `REPEATABLE READ` or `SERIALIZABLE` transaction; ordinary
read committed or implicit/default transactions fail before writing.

Under that caller-authorized database snapshot, the function reads
the authoritative *original* `programs.national_program_publications`,
`centers.registry`, and exact-version
`programs.national_program_issuances`; it builds an ordered JSONB
source snapshot showing published, center existence/activity and
prior issuance facts. It computes its own SHA-256 of canonical
PostgreSQL JSONB source text. **The function does not trust a caller's
S59 digest, issuer claim, authority or preflight flags**.

The source snapshot plus a non-authorizing request, a technical
command audit and a transactional outbox envelope are inserted as
one PostgreSQL transaction, with no separate service write. A failed
request or duplicate UUID does not add audit/outbox records.
The outbox contains only intent ID and explicit non-authorizing
state flags, and deliberately excludes private center IDs, reason,
source snapshot and unverified actor identity.

### Security restrictions

- No HTTP endpoint, event listener, UI button, job, DI registration,
  background approval, issuer grant, or data model for ACTUAL ISSUE.
- No runtime role has `EXECUTE` on the capture function; even the
  S52 permission-review account is not a trusted HQ issuer.
  `SECURITY INVOKER` means callers would need their *own*
  independently approved SQL privileges. The disposable CI
  `postgres` superuser is **test-only** and must never be used
  as a Production command identity.
- New table has FORCE RLS, **zero policies**, PUBLIC access revoked,
  and an append-only mutation-rejection trigger. Production SELECT,
  INSERT, UPDATE and DELETE all fail closed; no center grants are
  inferred. The original S09 issuance table/function and forced
  default-DENY authorization remain unchanged.
- Source digest protects only accidental structural changes. Neither
  S59 nor S60 SHA-256 is a signature, provenance, human review,
  legal attestation, authorization, effective version or a guarantee
  of current validity after the transaction commits.
- The stored `unverified_requester_subject` is an untrusted
  technical input, NOT a confirmed Keycloak principal.

### Executable acceptance

A dedicated fresh PostgreSQL database fixture runs
`backend/db/tests/national_issuance_review_intent_integrity.psql`
in mandatory Foundation `postgres-integrity` CI:
1. Seeds 80 original synthetic center registry records (one
   inactive), real versions V1 and V2 published plus V3 unpublished,
   and two independent historical issuances of different versions.
2. In explicit repeatable-read, records one *80-center* review
   intent and checks exact ordered IDs, original center/publication/
   previous-version facts, PostgreSQL source hash and all four false
   authorization/issuance flags.
3. Confirms exactly one intent, audit and outbox, no new issuance,
   no private center/actor/reason data in the outbox.
4. Rejects duplicate UUID, ambiguous center selection, malformed
   program version, and a missing isolation context before writes.
5. Connects as an actual non-superuser LOGIN and proves it cannot
   EXECUTE the function, INSERT a forged review, or SET ROLE a
   privileged actor. Checks no RLS policy, no privileged runtime
   access, and append-only protection.
6. Changes one original center to inactive *outside* the capture
   workflow, captures an independent later intent and proves the
   old immutable source snapshot remains unchanged, while new
   evidence sees the current source state.

S60 migration is also included in the existing full restricted
PostgreSQL API integration setup to catch schema compatibility
regressions. All Backend, other real PostgreSQL, Chromium,
AI contracts and Secret Scan tests must remain green.

## Deliberate block on Production activation

This is a durable **technical intention to request review**, not a
Human Review request authenticated and accepted by a human reviewer.
No permission policy, delegated approver, HQ account, public center
recipient, BFF, receipt/acknowledgement, actual issuance command,
audit attribution of a verified actor, local plan mutation, or
external notification is authorized by the implementation.

A future real review submission must bind verified OIDC subject,
approved grants, approved target selection, and fresh authoritative
source checks. A later approved issuance command must *again*
verify all those inputs and prior issuance within its own atomic
transaction. Capturing a request NEVER triggers it.

PR #60 stays Draft/Open stacked on #59. No Ready for Review,
Merge, Stage, QA Gate, Release Approval or Production without
explicit owner authorization and independent human Code Review.
