# Sprint 68 — Current-source drift in the HQ inspection dossier

## Product contract

The S62 virtual technical dossier verifies that each immutable S61 observation matches its stored digest and original S60 request. Sprint 68 extends that read-only projection with a digest recomputed from the current national publication, selected-center registry states, and exact issuance records.

For each historical inspection note, the view reports the current source digest and whether it matches that note's captured observation. A mismatch means only that the underlying source has changed since that observation; it does not label the change valid or invalid, choose a reviewer, accept or reject the request, or authorize issuance. The historical note and digest remain unchanged.

The view remains SECURITY INVOKER, exposes no selected-center list or free-text note, and has no PUBLIC or restricted operational grant. No API, UI, role, RLS policy, reviewer identity, decision, issuance, or dispatch path is added.

## Verification

The disposable PostgreSQL review fixture applies migration 0048 after the S60-S67 chain with 80 synthetic centers. It checks the initial source digest format, distinguishes a historical inspection that predates a later synthetic issuance from a later matching inspection, confirms a separate unchanged request still matches, and verifies the digest-match flag is internally consistent. Existing tests continue to check append-only history, source/audit/outbox integrity, non-authorizing flags, and denied restricted SQL access.

This view evaluates current sources at query time. It does not provide a transactionally pinned, cross-query review snapshot or a human approval record. CI and technical COMMENT are not independent human approval or release authorization.
