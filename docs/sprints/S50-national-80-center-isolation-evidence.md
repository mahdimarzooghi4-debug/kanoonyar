# Sprint 50 — Eighty synthetic center data-isolation evidence, without release claims

## Business and Technical references
- B01 initial **80 real cultural centers**; B03 KY-ORG-01/03/05,
  KY-LMS-01/02, KY-EVL-01/03, national common technical acceptance
  B05A AC-REL-01 and AC-REL-03 and AC-ORG-02/03.
- T03/T08/T10 tenant access boundaries, canonical S12 owner data,
  S46 direct owner class catalogue and S47 class-scoped S12 session source.
- Business and national release gates remain DRAFT/unapproved.

## Scope: genuinely executable, partial test evidence
The new `NationalEightyCenterIsolationEvidenceTests` exercises the **existing real
S46/S47 API handler → restricted read SQL principal → PostgreSQL** path on
the repository's disposable CI PostgreSQL environment. Exactly 80 synthetic
center registry identities, 80 separate OIDC-substitute subjects with explicit
current-center membership, 80 original center-owned classes and 80 original
session rows (with source-owned session/class IDs) are seeded by the
**CI admin fixture only** in one transaction. These are not the authentic 80
centers, not imported personal data, not assigned Production actor roles and
not a Production data migration.

The test then independently checks for **each of the 80 identities**:
- The source-owning authenticated subject sees exactly its center's own
  original class through S46 and exactly the associated S12 session through
  S47, with authentic labels, source IDs and nullable planned timestamps.
- Using the **same valid actor** to request the next synthetic center
  cannot disclose its class owner list (empty due to membership) or its
  sessions (404 from the original source owner query).
- Original class/session identity set cardinality is precisely 80, without
  one class/session appearing as another center's.
- One deliberately multi-center member in two centers can see only
  the requested center's records per request, **not** a union of its two
  legitimate centers and never data from the other 78 centers.
- Individually revoked membership and separately inactive center
  independently suppress subsequent reads immediately, without
  rewriting another center's source data.
- An active member with no independently authorized operation remains
  denied on both S46 and S47; an administrator SQL credential cannot
  replace the verified restricted reader for either endpoint.
- Original server APIs and FORCE RLS policies are unchanged. Test-only
  allow grants are scoped to this test assembly and **never installed**
  in runtime DI; Production authorization remains default DENY.

When `CENTER_DRAFT_INTEGRATION_REQUIRED=1`, missing fixture credentials
fail the test. It is included in the existing full PostgreSQL CI test
suite, not a manual script or a success inferred from mocked responses.

## Meaning and limits of evidence
This is a **bounded 80-tenant isolation demonstration for exactly two
read paths and the corresponding canonical owner records**. It provides
neither a guarantee for all modules, nor an import/onboarding record for
real centers, nor a global access policy approval. In particular it
does not test all 80 centers end-to-end through the browser, real
Keycloak accounts, local-plan issuance flows, consent/guardian
authority, financial modules, public publication, campaign counts,
payments, full reporting, backups, disaster recovery, accessibility,
performance/service-level goals, or meaningful national operational
capacity. No new threshold, benchmark, KPI, login, role or center
onboarding rule is invented. Tests do not replace security penetration
testing or independent human review.

## Governance
Sprint 50 remains on a distinct Draft/Open stacked PR based on
Sprint 49, following contract → test code → full CI → technical COMMENT.
A green test is explicitly `PASS_LIMITED_SCOPE`, **not** Stage
Admission, Production readiness, release signoff, approval of all
80 centers, or permission to merge. No Ready for Review, Merge, Stage,
QA Gate, Release Approval or Production without explicit owner instruction.
