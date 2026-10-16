# Sprint 63 — National issuance review event uniqueness

## Contract

S60/S61 already atomically write immutable request/inspection records with
technical audit and outbox rows. Their private, non-authorizing workflow must
not accumulate a second audit or outbox envelope for the same logical source
event, even if a retry supplies fresh UUIDs.

Migration `0046_national_issuance_review_event_uniqueness.sql` adds three
partial unique indexes limited to the S60 review-intent and S61 inspection-note
event families:

- one audit target per action, target type and target ID;
- one outbox event per event type, aggregate type and aggregate ID;
- at most one review outbox event per audit ID.

The indexes do not cover unrelated audit actions or outbox event types. They
do not issue, publish, dispatch, authorize, approve, or attest any event.
Existing atomic capture functions still own the single transaction that writes
the source row, audit row, and outbox row.

## Executable tests

The S60/S61 original-source PostgreSQL chain now applies migration 0046 and
runs `national_issuance_review_event_uniqueness_integrity.psql`. Against the
real 80-center fixture, it tries duplicate S60 and S61 audit rows with new
UUIDs, duplicate outbox rows with new event UUIDs, and reuse of an audit ID
for another national-review outbox envelope. Each attempt must fail with a
unique violation and leave row counts unchanged. The fixture also checks all
three unique indexes are installed.

Migration 0046 is included in the full restricted API PostgreSQL setup.
Backend, PostgreSQL, Chromium, AI-contract and secret-scan CI gates remain
mandatory.

## Authority boundary

No reviewer identity, grant, role matrix, human decision, actual issuance,
center receipt, local-plan transition, API or UI is introduced. Keep PR #63
Draft/Open/Unmerged and do not pass Stage, QA, release or production gates.
