# Sprint 102 — Headquarters inspection read scope contract

## Business outcome

Make every future headquarters inspection read-policy decision aware of the exact read surface and normalized data scope requested. A policy adapter must be able to distinguish aggregate pages, aggregate CSV exports, and one exact national-program version dossier.

## Technical contract

- Replace actor-only rollup and dossier authorization seams with one typed `INationalInspectionReadAuthorization` contract.
- Pass an operation discriminator plus the validated optional program/version target and validated findings filters to the policy adapter.
- Preserve explicit default-deny behavior for every operation. No role, grant, identity authority, review decision, or issuance rule is introduced.
- Invoke policy only after request validation and before opening the restricted read-only database connection.
- Keep page cursors out of the authorization scope: they navigate within the already described dataset and do not broaden its program/version or filter scope.

## Verification

- Unit tests assert exact normalized scope delivery for rollup pages, CSV exports, and dossier pages.
- Invalid/ambiguous query combinations remain rejected before database access.
- Existing PostgreSQL integration coverage runs through an explicit test-only allow adapter and retains its restricted database role checks.
- Route inventory asserts the production adapter remains `UnapprovedNationalInspectionReadAuthorization`.

## Boundaries

B02/T15 remain drafts. This is an adapter input contract only; it does not define which person, role, condition, or approval may read national inspection evidence. Production reads remain unavailable until the competent human process approves and implements a policy. CI and technical review do not constitute independent human approval.

## Release state

Draft PR only. No merge, Stage, QA gate, release approval, or Production action.
