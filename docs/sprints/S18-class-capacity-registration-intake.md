# Sprint 18 — Versioned Class Capacity & Unconfirmed Registration Request Intake

## Business linkage
KY-LMS-03, KY-AUD-03, WF-02, US-LMS-01 and AC-LMS-02/03 require class
capacity and duplicate/concurrent registration protection. Eligibility,
publication, consent, guardian proof, admission priority, waitlists, tuition
and approval policy remain undecided and must not be fabricated.

This slice implements **capacity planning** and **request-only intake**.
No confirmed registration exists: every intent is immutable `REQUESTED`,
`seatReserved=false`. `confirmedEnrollmentCount=null` means unknown/not implemented,
not zero. It is forbidden to compare requests to capacity as if seats were taken.

## Source ownership and contracts
Learning owns `class_capacity_revisions` with exact same-center class FK,
positive configured capacity, immutable incrementing revision, expected-revision
concurrency check under a transaction advisory lock and Audit/Outbox atomically.
Learning also owns `class_registration_intents` (UUID-only opaque applicant ref,
no name/phone/child data), exact capacity revision FK and unique
(center, class, applicant_ref) so repeated/concurrent intake cannot duplicate.
No real Audience identity, proof of eligibility or consent is inferred from UUID.

- `POST /api/v1/centers/{centerId}/classes/{classId}/capacity-revisions`
  body `{expectedRevision,capacity}`. Returns exact next revision,
  configured capacity, `confirmedEnrollmentCount:null`.
- `POST /api/v1/centers/{centerId}/classes/{classId}/registration-intents`
  body `{requestId,applicantRef}` (two non-empty UUIDs). Requires an
  existing explicit capacity version; pins the latest under the same lock.
  Returns request ID, pinned capacity revision, `state:REQUESTED`,
  `seatReserved:false`. No confirmation, accepted status or seat allocation.
- `GET /api/v1/centers/{centerId}/classes/{classId}/intake` with optional
  `beforeRequestId` exclusive UUID cursor. Returns nullable current
  capacity/revision, nullable confirmed count, and 20 paginated unconfirmed
  requests (ordered by ID not priority or submission time).

Each operation has a distinct `ICenterClassIntakeAuthorization` operation
(`CLASS_CAPACITY_REVISE`, `CLASS_REGISTRATION_INTENT_SUBMIT`,
`CLASS_INTAKE_READ`), all default DENY. JWT role and center membership
alone do not grant operational access. Only a constrained EXECUTE-only
SQL credential may invoke SECURITY DEFINER NOLOGIN-owner functions.
RLS, exact FK, live membership, collision protection, append-only triggers,
audit and outbox are mandatory. A PostgreSQL center GUC is not secure actor
authentication; real authorization/consent remains gated.

The center panel exposes class capacity, the unconfirmed request queue
and UUID-only intake, with strict server-response validation and protected
state reset on token/center/class switches. Existing class/session history
and reporting stay independent. No public enrollment portal and no new
identity/eligibility implementation.

## Acceptance
Unit fail-closed and contract tests; disposable PostgreSQL SQL + limited-login
integration verifying stale/concurrent capacity, duplicate applicant, no
configured capacity, center revocation, append-only rows and Audit/Outbox,
signed-JWT unauthorized operation, Chromium denial, stale data clearing,
and forged confirmed-seat response rejection. No Stage, QA, Ready for
Review, Merge, Production or official 80-center launch is authorized.
