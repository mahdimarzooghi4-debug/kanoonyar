# Sprint 59 — Integrity-bound HQ national-issuance preflight review packets

## Domain issue from Sprint 58

B04 WF-03, B05 AC-NAT-01..04 and B05A AC-REL-02 require exact
national-version, selected center identities and historical issuance
lineage before a future human may authorize a national issue. Sprint
58 introduced a real PostgreSQL read-only preflight in one
REPEATABLE READ snapshot, but a previous inspection can become
stale before someone evaluates it. A reviewer could also compare
a changed, reordered or copied center selection without knowing it.

B02/T15 authority matrix, national issuance/receipt/withdrawal
rules and actual human reviewer grants remain **DRAFT**. This
increment is not a substitute for these approvals or live issuance.

## Implemented boundary

`NationalIssuancePreflightReviewPacket` is a new **unregistered,
route-free, non-authorizing** component. It wraps only an
`NationalIssuanceBatchPreflight.Observation` from the existing S58
original-source inspection, with a defensively copied and
read-only ordered center list, an explicit format version and
SHA-256 digest of a deterministic binary representation.

The versioned canonical representation binds:
- Exact national `programId` and positive `nationalVersion`.
- Whether that *exact* national version is in the original
  publication table (not whether a definition exists).
- Number and ORDER of all explicitly selected distinct
  original center IDs, and for each one its actual registry
  existence, registry active flag and exact-version previous
  issuance existence.
- Both `IssuesAnyCommand=false` and
  `HumanIssuanceAuthorizationVerified=false`.

Length-prefixed UTF-8 text, well-defined primitive encoding
and ordered center entries prevent delimiter collisions,
silent reordering and accidental omission. Checksums are
recomputed and compared with constant-time byte equality
before revalidation. Tampered values, unsupported format,
invalid digest, duplicate/missing/unsafe center IDs,
impossible registry flags, any "approved" or "issuing" flag
are rejected.

**SHA-256 IS NOT A SIGNATURE.** A matching checksum does not
establish provenance, authenticate issuer/reader, prove a
human review, satisfy a legal requirement or authorize
issuing to any center. The source observation must originate
from an independently authorized read path; no runtime SQL
login, grant or HTTP route is added.

## Exact revalidation

`RecheckAsync` first verifies packet integrity. It then
re-inspects the original published version, center registry
and exact historical issuances for precisely the same
ordered center selection using S58's new read-only repeatable
read transaction, derives a new packet, and compares its
digest to the pinned original. It returns:
- `SourceUnchanged` — equality of structural source observations
  between these two snapshots, *not* a promise the world has
  not changed during or since the recheck.
- `CurrentPacket` — new versioned structural evidence, still
  with no inferred grant, eligibility or approval.
- `HumanReviewRecorded=false` and `IssuanceAuthorized=false`
  unconditionally.

Even `SourceUnchanged=true` must never trigger issuance.
A future authorized issuance command would need to recheck
trusted OIDC actor, explicit HQ grant and center scope,
publication, center status, historical issuance, approved
policies and audit/outbox atomically within its *own*
permitted command transaction.

## Tests

- Pure xUnit digest invariants: same source deterministic,
  variation in program, version, published flag, 80-center
  order, registry/existing issuance flags changes digest.
- Defensive copy / modified packet detection; forged
  approved/issued flags and invalid checksum fail before
  any SQL read. Unsupported, duplicate or contradictory
  source evidence is denied.
- The existing S58 integration test, executed by the
  mandatory real PostgreSQL gate on the original schema,
  now checks an **unchanged 80-center** recheck; detects
  source drift after center inactivation, after an
  independently seeded V3 publication, and after an
  independently seeded exact-version historical issuance.
  Fixture SQL mutations are explicitly independent of
  `Create` and `RecheckAsync`; neither writes anything
  or emits new issuance audit/outbox.

## What is NOT delivered

No actual review-submission UI, backend endpoint, durable
review record, user/issuer authentication, signed review
attestation, acceptance state, HQ entitlement, verified
recipient, local plan mutation, issuance trigger or
Production database credential. This is an offline,
testable structural integrity/recheck component. The
existing S09 issuance function remains ungranted and
its FORCE RLS default denial is untouched. All real
80-center onboarding, lawful operations, Stage, QA,
Release Approval and Production remain unauthorized.

PR is Draft/Open stacked on Sprint 58. Green CI and automated
Code Review COMMENT are NOT independent human review or
a release authorization. No Ready for Review, Merge,
Stage, QA or Production without explicit owner instruction.
