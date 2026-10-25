# Sprint 114 — Stage OIDC discovery readiness

## Business outcome

Do not report a non-development deployment ready merely because OIDC settings look structurally valid. The configured provider must answer its standard discovery endpoint and identify itself as the exact configured authority.

## Technical contract

- Reuse the configured HTTPS `OIDC_AUTHORITY` and request its `/.well-known/openid-configuration` document with a bounded four-second client timeout.
- Require a successful HTTP response, valid JSON, and an `issuer` string exactly equal to the configured authority.
- Fail readiness for unavailable endpoints, timeout, malformed documents, missing issuer, or issuer mismatch. Keep diagnostics generic and do not expose provider response bodies.
- Keep `/health/live` independent from readiness checks and keep Development readiness independent of external OIDC.
- Exercise provider responses with an in-process fake HTTP client; CI does not contact an external identity provider.

## Verification

- Matching issuer and successful discovery produce HTTP 200 readiness and assert the exact discovery URL.
- Unavailable discovery, mismatch, missing issuer, and malformed JSON produce HTTP 503 readiness.
- Missing OIDC configuration remains unhealthy while liveness remains healthy.
- Foundation CI remains required for the exact final pull request head.

## Limits before Stage

Discovery reachability does not prove token issuance, client registration, user identity, account lifecycle, role grants, or application authorization. It does not provide a Stage host, TLS/origin isolation, database, reviewed migrations/restore process, test accounts, approved access matrix, or end-to-end acceptance plan. Those remain prerequisites owned by the appropriate environment and policy owners.

## Boundaries

No provider is contacted by the test suite, and no real issuer URL, credential, identity, role, authorization, human approval, release approval, or deployment is introduced. A healthy discovery probe is not authorization to deploy.
