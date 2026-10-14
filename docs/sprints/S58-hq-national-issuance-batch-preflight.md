# Sprint 58 — Headquarters national issuance batch preflight, NO operational issuance

## Business intent and source of truth

B03 KY-NAT-01/02/06, B04 WF-03, B05 AC-NAT-01..04, B05A AC-REL-02,
and T15 describe issuance of an immutable *exact published national
version* to **explicitly selected** center identities. They do NOT
approve the issuer's operating role, center selection policy, batch
issuance, revocation, acknowledgement, re-issuance, deadline, compliance
or delegated authority. Those Business/Technical contracts remain
DRAFT; national launch is still DENIED BY DEFAULT.

Previous sprints built real S09 original publication/center issuance
tables with force-RLS no-policy deny and an ungranted issuer function;
S53–57 added PUBLIC published-only national version discovery.
A real HQ issuance actor cannot safely be invented from the public
view or from membership and a JWT role claim.

## Substantial, deliberately offline technical increment

`NationalIssuanceBatchPreflight.InspectAsync` is an **unregistered,
route-free, read-only source inspection** taking an exact national
`(programId,version)`, a required nonempty explicit collection of
distinct real center IDs and a caller-supplied, independently permitted
already-open PostgreSQL connection. It does not issue to every center
by default, import the actual 80 centers, or infer target/eligibility.

The method reads under one PostgreSQL REPEATABLE READ, explicitly
READ ONLY transaction:
- Exactly whether the source version is in original
  `programs.national_program_publications` (never assumes a definition
  is published).
- For *each* selected `centers.registry` ID in input order, whether
  it exists and whether its source `is_active` flag is true.
- Whether `programs.national_program_issuances` has an original
  issuance row for exactly the chosen `center_id,program_id,
  national_version`, not a nearby version or another center.

All SQL uses fixed statements and typed parameters; selected
center IDs are passed as a PostgreSQL `text[]` parameter, not
interpolated into SQL. Missing/whitespace/control-character IDs,
duplicate center selection, non-positive version and empty batch
fail *before* any database read. The response contains only the
explicit center IDs and structural source flags: no source actor,
private participation, target personal identity, KPI, coverage
score, guessed center eligibility, approval or fabricated status.

Every successful response **explicitly** reports
`IssuesAnyCommand=false` and
`HumanIssuanceAuthorizationVerified=false`.
A source record that happens to be ACTIVE, or a previously published
version, is NOT an authorization/approval. Only an accountable
human-approved command path with explicit grants and live source
checks can eventually issue.

**No controller route, background worker, server registration, DB
role/grant, new table, mutation, admin action or UI is added.**
The existing force-RLS-protected issuance table remains inaccessible
to runtime accounts. The test uses the disposable CI PostgreSQL
administrator *only* to inspect historical source data; this must
NOT be deployed as a Production privileged credential or API.

## Executable data-integrity evidence

A dedicated xUnit/PostgreSQL integration test (mandated by the
existing `postgres-integrity` CI job) inserts **80 original
synthetic center registry identities**, one intentionally inactive,
three original national program versions with only V1/V2 published,
a V2 issuance to center 00 and historical V1 issuance to center 01.
It then performs an actual read-only preflight using the original
source tables and confirms:

1. Exactly 80 selected centers, preserved input order, no implicit
   all-center selection, independent per-center registry flags.
2. Only the original exact V2 issuance for center 00 is observed
   under V2. Historical issuance V1 for center 01 cannot count as
   V2 issuance; version V3 is **not published** and cannot inherit V2.
3. An explicitly requested unknown center is reported as missing,
   not silently invented or dropped; inactive center stays
   distinguishable from nonexistent center.
4. Multiple inspections do not change issuance cardinality,
   append any national issuance audit, publish a version, mutate
   a center, create grants or mark a human authorization.
5. Subsequent center inactivation is visible in a fresh inspection;
   preflight is only a snapshot and **never an authorization ticket**.
6. Missing/untrusted/duplicated selection is rejected without
   accessing the database. Existing real PostgreSQL, signed OIDC,
   Frontend/Chromium, AI and secret tests remain part of full CI.

## Explicit limits and next contract gates

This is not a complete headquarters workbench or a Production
preflight service. It is a deterministic, independently testable
source inspection package for eventual HQ issuance. Its source
snapshot could be stale immediately after the read; eventual
execution must recheck the same exact publication, center, previous
issuance, independent issuer/center scope permissions, human
approval and audit/outbox within its *own* authorized command
transaction. A green structural preflight may NEVER trigger a
write or imply permission to issue.

Pending work includes B02 role × operation × scope × condition,
actual HQ authenticated issuer, batch selection authority,
human review and execution authorization, acknowledgement/receipt
contracts, local-plan association with exact issuance, outcome
lineage, 80-real-center end-to-end workflows and full national
Stage/QA/Release/Production gates.

Keep Sprint 58 Draft/Open stacked on Sprint 57. No Ready for
Review, Merge, Stage, QA, Release Approval or Production without
explicit owner instruction. An automated Code Review COMMENT is
not independent human sign-off.
