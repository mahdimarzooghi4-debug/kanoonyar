# Sprint 51 — Reviewable operational authorization boundary; no grant activation

Business/Technical: B02 users and delegated permissions remains DRAFT,
B04 WF-01 remains DRAFT, T04 identity and authorization remains DRAFT,
B05A AC-ORG-02/03 and AUTH-01/03/04/05/07/10/12. Explicit owner
instruction: continue product development, not approve a permission
matrix or deploy Production.

## Real security work implemented

### 1. Pinned complete ASP.NET operational route inventory
`OperationAuthorizationBoundaryTests` pins all **72 exact configured
identity/center API method+route pairs** as of main after Sprint 50.
Against *actual built app endpoints* it requires every route to be
present, no unexpected new public or operational API, and ASP.NET
`IAuthorizeData` on all except the one explicit public read
`GET /api/v1/programs/published`. Missing or extra routes, missing
`RequireAuthorization`, or inadvertent anonymous metadata fail CI.
Future API authors must update and review the inventory deliberately.

The same complete manifest is checked when OIDC issuer/audience are
not configured; each of the 72 route shapes is executed over an actual
HTTP TestServer and must return **503** rather than pretend successful
identity or operational access. This revealed and fixed missing no-OIDC
routes for the existing S46 direct class owner catalogue and S47
class-scoped S12 session read. No unauthenticated version may become
a backdoor or a false successful response.

Runtime DI for representative grant adapters is additionally asserted
to resolve only their existing `Unapproved...` default-DENY
implementations. There is no JWT role bypass.

### 2. Non-authorizing grant evidence contract
`OperationGrantEvidenceBoundary.HasMatchingEvidence` explicitly
compares a trusted issuer+subject with center, exact operation, exact
optional resource scope, opaque versioned policy identifier, exact
positive grant revision, current live membership, active center and
non-revoked state. Null/missing/whitespace identities, stale revisions,
revoked grants, another center, another issuer, broad wildcards,
altered resource scope or an inactivated membership deny the
**consistency proof**. It has full negative-unit coverage including 80
synthetic simultaneous tenant identities.

**Critical distinction**: a matching proof is not an authorization
decision. It does not attest authenticity of the supplied facts and
MUST NEVER be used as the endpoint's allow/deny mechanism. No
production DI uses it. An authoritative approved grant store, scoped
signed-issuer binding, approved approver/delegation matrix, human
grant/revocation workflow, audit and a secured policy evaluator
must be independently designed and approved before any real
positive authorization can occur.

## What remains blocked
No authoring of real grant records, human approval, activation,
delegation, privileged role, policy expiry, session revocation, child
data access or Headquarter cross-center scope is authorized by this
Sprint. OIDC authenticity validation, original center membership
checks, operation-specific default-DENY adapters and FORCE RLS remain
unchanged. No database migration, new public API, user roles, mock
Production center users, real participant data, payment or AI approval.

## Verification and gates
Full C# Backend, signed OIDC and API contract tests, restricted
PostgreSQL integration, frontend/Chromium regression, AI boundary and
Secret Scan CI must be green on the exact PR head. Technical Code
Review COMMENT is not a human sign-off. PR is Draft/Open stacked
directly on verified Sprint 50 main; no merge/Stage/QA/release or
Production without explicit user instruction.
