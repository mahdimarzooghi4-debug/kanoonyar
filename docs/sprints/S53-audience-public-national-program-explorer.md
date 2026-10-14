# Sprint 53 — Audience published national-program exploration (first real audience read workflow)

Based on B01 nationwide complete product principle, B02 three separate
experiences, B03 KY-AUD / KY-NAT-01/06, B04 WF-03 and B05/B05A
private-before-published and no-inferred-enrolment guarantees.

## What was materially connected

Before Sprint 53 the standalone public audience React experience was a
shared empty Foundation shell. The existing *public-only* S04 source
`GET /api/v1/programs/published` already returns genuine program-version
records linked to persisted national program publication events.
Sprint 53 connects that source to a **usable public audience portal**,
with no new endpoint, redundant database, username/consent claim,
permission grant or speculative public-center records:

1. Read ONLY the original S04 published-version endpoint, without
   credentials, with no-store HTTP requests and cancellable reload.
2. Validate each entire published response (bounded 500 records,
   safe version, stable program ID, title and arrays of displayable
   national goals/mandatory requirements). Missing/duplicated/unsafe
   records fail closed rather than treating unknown version or missing
   publication as approved. Only the four public allowlisted fields
   and version render; extra private/membership/content fields in an
   unexpectedly decorated response never reach the audience UI.
3. Group only genuine published records by source `programId`.
   Display the maximum **published version**, never the highest
   unissued draft or automatically promoted "current" version.
   Search across genuine published titles/identifiers; inspect
   national goals and mandatory requirements; optionally compare
   any **earlier published** version of the same program. Comparison
   does not invent changed business obligations, local execution,
   center publication/registration or eligibility.
4. On invalid response, 403/503, reload or out-of-order request,
   discard stale previously displayed details; no leaking of
   local drafts or guessed cross-center data.
5. Explicitly distinguish nationwide national-program publication
   from real class availability, actual center services, consent,
   registration, booking, payments, admissions and participation.
   No active affordance for any such restricted action appears.

This is **only the first public discovery slice** of the audience
experience, not a full audience account or service journey. S04's
existing Postgres source and independent headquarter and center
experiences remain unchanged.

## Executable evidence

New real Playwright Chromium suite under the isolated audience
deployment verifies original API exclusivity, published version
ordering/comparison and search, empty state, forged private fields
ignored, malformed and duplicate publication denial, 503 recovery,
and superseded asynchronous responses failing closed. Full
Foundation CI must pass Frontend+Chromium, Backend, restricted
PostgreSQL, AI boundary and Secret Scan.

## Decision blockers

No audience identity/profile, consent, guardianship, center
membership/relationship, localized publication, class/event
enrolment, acceptance, financial transfer, message delivery,
capacity/eligibility or official national compliance indicator.
Those require approved B02/T04 and B03/B04/B05 process contracts
and authority. No real data import or Production deployment.
Nationwide first launch is still DENIED BY DEFAULT until all
modules and release gates pass.

PR remains Draft/Open stacked on PR #52 (which remains stacked
on Draft/Open #51), with no Ready for Review, Merge, Stage, QA
Gate, Release Approval or Production absent explicit owner approval.
Automated Code Review COMMENT does not replace independent human
Code Review.
