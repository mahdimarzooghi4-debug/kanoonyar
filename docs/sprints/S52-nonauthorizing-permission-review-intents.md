# Sprint 52 — Immutable permission-change review intents (never operational grants)

## Why this slice
B02 WF-01 and T04 require a traceable path from a **future authorized human**
request to a separately authorized human reviewer, then an independently
approved policy evaluator. B02/T04 and the role × operation × scope ×
condition × audit matrix remain DRAFT. Sprint 51 already froze the
production 72-route authentication inventory and created a
NON-AUTHORIZING evidence-matching predicate. Sprint 52 implements
only the durable **review-request evidence**, not grant issuance,
review decisions, actual delegation or revocation.

## Immutable technical data source
`0042_permission_change_review_intents.sql` adds a dedicated
`identity_access.permission_change_review_intents` append-only source.
It stores request UUID, original center identity, requesting subject,
proposed target subject, opaque proposed operation, optional exact
resource reference, a stated review reason, and recorded UTC time.
The request is an unapproved *proposal*. It is never a policy
definition, permission, human approval, assignment, transfer of
data, legal consent, or OIDC credential.

Both `review_approved` and `permission_effective` are constrained
to **false** in PostgreSQL. There is no approval transition, no
ACTIVE status, and no effective grant table. A role claim in
Keycloak, a verified center membership or a matching Sprint 51
evidence object cannot make the record grant an operation.
The existing Production `Unapproved...` authorization adapters
and full route inventory stay untouched.

## Technical enforcement
- Center FK, mandatory nonblank bounded fields, duplicate UUID
  replay rejection and a strict owner-center FORCE RLS policy.
  There is no cross-tenant shared lookup, implicit permission
  wildcard or default role; proposed-operation text is not an
  authorized operation catalogue.
- A NOLOGIN / NOINHERIT / NOBYPASSRLS restricted SQL owner executes
  one SECURITY DEFINER function to atomically persist the
  proposed request, immutable command audit and transactional
  outbox event. Audit/outbox convey only the review intent and
  explicit false effective/approved state; target identity and
  stated reason are excluded from the outbox payload.
- The function requires a transaction-scoped center and active
  requesting center membership. SQL-supplied actor and center
  variables are **not authentication**; it is deliberately
  **not executable by any Production login or API**. There is
  no public or authenticated HTTP command; no automatic approval,
  staff role, policy assignment, rights import or new UI.
  Only disposable CI fixture credentials can exercise the function.
- No updates/deletes of proposals, even by an administrator.
  Future human review outcomes, revocation decisions and
  grants require independently approved, append-only source
  contracts rather than rewriting this historical request.

## Real PostgreSQL acceptance
The independent disposable `permission_change_review_intent_integrity.psql`
is run through full CI on a fresh PostgreSQL DB; tests real SQL:
1. A function-only, non-privileged synthetic login can record one
   proposal with linked audit and outbox in a single call.
2. Duplicate UUID, cross-center scope, revoked requesting
   membership and malformed operation are rejected atomically.
3. Direct table/audit writes and SET ROLE to restricted owner are
   denied. PUBLIC and general read accounts cannot invoke it.
4. Both false-state DB constraints, append-only update/delete
   triggers, audit/outbox cardinality and no user subject in
   outbox are asserted.
5. FORCE RLS prevents even the no-login owner from reading the
   review evidence under another tenant context. The existing
   broad disposable API integration schema migration chain also
   applies S52's new migration to catch integration regressions.

## What is still blocked
A B02-approved permission definition, approved grant/change
requester, real identity and issuer binding, official approver/
delegation limits, independent review history, effective grant
versioning/revocation, business policy promotion, UX, real
Keycloak and all HQ/audience permissions. Implementing these
without explicit Business approval would allow forged authority,
which is out of scope.

No real 80-center migration/data seeding, production DB
credentials, deployments or public services are created.
This is *engineering evidence* and an incomplete Business workflow.
Keep Sprint 52 PR stacked Draft/Open on Sprint 51. Full green CI,
technical COMMENT and author merge permission are separate;
none gives Stage/QA/Release/Production authorization.
