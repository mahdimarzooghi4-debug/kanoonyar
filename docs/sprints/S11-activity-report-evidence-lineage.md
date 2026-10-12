# Sprint 11 — Unverified Activity Report and Evidence Reference Lineage

## Engineering slice
An activity report is an **unverified claim**, not an event executed,
attendance recorded, class delivered, outcome achieved, or evidence approved.
A report references an exact immutable national-issuance/local-plan link
and one opaque source record and evidence reference. The references are
NOT resolved or independently authenticated by this foundation.

## Data and controls
- Migration 0015 creates append-only report records with exact same-center
  composite FK to center_plan_issuance_links and original draft identity.
- A technical report_sequence supports stable exclusive pagination; its
  gaps are not evidence of activities at other centers or of work completed.
- Reported occurrence time can be unknown (NULL). It is never inferred.
- SECURITY INVOKER report command records report, audit and outbox atomically.
- PUBLIC EXECUTE and table privileges revoked; no runtime writer role exists.
- No status transition to VERIFIED and no invented verification thresholds.
- Real PostgreSQL tests prove 22 reports, two-page pagination, duplicate
  evidence rejection, invalid center/draft FK, rollback, immutability and RLS.
- A read API and center UI will expose unverified reports only after independent
  permission, restricted SQL login and live membership verification.

## Exclusions
No class/event producer integration, evidence object validation, file download,
financial act, numerical KPIs, verified completion, HQ reporting or Production.
Real users remain denied until an independent operation grant contract exists.
All work is on a stacked Draft PR, with no Stage/QA/Release/Production.