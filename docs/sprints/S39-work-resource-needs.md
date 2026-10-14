# Sprint 39 — Private planned equipment and resource needs for actual work cards

Business context: B03 KY-FIN-03, KY-LOC-02/04, KY-ORG-04;
B04 local center planning workflow. Business and approval contracts remain
DRAFT. **This capability records what might be needed**, not what is
owned, in stock, requested from a supplier, financed, allocated,
approved, bought, delivered, installed or fulfilled.

## Source-owned lifecycle
- Existing S33 `operations.center_work_cards`, with their exact immutable
  pinned local national-program plan version, are the only permissible
  source. No free-standing, fake or cross-center source/work card ID.
- Append-only center-private resource need identity pins exactly one
  original work card. Revisions (title 1–160, plain-text planning detail
  0–2000, optional timezone-aware desired-by date) are human-authored
  planning statements only. No price, unit, quantity, supplier,
  balance, stock ledger, accounting or fulfillment state is invented.
  Original source association cannot change.
- POST `/api/v1/centers/{centerId}/operations/work-cards/{workCardId}/resource-needs/revisions`
  requires client id, expectedRevision, label, detail and optional
  UTC desiredBy; checks live member and canonical work-card parent.
  Under a per-need advisory transaction lock, one expected revision
  succeeds, other races/duplicates are rejected; row+Audit+Outbox
  commit atomically, no personal note/detail in audit/outbox payload.
- GET same base path returns at most 20 planning records at a time
  ordered by immutable UUID with exclusive `beforeNeedId` cursor.
  GET `.../resource-needs/{needId}/revisions` returns immutable
  historical versions 20/page. Read uses REPEATABLE READ snapshot
  and checks the actual work-card owner first; fake/cross-card IDs
  return 404, not an unauthorized private document.
- Every result declares PRIVATE_PLANNING, fulfillmentStatus=UNKNOWN,
  isAllocated=false, isPurchased=false, isFulfilled=false. These are
  **absence of claims** rather than verified inventory or procurement.
- Actor authorization: signed OIDC subject, active membership in
  requested center, independent operation grant
  WORK_RESOURCE_NEED_APPEND/LIST_READ/HISTORY_READ, separate
  WORK_CARD_LIST_READ and LocalPlanDraftRead source grants.
  Runtime grants default DENY. Writer EXECUTE-only via NOLOGIN
  SECURITY DEFINER owner; reader SELECT-only, FORCE RLS both tables,
  reject privileged/settable SQL principals. Never authorize solely
  from a JWT role, route id or session GUC.
- UI appears within actual S33 work-card owner panel only after
  source cards are read; supports new and revised needs, immutable
  history, cursor paging, private identity switch reset and strict
  anti-forgery acceptance validation; rendering is escaped plain text.
- Automated evidence: API input/JWT negative tests; disposable
  PostgreSQL true owner cards and plan pins, non-member/foreign center,
  stale and concurrent versions, immutability, atomic Audit/Outbox,
  SQL privilege checks, reader paging, and real Chromium positive,
  forgery denial and operation grant denial.

## Deferred
Inventory counts, equipment ownership, item valuation, procurement,
spending, provider coordination, warehouse reservation, resource
allocation to work items, finance audit and national financial reports
require separate approved business/technical/financial contracts.
No synthetic Production grants or Stage/Release claims.
PR Draft/Open/unmerged; no Ready, Merge, QA Gate, Stage or Production.
