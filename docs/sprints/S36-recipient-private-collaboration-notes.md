# Sprint 36 — Recipient-only private collaboration note register

Business context: B03 KY-NET-01/03 and KY-ORG-05; B05A AC-NET-01/02, AC-X-02/04/07.
Business documentation remains DRAFT. No collaboration acceptance/rejection, staffing,
resource allocation, real delivery, or publication is authorized here.

## Product behavior
- A current member of the **explicit destination** center B, subject to two
  separate effective/revocable rights (`COLLABORATION_INCOMING_READ` and
  operation-specific `COLLABORATION_RECIPIENT_NOTE_APPEND` /
  `COLLABORATION_RECIPIENT_NOTE_READ`), may write internal
  *free-text planning notes* about a real S34 proposal explicitly sent by A to B.
- Plain text only, 1–1200 chars; no attachments, PII/children records, private
  source documents or derived resource permissions. No decision vocabulary or
  change to S34's `PROPOSED_UNREVIEWED`. The sender A **must not** receive B's
  internal note text, author identity or review metadata, even if A created
  the underlying proposal. Other centers must see neither notes nor existence.
- `collaboration.recipient_private_notes` has exact composite FK
  `(proposal_id,target_center_id)` to canonical S34
  `collaboration.center_proposals`, immutable append-only row, FORCE RLS
  targeting only B, and no UPDATE/DELETE path. A duplicate note UUID fails
  closed and produces no extra audit/outbox.
- POST + GET
  `/api/v1/centers/{centerId}/collaboration/proposals/{proposalId}/recipient-notes`.
  Append commits note + Audit + Outbox in one transaction via a NOLOGIN
  SECURITY DEFINER scoped owner that checks canonical recipient identity
  and active live membership. Writer login gets EXECUTE only, reader login
  SELECT only on safe allowlisted note columns (no `recorded_by_subject`).
  No source-table or credential inheritance rights.
- Read uses a PostgreSQL REPEATABLE READ snapshot, active center membership,
  explicit destination proposal existence check and at most 20 notes/page
  by descending UUID with exclusive `beforeNoteId` cursor. Zero notes for
  an existing proposal is a valid empty register, **not** an approval.
- Signed OIDC subject required; independent grants default DENY. GUC scope,
  JWT role alone or shared login cannot grant cross-center read.
  All responses are `RECIPIENT_INTERNAL`,
  `reviewStatus=PROPOSED_UNREVIEWED`, `isAccepted=false`,
  `isExecuted=false`.
- Center UI adds a private note workspace only on the incoming proposal
  view, with explicit text, append, read, safe denial/conflict behavior and
  center/token unmount reset. This must not display notes alongside outgoing
  proposals.
- Tests: signed JWT role-only denial; disposable PostgreSQL for A→B and
  A→C, no notes leak to A or C, revoked target membership, spoofed origin,
  duplicate note id, actual immutable rows, Audit/Outbox atomicity and
  restricted column privileges, browser valid entry and forged acceptance
  refusal. The current runtime has no real business grant evaluator
  wired, therefore default DENY everywhere.

## Deferred
Formal recipient response/approval/decline lifecycle, authorizations
for sharing notes with sender, licensed content, personal data,
real resource moves, messages/notifications, and reporting outcomes
need explicit independent Business/Technical approvals.
Keep PR Draft/Open/unmerged. No Ready, Merge, Stage, QA Gate, Release or Production.
