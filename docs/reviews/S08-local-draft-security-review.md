# Sprint 08 — Local plan draft integration and security Code Review

Reviewed SHA: `5e74c36895037ba8b32c031ea7ffdf3e4bf2fcf2` (preliminary integration green baseline).
PR: #8, stacked on Sprint 07, **Draft/Open/unmerged**.

## Verified evidence before final UI denial test
- Push CI [38152273044](https://github.com/mahdimarzooghi4-debug/kanoonyar/actions/runs/38152273044): SUCCESS.
- PR CI [38152276575](https://github.com/mahdimarzooghi4-debug/kanoonyar/actions/runs/38152276575): SUCCESS.
- Backend, Frontend Chromium, Postgres Integrity incl. disposable restricted role + API handler
  integration, AI contracts, and Secret Scan passed for the baseline SHA.

## Security checks covered
- Independent **default-deny** interfaces for read and write. Membership by itself does not
  grant either operation. No authorization policy has been invented.
- JWT-derived subject (test ClaimsPrincipal in direct handler tests) and center-specific
  transaction-local PostgreSQL RLS are separate controls, neither substitutes for a grant.
- DB login checks reject superuser/BYPASSRLS/role-admin/database-creator/table-owner
  connections. Membership and draft tables require FORCE+ENABLE RLS.
- The real test database suite exercises a constrained LOGIN/non-owner DB role,
  live membership, cross-center read/write isolation, revoked membership, stale version
  conflicts, concurrent revision zero submissions, and one-to-one revision/audit/outbox records.
- UI displays denial/conflict and never interprets a failed or malformed save as success.
- No trial permission provider is included in the production service container.

## Review limitations / blocking findings
1. **Business policy blocker:** permission × operation × center/resource × delegation/revocation
   grant contract is not approved. Both read and write MUST remain DENY in runtime.
2. **DB trust boundary:** test role has direct privileges solely in disposable CI; PostgreSQL
   custom `kanoonyar.center_id` is spoofable by a holder of SQL credentials and must not
   be accepted as a production security boundary. A production-grade least-privilege
   command path backed by independent verified identity/permissions still needs design.
3. **Identity contract:** the direct handler tests use synthetic ClaimsPrincipals, not a
   signed external OIDC login or a real browser->OIDC->API->Postgres system test.
   Current bearer-token entry UI is a development aid, not final approved login/BFF.
4. **Grant atomicity:** future revocation semantics must be bound to the same authoritative
   write decision with tests proving revoked/stale grants cannot write under race.
5. **Response ambiguity:** if a request commits but its HTTP acknowledgement is lost,
   a retry can conflict (HTTP 409); no idempotency/reconciliation contract has been approved.
6. **Published-program public API** must still receive Business/privacy/security confirmation.

Disposition: **technical safety foundation reviewed, not ready for an operational write/read
enablement or rollout.** Automated review and CI do not constitute independent human approval.
Do not mark Ready for Review, merge or deploy Stage/QA Gate/Release/Production without explicit
owner direction. The final SHA/CI and preliminary review are recorded separately in the PR.


## Signed JWT → actual ASP.NET HTTP → restricted PostgreSQL proof (additional Sprint 08 slice)
- Explicitly set \`JwtBearerOptions.MapInboundClaims=false\` so the signed standard OIDC \`sub\`
  claim survives into the authenticated principal used by draft handlers.
- A local test-only RSA signing key and offline OpenID metadata configuration drive the
  **real JwtBearer middleware and HTTP routing** in WebApplicationFactory tests.
- Valid signature, verified \`sub\`, and rejection of unknown signer, incorrect issuer,
  audience, expired token, missing subject, and admin-role-without-grant are tested.
- The opt-in CI PostgreSQL job exercises signed JWT + HTTP + ASP.NET handler + actual
  Npgsql connection via disposable restricted LOGIN role, revision concurrency,
  cross-center/disabled memberships, and exact audit/outbox cardinality.
- Test-only authorization doubles are installed only in the test hosting container to
  exercise storage. **Production app remains default DENY for all draft reads/writes.**
- The issuer/key metadata are offline CI fixtures, NOT an actual Keycloak integration.
  Browser→real Keycloak→API→PostgreSQL proof and approved business grant policy remain blocked.


## Supplemental least-privilege read-login proof
- Split disposable CI PostgreSQL logins into independent read-only
  \`kanoonyar_draft_read_ci\` and test-only writer \`kanoonyar_draft_api_ci\`.
- The read handler's database role guard rejects direct INSERT/UPDATE/DELETE on
  sensitive tables, execute rights on the draft append function, ownership,
  superuser/BYPASSRLS, and SET ROLE paths to privileged writer/owner roles.
- Real PostgreSQL tests verify raw SQL INSERT denial, rejection of direct
  command execution and SET ROLE, and refusal of writer-capable connections
  mistakenly supplied to the read endpoint.
- An additional synthetic role with no inherited writes but a SET ROLE path
  to the writer must be rejected by the read guard. This tests transitive
  escalation that checking only current_user can miss.
- Corrected the guard to inspect catalog OIDs for governance tables so a
  read-only login does **not** require governance schema USAGE merely to
  evaluate denied privileges.
- **Caveat:** SQL readers can still set a spoofable session GUC. These read
  privileges exist only on a disposable CI database; they are **not** an
  approved Production permission design. Both application-level operation
  grants remain default DENY.
