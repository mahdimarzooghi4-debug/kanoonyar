# Sprint 15 — Session Claim History

- Independent read authorization for SESSION_OCCURRENCE_CLAIMS_READ versus SESSION_ATTENDANCE_CLAIMS_READ;
  the former **never** grants the latter. Production authorization stays default DENY.
- Immutable source-owned occurrence claims and per-occurrence attendance claims are read only
  from PostgreSQL with live membership, FORCE RLS and a restricted SQL read login.
- Exclusive 20-item keyset pagination by (recordedAt DESC, UUID DESC), with paired
  UTC `beforeAt` + `beforeId` cursors; malformed or half-present cursors are rejected.
- Historical claims remain literally UNVERIFIED. Unknown attendance is not absent,
  evidence references are not validated evidence, and reported sessions are not
  confirmed delivery. No participant totals, attendance KPIs, outcomes or HQ rankings.
- Opaque participant identifiers are restricted data; no names or contact information
  should be used, and real participant rights/consent/source integration is not here.
- Test fixture SQL grants are strictly local and cannot authorize Production.
- PRs stay Draft/Open/unmerged; no Stage/QA/Release/Production.