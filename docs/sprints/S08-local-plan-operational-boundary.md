# Sprint 08 — End-to-end local draft adapter (technical implementation, NOT operational authorization)

## Delivered technical slice
- Authenticated OIDC POST \`/api/v1/centers/{centerId}/local-plan-drafts/revisions\` accepts
  \`draftId, expectedRevision, programId, nationalVersion, localTitle, localActivities\`.
- The actor is extracted from the verified JWT, not from request JSON; center scope comes from the
  route but must be independently authorized, not trusted as an authority claim.
- The default registered \`ILocalPlanDraftWriteAuthorization\` always DENIES (HTTP 403).
  No grant vocabulary, roles, grants, approval, or permission decisions have been invented.
  No currently authorized production writer exists.
- If/when an approved independent permission evaluator is provided, the handler additionally checks
  live active membership, restricted PostgreSQL login/current roles, RLS FORCE and ENABLED,
  then appends via the existing atomic command with audit/outbox in the same database transaction.
  Expected revision conflicts return HTTP 409 and no success is asserted before commit.
- Read API also denies unsafe/superuser/BYPASSRLS database connections (503, fail-closed).
  Database role configuration is separate for read/write, and neither variable implies a grant.
- Center UI can submit versioned drafts through the real API route and reload an existing draft
  for an expected-revision edit. Denial (403), conflict (409), and unknown status are distinct.
  Browser tests mock HTTP; they are NOT actual live DB-to-browser tests.

## Not authorized or delivered
- Missing approved operation permission matrix, grant issuance/revocation authority and audit,
  and authoritative real end-to-end browser→OIDC→API→restricted Postgres proof.
- No automatic grants from membership, role labels, center session GUC, environment variables,
  test identities, or UI state. Do not override the deny-by-default provider before Business
  approval and independent technical Code Review.
- Do not provision production runtime table grants, issue production credentials, enable Stage,
  QA Gate, Release, or Production from this slice.
- PostgreSQL test roles/grants exist only inside disposable CI database tests. RLS scope set by
  application is defense in depth, not an independently verifiable tenant identity.

## Next authorization gate
Obtain Business-approved permission×operation×center/resource contract plus grant/delegation and
revocation semantics, enforce on each request with trustworthy identity/tenant binding and tests
including stale access, cross-center ownership, concurrency, and revocation. Follow with integration
tests against a real restricted login role before claiming write operations are ready for users.


## Sprint 08 restricted-database integration extension
- Both read AND write handlers now require independent operation-specific authorization,
  implemented as DENY by default. No existing membership can authorize draft reading.
- The handler refuses PostgreSQL login roles that are privileged, database/table owners,
  BYPASSRLS, superusers, role administrators or database creators; both memberships and
  drafts must have ENABLE/FORCE RLS.
- CI runs a real Npgsql handler-to-PostgreSQL integration against a fresh throwaway
  database with a LOGIN role that is non-superuser/non-owner/NOBYPASSRLS.
- Test-only authorization adapters (compiled ONLY in the test assembly) allow the
  database path to be tested: create/read/revise, stale conflict, concurrent create,
  denied member/revoked member, cross-center isolation, atomic Audit/Outbox counts,
  default deny, and privileged-DB refusal.
- CI grants on the disposable database are not approved runtime privileges and cannot
  be copied into Production. The integration test exercises handlers directly with
  test ClaimsPrincipals; it does not verify a signed OIDC token or full browser-to-DB flow.
- No real grant issuance, permission activation, external IdP or user writes are enabled.
