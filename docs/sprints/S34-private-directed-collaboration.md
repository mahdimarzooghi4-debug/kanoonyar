# Sprint 34 — Minimal directed collaboration proposal (KY-NET)

Business references: B03 KY-NET-01/03; B05A AC-NET-01/02/04, AC-ORG-02/03, AC-X-02/04/07.
The texts are DRAFT: this technical slice does not make new real policy approvals.

## Capability and security
- A member of center A with a separately approved **outgoing create grant** may
  explicitly enter center B's identity, a short plain-text title and a bounded
  plain-text summary intended for B. The only data shared with B is those
  exact fields, immutable proposal ID, source/target center ID and created time.
  Do not enter participant PII, private attachments or legal commitments.
  No automatic source-plan/class/participant references or copied source data.
- Table `collaboration.center_proposals`, origin/target canonical active center
  FK, immutable trigger, FORCE RLS using origin-or-explicit-target read scope,
  origin-only insert check. Both centers must exist; active origin membership
  checked in the atomic `SECURITY DEFINER` append command, active target
  checked before commit.
- Sender (source context): `POST /api/v1/centers/{centerId}/collaboration/proposals`,
  `GET /api/v1/centers/{centerId}/collaboration/outgoing`. Recipient (target
  context): `GET /api/v1/centers/{centerId}/collaboration/incoming`. Read returns
  20/page ordered by immutable proposal UUID with strict exclusive cursor.
  Reader only has column-level SELECT privileges for the six safe fields,
  **not** `created_by_subject` or any owner data or write capability.
- All paths require signed OIDC subject and independent operation-scoped
  `ICenterCollaborationAuthorization`; default DENY. SQL read requires
  separate restricted connection, live center membership and row-level policy
  with exact requested center scope. Writer login has EXECUTE-only command;
  append stores proposal + Audit + Outbox atomically. Duplicate proposal UUID
  gives 409 (no double audit). A chosen B does **not** gain authority to view
  any unrelated A records by virtue of this proposal.
- Recipient may see only an explicitly directed proposal, **not** an accepted,
  staffed, funded, booked or executed collaboration. Every response contains
  `reviewStatus="PROPOSED_UNREVIEWED"`, `isAccepted=false`,
  `isExecuted=false`. No approval/acceptance/rejection or public visibility
  command, delivery notification, financial/resource movement, or role
  delegation. Sender may read only its own outgoing; third centers see neither.
- The center UI has a compose form plus separate Outgoing and Incoming
  views, strict anti-forgery validation and per-identity/center reset. No
  fake recipient lookup, no implicit availability promise.
- Acceptance evidence: backend negative validators and signed-JWT role-only
  denial; disposable restricted PostgreSQL creation, duplicate/concurrent
  UUID protection, sender/recipient/third-center isolation, revoked members,
  inactive target refusal, restricted column-level reader and atomic
  Audit/Outbox; Chromium compose, incoming-only explicit visibility, false
  acceptance rejection and center switch privacy.

## Blocked follow-on policies
Real collaboration review/acceptance, delegation, task assignment,
cross-center personnel sharing, source-document authorization,
PII/children, notice/retention and actual shared resource transfers
need independently approved Business/Technical contracts. Initial
countrywide launch is not authorized by this Sprint.

Keep Draft/Open/Unmerged. No Ready for Review, merge, Stage, QA,
Release or Production without explicit owner approval.
