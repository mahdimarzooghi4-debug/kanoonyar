# Sprint 115 — Portable API container image

## Business outcome

Provide a repeatable, host-neutral API image build that can be exercised in CI before a Stage host is selected.

## Technical contract

- Build the .NET 10 API from the repository root with the checked-in multi-stage `Dockerfile`.
- Publish the API in Release configuration and run it from the ASP.NET runtime image as the image's non-root application user.
- Listen on container port 8080; the host or orchestrator remains responsible for TLS termination, private networking, secrets, and health-check scheduling.
- Exclude source-control data, build outputs, frontend bundles, and dependency directories from the Docker build context.
- Build the image in Foundation CI for pull requests; do not push it to a registry.

## Verification

- Foundation CI builds `kanoonyar-api:ci` locally from the exact PR source.
- Existing backend tests, PostgreSQL integrity, frontend/Chromium, AI contract, and secret-scan gates remain required.

## Limits before Stage

This image does not select a hosting platform, provision DNS/TLS, database, OIDC provider, secret store, or network policy. It does not run migrations, grant access, create identities, or deploy. The runtime's `/health/live` and `/health/ready` endpoints remain available for a host-controlled probe; readiness requires the approved non-development OIDC configuration and reachable discovery established in S113–S114.

## Boundaries

No registry push or deployment is performed. No secrets or environment-specific settings are baked into the image. A successful image build is not approval to deploy.
