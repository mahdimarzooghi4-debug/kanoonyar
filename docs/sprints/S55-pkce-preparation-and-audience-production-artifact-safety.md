# Sprint 55 — OIDC PKCE contract preparation and safe audience test-harness isolation

## Authority and source

- Business B01/B02/B05A and Technical T04 remain DRAFT for actual audience
  account provisioning, browser login, legal guardian/consent, identity
  lifecycle, session persistence, token refresh, logout and revocation.
- S53 introduced genuine public published-only national program browsing.
- S54 introduced a **test-only manual bearer field** that deliberately
  used the original signed-JWT GET /api/v1/identity/me endpoint to verify
  the user's own subject. Manual bearer entry is NOT an approved
  production sign-in UX.
- This Sprint responds to the exposed risk: a test-only credential form
  should not accidentally ship in the publicly deployable audience bundle.

## (A) Production audience artifact is now public discovery only

The normal `npm run build` / `npm run build:audience` Vite production
build unconditionally hides the S54 manual test credential component.
The existing public program explorer stays fully operational and
unauthenticated.

- The compiled audience production bundle in `dist/audience` is
  checked to contain **neither** the S54 manual bearer element ID nor
  its original private /api/v1/identity/me fetch.
- A separate nondeployable, explicit `audience-ci` Vite mode builds
  `dist/audience-ci` for the Playwright suite; the Playwright audience
  preview points to **only** that isolated test bundle, preserving every
  S53/S54 browser regression without putting test credential entry into
  the publishable artifact.
- Existing standalone deployment manifests continue covering only the
  real headquarters, center and normal audience build outputs. There
  is no audience-ci manifest/upload/deployment route. The test-only
  build is generated *after* normal release-artifact checks.
- Producing the special CI mode for deployment is prohibited; no
  configuration or mode flag is a substitute for an audited release
  pipeline and human release approval.

## (B) Isolated PKCE cryptographic preparation, no live login

`OidcPkcePreparation` in the security module is an unregistered,
route-free, process-local *protocol contract*, NOT a BFF or login
service. It generates 256-bit random state, nonce, code verifier and
independent browser binding. It constructs the standard OIDC
Authorization Code + PKCE S256 URL from explicitly supplied,
pinned HTTPS issuer, authorization endpoint, clientId and callback.
The verifier never enters the URL.

- The pending original issuer, client, redirect URI, binding, nonce,
  verifier and **caller-specified** expiration instant are compared
  on consume. No default TTL, session lifetime or expiry threshold is
  guessed. The actual browser binding is checked with constant-time
  byte comparison.
- An in-memory ConcurrentDictionary consumes valid state **once**;
  replay, foreign browser binding, stale expiry, wrong issuer/client/
  redirect, missing authorization code and invalid HTTPS URL fail
  closed. Parallel replays cannot successfully consume twice.
- Unit tests recompute SHA-256 challenge from the consumed verifier,
  check exact mandatory OIDC query fields, reject unsafe redirect and
  issuer configurations, confirm expiry, concurrency and 80 unique
  synthetic browser-state isolation pairs.
- Because there is **no HTTP route, Keycloak discovery, token exchange,
  signed ID-token validation, cookie/session backend, nonce redemption
  against a signed ID token, CSRF middleware or runtime DI**, this
  component confers NO authentication or permission to a real user.
  A matching PKCE/nonce is NOT proof of identity. A future separately
  approved BFF must implement all these safeguards, especially
  issuer trust, state binding to a secure HttpOnly browser cookie,
  durable bounded ticket storage, expiry cleanup, login/session
  revocation and logout.

## Acceptance and deferred decisions

The full CI must pass exact-head C# cryptography tests, original
signed OIDC HTTP/security regression, Frontend three-experience
production builds, **production JS artifact absence assertions**,
separate Chromium-only test bundle, real restricted PostgreSQL,
AI contracts and secret-pattern scans.

The following remain deliberately forbidden and absent: live audience
PKCE login route, real identity profile, center relationship, legal
consent/guardian proof, real registration/admission, finance/payment,
any grant promotion, server secret or external AI connection.

PR #55 remains Draft/Open stacked on #54/#53/#52/#51. This is a
small but auditable security foundation, **not** a production
authorization or acceptance gate. No Ready for Review, merge, Stage,
QA/Release Approval or nationwide deployment without owner instruction.
