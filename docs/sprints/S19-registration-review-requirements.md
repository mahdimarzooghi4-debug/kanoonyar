# Sprint 19 — Registration Human-Review Requirements (non-decisional)

## Business boundary
WF-02 / US-LMS-01 call for assessment of applicant rights, authorized
conditions and admission checks *before* confirmed enrollment. B04 explicitly
marks sensitive transitions and legal consent, guardian authority and
eligibility policy as unresolved. This package does not invent the outcome.

Learning owns an immutable, center-scoped **human review request** attached by
exact FK to an existing REQUESTED class registration intent from Sprint 18.
The four strictly bounded review topics are *questions only*:
IDENTITY_AUTHORITY, ELIGIBILITY_CRITERIA, CONSENT_AUTHORITY, ADMISSION_POLICY.
They mean a check is requested, NEVER that a person has given consent, that
a legal guardian has been verified, or that conditions are fulfilled.

One record per (center, registration request, topic), atomic Audit + Outbox,
immutable with FORCE RLS. No uploaded proof, ID documents, notes, phone,
names, real attendee identity, evidence ingestion, approval/rejection,
score, priority, seat occupation, capacity count or enrollment. The base
registration intent stays REQUESTED. No user-facing portal is enabled.

APIs:
- POST `/api/v1/centers/{centerId}/classes/{classId}/registration-intents/{requestId}/review-requests`
  body `{reviewRequestId,reviewTopic}`, reply echoes IDs, reviewStatus
  `REVIEW_REQUIRED`, enrollmentDecision `null`, seatReserved `false`.
- GET same path returns linked review requests (max four) and immutable
  registration state REQUESTED, enrollmentDecision null, seatReserved false.

A new independent per-operation `ICenterRegistrationReviewAuthorization`
is always bound to DEFAULT DENY in runtime; JWT role and membership alone
cannot approve it. Explicit permissions are REVIEW_REQUIREMENT_RAISE and
REVIEW_REQUIREMENT_READ; fail-closed on permission provider error. Restricted
EXECUTE-only SQL login to a NOLOGIN function owner, live membership check,
center GUC (never relied on as identity), dedicated read-only connection,
exact source id and class link. All test grants are synthetic.

In center UI show review needs from source-owned registration requests,
with strict response identity and status validation, no green eligibility
badge or confirmation. Do not treat an unraised topic as verified.

Acceptance: SQL atomicity and duplicate/topic/cross-center tests, restricted
Postgres handler integration, signed JWT default deny, Chromium state reset,
denial and forged VERIFIED rejection. No Merge, Ready for Review, Stage,
QA, Release or Production. Real identity/consent/eligibility connectors
and reviewer decision authority remain future Business decisions.
