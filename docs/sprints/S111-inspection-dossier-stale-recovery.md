# Sprint 111 — Recover from stale dossier continuations

## Business outcome

Make conflict recovery clear to headquarters users and verify that restarting a changed dossier begins from page one while retaining the exact target and finding filter.

## Technical contract

- Label the dossier error action as a restart from the first page; it calls the existing fresh-read path and clears previously loaded pages.
- Keep the existing S110 contract, server-side 409 response, same-origin read scope, and CSV behavior.
- Do not retry a stale cursor silently or append data from a changed match set.

## Verification

- Browser coverage creates a filtered multi-page dossier, receives a server 409 on continuation, confirms the table is hidden, and restarts from page one.
- The recovery assertion checks that the exact program/version and finding code are retained while cursor and expected fingerprint parameters are removed from the fresh request.
- It confirms the fresh result replaces stale rows and that the updated row is shown under the retained finding filter.

## Boundaries

Recovery is an explicit user action. It creates no authority, review decision, approval, issuance, or production behavior.

## Release state

Draft PR only. No merge, Stage, QA gate, release approval, or Production action.
