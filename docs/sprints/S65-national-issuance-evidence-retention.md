# Sprint 65 — National issuance review evidence retention

## Contract

S60 requests and S61 inspection notes are immutable technical evidence.
Their audit and outbox records are also append-only. S64 enforces a complete
source/audit/outbox triple when one of these records is inserted. This sprint
adds an executable integration check that the four existing append-only
protections preserve the full historical evidence pair for both event
families after commit.

The test is rollback-safe at each rejected SQL mutation and checks exact
before/after JSON snapshots, event/source cardinalities, and the national
issuance count. It exercises UPDATE and DELETE against the S60 request,
S61 inspection note, their audit rows, and their outbox rows. No schema,
permission, role, reviewer, decision, issuance, route, or dispatch behavior
is added.

## Verification meaning

This verifies the current database triggers on the supplied synthetic S60/S61
fixtures and the current S63/S64 event guards. It does not prove retention
against a database owner disabling triggers, a Production backup/restore,
a deployed schema, legal retention rules, operator identity, or future
outbox delivery. Those remain separate engineering and governance work.

PR #65 stays Draft/Open/Unmerged and is stacked on PR #64. CI and technical
COMMENT are not independent human approval or release authorization.
