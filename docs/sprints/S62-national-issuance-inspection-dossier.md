# Sprint 62 — National issuance inspection integrity dossier

## Scope and authority boundary

B02/T15 still do not establish a verified HQ reviewer, issuer, role-to-operation
scope, review decision, or issuance workflow. This sprint therefore adds only a
database-side, read-only projection of the existing S60 request and S61
technical inspection records. It does not create a review decision or expose
the records through an application endpoint.

Migration `0045_national_issuance_inspection_dossier_view.sql` creates
`programs.national_issuance_review_inspection_dossier`, a virtual
`SECURITY INVOKER` view with one row per immutable inspection note. It
recomputes both source digests, checks the note's pinned request digest against
the original S60 source, and checks the exact matching technical audit and
outbox envelope. The projection carries only request/note identifiers,
program/version, timestamps, hashes, drift observation, integrity booleans,
and fixed false human-review/issuance flags.

The view does not persist or duplicate snapshots. It excludes selected center
IDs, source JSON, free-text notes, and unverified actor claims. It is explicitly
revoked from PUBLIC, has no runtime grants or RLS policy, and runs with
invoker permissions so underlying RLS remains in force. It is not registered
in DI, HTTP routing, workers, or a user interface. A successful integrity
check is not proof of identity, permission, source freshness, human review, or
issuance authority.

## Executable coverage

`national_issuance_review_inspection_dossier_integrity.psql` runs inside the
real S60/S61 disposable PostgreSQL fixture with 80 synthetic centers and
three append-only inspection notes. It verifies:

- the view is a SECURITY INVOKER view and contains no private source/free-text
  columns;
- all three notes appear exactly once, with valid original request and
  inspection digests and matching audit/outbox records;
- both changed-source inspections and the unchanged-source inspection remain
  distinguishable;
- all human-review and issuance flags remain false;
- both existing restricted SQL accounts lack SELECT privilege and a real
  restricted login cannot read the view.

Migration 0045 is also included in the full restricted API PostgreSQL setup.
The normal backend, PostgreSQL, Chromium, AI-contract and secret-scan CI gates
remain required.

## Not delivered

No verified reviewer identity, reviewer decision, authority grant/revocation,
actual issue command, center receipt, local-plan activation, public or HQ API,
Stage, QA gate, release approval, or Production deployment. The view is
technical evidence assembly only. Keep the Sprint PR Draft/Open/Unmerged.
