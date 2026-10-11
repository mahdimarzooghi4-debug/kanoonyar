# Center identity and national-program adoption boundary

The center registry contract identifies centers explicitly. A membership contract binds an external identity subject to a particular center and an opaque role code. Neither record authenticates a subject or grants permission. A local adoption request binds center, actor subject, national program ID and version, and local title. Construction is not a publication, approval, assignment, or authorized write.

Before operational endpoints: establish persistent versioned headquarters registry; Keycloak/OIDC subject verification; human-managed center membership and delegated role policy; center-scoped authorization on every read/write; inactive center behavior; audit/outbox and concurrency rules; national publication authority; and version-stable local adoption. No role hierarchy, numeric limits, default access, or policy thresholds have been assumed.
