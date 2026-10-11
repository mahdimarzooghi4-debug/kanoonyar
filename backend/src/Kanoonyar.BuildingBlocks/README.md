# BuildingBlocks — Foundation contract

This assembly contains small dependency-free technical primitives shared by the host and future bounded modules. It is not an authorization, tenancy resolver, or persistence layer.

- `TenantId` rejects blank identifiers. A syntactically valid identifier does **not** prove the caller may access that tenant.
- Never resolve tenant context from untrusted client input without authenticated, server-side authorization.
- No center, headquarters, audience, finance or minor-data access policy is defined here.
- BuildingBlocks must not depend on API, Contracts, future modules, database providers or AI runtime.
- The current project graph tests reject unapproved project references; new module edges require reviewed architecture changes and negative tests.

Sprint 01 foundation only; no operational multi-tenant security is claimed.
