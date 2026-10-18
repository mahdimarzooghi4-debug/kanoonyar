# Sprint 113 — Stage readiness OIDC gate

## Business outcome

Make the API's readiness signal meaningful for a non-development Stage environment. A deployment without the identity-provider configuration required to authenticate users must not report ready.

## Technical contract

- Keep liveness independent: `/health/live` answers whether the process is running.
- In Staging and other non-Development environments, `/health/ready` is unhealthy unless the paired OIDC authority and audience pass the existing startup validation.
- Development remains usable without OIDC for local and CI tests.
- A well-shaped OIDC configuration only satisfies the configuration gate; it does not prove provider reachability, client registration, user authorization, or any approved role grant.

## Verification

- Backend tests prove Staging readiness fails when both OIDC settings are absent while liveness remains healthy.
- A second test proves a structurally valid HTTPS authority and audience satisfy this single readiness check.
- Foundation CI remains required for the final SHA.

## Remaining Stage evidence

This sprint does not provision or deploy Stage. Before a Stage run, the environment still needs an approved non-production OIDC issuer/client configuration, isolated host/origin and TLS, a Stage database with reviewed migration/restore procedure, test identities and role grants approved by their owners, and an agreed end-to-end acceptance plan. The frontend deployment guide records that hosted deployment, DNS/TLS and origin isolation are not implemented. No real center or audience data is needed for this gate.

## Boundaries

No identity-provider account, role, operational grant, real data, human review, issuance, or release approval is created. A green readiness response is not authorization to deploy.
