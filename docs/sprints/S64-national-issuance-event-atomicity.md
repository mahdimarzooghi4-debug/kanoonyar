# Sprint 64 — National issuance review event atomicity

## Contract

S60/S61 capture functions write an immutable request or inspection note,
technical audit, and outbox envelope in one transaction. Sprint 63 prevents
duplicate event pairs. Sprint 64 makes completeness a deferred PostgreSQL
constraint so the database rejects a commit that contains only part of this
triple.

Migration `0047_national_issuance_review_event_atomicity.sql` adds a
SECURITY INVOKER trigger function and deferred constraint triggers on the S60
request table, S61 inspection table, governance audit table, and outbox table.
At transaction commit, each S60/S61 source or event must resolve to exactly
one matching original source, audit, and outbox record with the expected IDs,
event family, integrity metadata, and all non-authorizing flags false.
Malformed target/aggregate types for known S60/S61 event codes fail closed.
Unrelated audit actions and outbox event types are skipped.

No trigger authenticates a subject, grants access, approves a request,
authorizes issuance, or dispatches an event. Production tables remain without
runtime grants or RLS policies; the integrity guard adds no route or role.

## Executable coverage

The focused original-source fixture runs against 80 synthetic centers and
applies migrations through 0047. Existing valid S60/S61 commands prove complete
triples still commit. New negative cases force deferred checks to immediate
inside a disposable transaction, then verify that PostgreSQL rejects and rolls
back:

- S60 request and S61 inspection source rows without their audit/outbox pair;
- an orphan S61 audit row;
- an orphan S61 outbox row linked to an unrelated audit;
- known S61 audit/event types paired with malformed target/aggregate types.

The test checks all source, audit, and outbox row counts return unchanged after
rejected statements. Migration 0047 is also included in the full restricted
API PostgreSQL fixture. All five Foundation CI gates remain mandatory.

## Authority boundary

No reviewer identity, decision, permission, actual issuance, center receipt,
local-plan activation, API, UI, Stage, QA gate, release approval, or Production
deployment is included. Keep PR #64 Draft/Open/Unmerged.
