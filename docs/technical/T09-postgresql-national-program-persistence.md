# T09 — PostgreSQL national-program persistence foundation

Two ordered, unapplied SQL migrations introduce the `programs` schema:

1. `0001_national_program_foundation.sql` defines version-keyed headquarters definitions, exact-version publication evidence, and center-scoped local plans. A local plan has a composite foreign key to an explicitly published national version, not merely a draft definition. Version and center IDs are opaque, and no fixed number of centers or program goals is assumed.
2. `0002_append_only_national_versions.sql` rejects UPDATE/DELETE of national definitions and publication evidence at the database layer. Corrections require new versions and a separately reviewed workflow, not mutation of history.

These migrations are **not applied or verified against a live PostgreSQL instance** in the current CI. There is no migration runner, database integration test, authoritative center table, OIDC verification, runtime DB principal, role-specific grants, RLS policy, transactional audit/outbox, or operational API yet. The schema does not claim that a supplied `created_by_subject` or `publisher_subject` was authenticated. The comments in the SQL are future operational requirements, not implemented privileges. In particular, the center ID is not yet foreign-keyed to an authoritative center registry, and cross-center read isolation must not rely on the schema alone.

**Next gate:** provision ephemeral PostgreSQL in CI, apply migrations in order, test duplicate versions, foreign-key publication/adoption, append-only triggers and rollback, then add authoritative center persistence and verified OIDC authorization before exposing writes. No production migration or deployment is authorized by this foundation.
