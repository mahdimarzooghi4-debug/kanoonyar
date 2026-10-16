# Sprint 61 — Durable technical inspection notes on national issuance review intents

## Reason for this bounded increment

S58 produces read-only original-source HQ issuance preflight, S59 creates an
integrity-checkable structural recheck packet, and S60 captures immutable
national issuance **review intent** plus audit/outbox in a disposable
PostgreSQL security boundary. None approves issuance. Missing was a durable,
auditable *technical observation* linked to the exact S60 request: a later
inspection needs to record whether the authoritative source has changed,
without rewriting the original review or fabricating human approval.

Under B02/T15, HQ reviewer role, issuer authority, authenticated
reviewer and complete role×operation×center scope remain DRAFT.

## Original-source implementation

Migration `0044_national_issuance_review_inspection_notes.sql` creates
`programs.national_issuance_review_inspection_notes` with a
foreign-key link to S60's immutable request, frozen source digest,
actual source-at-inspection JSONB and corresponding checksum,
a source-drift boolean, brief technical note, **unverified**
inspector subject and recorded UTC timestamp.

A SECURITY INVOKER function with no PUBLIC/runtime EXECUTE grant,
`capture_national_issuance_review_inspection_note`, is exercisable
only in a disposable SQL fixture. It requires an explicit REPEATABLE READ
or SERIALIZABLE transaction. It validates the original S60 snapshot's
SHA-256 from the stored original JSONB, and independently re-reads the
EXACT original national program/version/ordered center selection
from the original publication, registry and issued-version tables
within the same transaction. It computes the current source digest
and whether the source differs from the original request.

Each invocation atomically records: (1) the append-only note,
(2) a linked immutable technical command audit and (3) a
transactional outbox record containing only the request/note IDs,
technical-change indicator and explicit non-authorizing flags.
The outbox does not include selected center identities,
original/source JSONB, note body or unverified inspector identity.
Duplicate note IDs, invalid/unlinked requests and failed
audit linkage do not leave partial notes or events.

`human_review_recorded=false`, `issuance_authorized=false`, and
`issuance_executed=false` are enforced by PostgreSQL CHECK
constraints. An inspector's arbitrary technical note is **not**
a human decision, approval, authorization, signature, or business
status even when its text says otherwise. No stored value is
consumed as an eligibility or issuance authority. The SHA-256
hash is not a signature or origin proof.

The table has FORCE RLS with zero policies and no PUBLIC grants,
plus immutable UPDATE/DELETE protection. The function is not
registered in an HTTP route, DI adapter, background worker or
production SQL login; even a synthetically signed OIDC identity
does not unlock access. No actual permission, enrollment,
center acknowledgement, programme issue or notification occurs.

## Real executable tests

The independent S60 PostgreSQL CI fixture is extended to install
S61 and run `national_issuance_review_inspection_integrity.psql`:

- Its real original **80 synthetic centers** are inspected in their
  selected order; an external inactivation after the original
  S60 request is detected, while another S60 request recorded
  after inactivation yields unchanged source observation.
- Checks pinned original digest, actual source digest, exact
  center ordering, and the immutable previous snapshot.
- Rejects duplicate note UUIDs, nonexisting requests,
  blank technical notes, and captures with insufficient
  transaction isolation.
- Confirms append-only mutation denial, forced-false states,
  1:1 note/audit/outbox, no private outbox data, no extra
  national issuance; independently tests atomic rollback
  when audit insert collides.
- Logs in through an independent actual non-superuser SQL
  account and checks denied function EXECUTE, denied direct
  INSERT and denied SQL role escalation.
- Adds a NEW original issuance via explicit fixture SQL only,
  re-inspects with another immutable note and proves updated
  historical source is visible without overwriting old
  inspection notes. No automatic decision or issuance occurs.

The full restricted API PostgreSQL fixture also applies the new
migration for regression coverage. Existing Backend, Frontend
Chromium, AI Contracts and Secret Scan gates remain mandatory.

## Remaining blocked capabilities

A real HQ inspection-review UI, authenticated/request-authorized
actor and reviewer, effective grants/revocation, permitted read
model and legal scope; independently human-approved review
decisions; issuance, recipient receipt/acknowledgement, connected
local plan workflows, Stage, nationwide QA, Release and Production.
An unchanged source is not evidence of permission and still
may be stale after the transaction.

Keep PR #61 stacked **Draft/Open** on PR #60; no Ready for Review,
merge, Stage/QA Gate, Release Approval or Production without
explicit owner authorization. Automated Code Review COMMENT
does not replace accountable independent human sign-off.
