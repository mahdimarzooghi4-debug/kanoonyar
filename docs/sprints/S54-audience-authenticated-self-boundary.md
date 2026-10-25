# Sprint 54 — Audience verified-self boundary (authentication only)

## Source and Business boundary

B01 nationwide three-environment product; B02 Identity and Access DRAFT;
B03 KY-AUD-01 through 05; B05A AC-AUD-01 through 05 and AC-REL-04.
S53 established public national program browsing from the original S04
published-only source. B02/T04 do **not** yet define legally valid
audience enrolment, a user-profile record, guardian consent/delegation,
local center membership or specific audience account rights. Sprint 54
does NOT invent them.

## Genuine source-backed behavior

- The audience portal now offers an **optional, explicitly
  developer/test-only** identity check next to unchanged public
  national-program discovery. It calls the existing original
  `GET /api/v1/identity/me` endpoint, not a new identity service
  or duplicate source. The backend validates signed JWT, configured
  HTTPS OIDC issuer, audience, signature, expiry and required `sub`.
- The user-provided opaque test token is in ephemeral React memory
  only. It is entered through a masked field, sent only as a Bearer
  header on a same-origin `GET /identity/me`, with credentials
  omitted, no-store and referrer no-referrer. There is no localStorage,
  sessionStorage, cookie, browser URL parameter, screenshot, logging,
  refresh-token integration, or background use. Editing, clearing
  or replacing a credential immediately invalidates the previous
  subject and aborts outstanding requests.
- Display only the allowlisted `subjectId` from the validated
  server response. If invalid, missing, malformed, revoked/expired,
  foreign issuer/audience, denied or unavailable, display no old
  identity. Late responses after credential change, explicit
  clear or revalidation must never restore stale identity.
- Public S53 browse/search/compare stays unauthenticated and
  independent. No private membership, center plan, form, class
  registration, attendee record, donation, contact information,
  guardian or consent APIs are called. No audience profile is
  created. A verified `sub` is never equated with an audience
  account, eligibility, approved relationship, operative permission
  or national rollout readiness.

This is a **non-production test credential UX**, not a finished
OIDC Authorization Code + PKCE login flow, credential custody scheme,
audience self-service account or role delegation. Production login
method, identity lifecycle and legal authority require separately
approved B02/T04 contracts. The existing S51 frozen operational
route manifest and default DENY adapters remain unchanged.

## Evidence / checks

Existing signed OIDC HTTP integration covers valid signed-sub tokens,
invalid signature, issuer, audience, expiry, missing sub and
default DENY regardless of role claims. Sprint 54 adds isolated
*real Chromium* audience tests for:
1. Public program list independently available without authentication.
2. Valid source `/identity/me` returning the exact own subject; extra
   forged role, membership, consent/guardian fields never rendered
   or converted into an authorization.
3. Rechecking after 401/403 and malformed/503 clears previous subject.
4. Identity verification never sends cookies, creates storage records,
   or calls private class/center/member/financial APIs.
5. Credential replacement and explicit clearing abort in-flight
   requests and prevent stale self-identity from reappearing.

Full unchanged Backend, restricted PostgreSQL, Frontend/Chromium,
AI boundary and Secret Scan CI must pass at the PR exact HEAD.

## Approval / operational restrictions

No audience database migration, provider-side account creation,
legal consent, actual verification of guardian status, published
center offerings, registration, booking, communication, admitted
place, financial settlement or automated human approval is included.
All these capabilities remain DENY unless their own Business,
Technical, QA and deployment gates are explicitly authorized.

PR remains Draft/Open stacked on PR #53, itself stacked on #52/#51.
No Ready for Review, merge, Stage, QA Gate, Release Approval or
Production. A technical review COMMENT is not independent human approval.
