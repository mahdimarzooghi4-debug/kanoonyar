# Sprint 87 — Eighty-center attendance history pagination

## Business
National rollout requires complete, center-scoped access to reported learning evidence at realistic list sizes. A paged history must not silently omit or repeat claims when many rows share a recording timestamp.

## Technical
Extend the required disposable PostgreSQL integration fixture to seed 43 synthetic attendance claims for each of 80 synthetic centers through the existing audited claim function. Exercise the restricted attendance-history reader across every keyset page. Verify the three expected page sizes (20, 20, 3), exact cursor handoff from each page's last row, descending timestamp/claim ordering, and equality between the requested synthetic claim IDs and the IDs returned across all pages. Check per-center participant-reference isolation, neighboring-center denial, explicit dual-center request scoping, revocation, center inactivation, and persistent UNVERIFIED status.

The evidence uses 3,440 unique synthetic claim IDs and synthetic participant references only. Runtime authorization remains default-deny; no endpoint, database permission, or operational policy is changed.

## Acceptance
- Required CI fails if the disposable admin/read database URLs are missing.
- Each of the 80 synthetic centers returns all 43 of its claims exactly once over pages of 20, 20, and 3.
- Each continuation cursor exactly matches the final row of its page; paired cursor values and strict descending key order are checked.
- Returned claim IDs and participant references belong only to the explicitly requested center, including for a subject with synthetic membership in two centers.
- Every history row remains UNVERIFIED; no attendance verification or derived KPI is created.
- Membership revocation and inactive registry state suppress the corresponding reads.

## Boundaries
No real center, participant, or attendance data; no human verification, public enrollment, production authorization, or authority to issue or approve a national notice is created. This is CI evidence only, not independent human approval or production readiness.

## Delivery
Stacked on Sprint 86. Keep Draft/Open/unmerged. No Stage, QA gate, release approval, or Production.
