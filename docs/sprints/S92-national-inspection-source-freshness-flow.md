# Sprint 92 — National inspection source-freshness flow

## Business
Validate the complete read path for changing source evidence: a technical inspection note records one source observation, the underlying center registry later changes, and the headquarters aggregate reports the resulting source drift without exposing private center evidence.

## Technical
- Extend the disposable PostgreSQL-to-API integration fixture with one synthetic active center, 22 published national program/version groups, and review intents.
- For one group, capture an inspection note in the same stable transaction while the center is active, then change that center's active state after commit.
- Through the restricted test reader and existing default-deny authorization seam, assert that the API reports one note, zero current-source matches, one current-source drift, and a technical observation timestamp for that program/version. Check all decision and issuance flags remain false.
- Update the headquarters browser fixture to show and assert the current-source match/drift pair in the read-only table.
- Keep all fixtures disposable and synthetic.

## Acceptance
- The API's S90 V2 aggregate is derived from the actual PostgreSQL source views for a changed source observation.
- The matching/drift counts distinguish the recorded observation from the subsequently changed source.
- The UI renders the aggregate freshness pair while retaining its technical-only explanation.
- No center identifier, source snapshot, or note detail is returned; no decision, grant, or issuance is created.

## Boundaries
This validates diagnostics only. The synthetic center and source mutation exist only in disposable CI data. API access remains default-deny outside the test seam. No authority, human-review policy, issuance workflow, Stage, QA, release approval, or Production action is introduced. CI and technical review are not independent human approval.
