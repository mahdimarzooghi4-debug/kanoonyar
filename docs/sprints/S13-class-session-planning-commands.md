# Sprint 13 — Class and Session Planning Commands

This slice turns owner-source identity tables into deliberately permission-gated
HTTP creation workflows for classes and planned sessions.

- PostgreSQL migration 0017 adds two SECURITY DEFINER commands owned by a
  NOLOGIN/NOBYPASSRLS role, each writing class/session + audit + outbox atomically.
- Source rows remain append-only. A later schedule correction requires a
  separate versioned contract; no UPDATE of historical planning data is allowed.
- Exact center-scoped FK requires a session to belong to an existing class.
- `scheduledAt` is nullable, and absence is never fabricated.
- Invalid/duplicate records, unknown or other-center class, audit collision,
  missing scoped membership and wrong center are covered in real PostgreSQL CI.
- An independent default-DENY operation grant is required before any live API
  mutation. Disposable CI roles do not authorize production writes.
- The custom PostgreSQL center GUC and supplied actor are not cryptographic
  identity. Database credential impersonation remains a known security boundary.
- A planned class/session cannot be treated as a delivered session, attendance,
  learning outcome or evidence-approved report.
- PRs remain Draft, with no Stage/QA/Release/Production.
