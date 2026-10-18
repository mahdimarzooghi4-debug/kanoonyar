# Sprint 116 — Read-only Stage API smoke check

## Business outcome

After an environment owner deploys the API to an approved Stage host, provide a repeatable check that the deployment is live, ready, and does not expose the authenticated identity endpoint anonymously.

## Technical contract

- Accept only an absolute HTTPS API base URL without embedded credentials, query, or fragment.
- Perform GET requests only, with a bounded timeout and no credentials or request body.
- Do not follow redirects; a redirect is a failed check.
- Require `/health/live` and `/health/ready` to return HTTP 200.
- Require unauthenticated `/api/v1/identity/me` to return HTTP 401.
- Inspect status codes only; never print or persist response bodies.
- Provide no option to disable HTTPS for real runs. The unit tests use a private loopback-only HTTP exception internally.

## Verification

- Standard-library tests run against an in-process loopback HTTP server.
- Coverage includes success, unhealthy readiness, an anonymously accessible identity route, redirects, and unsafe base URLs.
- Foundation CI runs the smoke-check tests after building the API image.

## Use after Stage deployment

Run `python3 tools/stage_smoke_check.py https://<approved-stage-api-host>` from a trusted operator environment after deployment. The host must be supplied by the environment owner. The check sends no token and does not modify data.

## Boundaries

This check does not deploy, authenticate a test user, validate database contents or migrations, prove tenant isolation, create accounts or grants, or replace end-to-end QA. A passing result is technical evidence only, not Stage acceptance or release approval.
