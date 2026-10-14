# Sprint 57 — Source-pinned public national-program version permalinks

## Actual operational need

B05 AC-NAT-04 and B05A AC-WEB-02/03/AC-REL-06 require
stable national-program version history without leaking unpublished
content or rewriting a previous publication. S53 introduced a
published-only audience catalogue; S56 fixed silent 500-row
truncation and made versions browseable in both Audience and HQ.
Neither provided a **shareable deep link to exactly one immutable
published version**. When a new version is published, recipients
must be able to return to the original source version rather than
having a bookmark quietly reinterpreted as "latest".

## Genuine original-source workflow

1. Original anonymous versioned source only:
   `GET /api/v1/programs/published/version?programId=...&version=...`.
   The handler requires EXACTLY one nonblank bounded canonical program
   ID and one canonical positive integer version, no other query keys,
   duplicates or half-key. Failure is 400. Both query values bind
   to typed SQL parameters in a fixed prepared query.
2. Reads only `programs.national_program_publications` INNER JOIN
   `programs.national_program_versions` by exact **original**
   `(program_id,version)`. No approved publication, no result.
   Unissued/unpublished/unknown version all result in **404**;
   no fallback to a newer published version, no leaking source
   authors or publication actor metadata. Database outage or
   missing read config remains **503**.
3. Audience lists genuine published versions as before. Each
   opened published version displays a native copyable, reloadable
   same-origin link `/?programId=...&version=...`. On opening
   it, the audience separately calls the original exact-version
   endpoint. It verifies that *returned source ID and version match
   the URL* and that the result satisfies the same allowlisted
   program/title/goals/requirements validation as the catalogue.
   Only verified exact public content appears in the pinned-detail
   region. A 404, invalid direct URL, malformed or forged 200,
   and a 503 display an explicit unavailable/invalid state,
   **never** a guessed program or substituted latest version.
4. General public listing remains separate, unauthenticated and
   available; it is not proof that a requested link's specific
   program version was issued. No center-local publication,
   recipient enrolment, participation, education, funding or
   verified outcome may be inferred.

## Verified security and test evidence

- S51 operational route inventory explicitly marks this **single**
  additional public GET as an audited exception; all existing 72
  identity-protected method/route pairs retain Authorize metadata
  in both OIDC-enabled and OIDC-unconfigured apps.
- S56's original real **restricted PostgreSQL** setup inserts
  502 distinct published versions and an unpublished version 503.
  The expanded test exercises this new exact-version handler with
  SELECT-only identity and proves 1, 500 and 502 resolve to their
  pinned stored titles/goals/keys, while 503, 999 and an unknown
  program ID are indistinguishable 404. It also checks no
  createdBySubject/publisherSubject field is exposed, malformed
  or ambiguous URL parameters are 400, and no storage is 503.
- Browser Chromium tests on isolated Audience exercise native
  permalink creation, direct reload to version 1 while
  catalogue latest is 3, exactly matching URL IDs, forged 200
  mismatch, 404 on unissued version 2, malformed/duplicated
  query keys preventing backend calls, and 503 requiring a
  genuine source. The existing full S53–56, OIDC, no-private
  browser artifact, PostgreSQL, AI and Secret Scan suites run.

## Non-goals and remaining approvals

No new roles or public center data. Program **publication
authority**, audience identity/consent, center-local course
publication, registration, payments, HQ reporting, real 80-center
onboarding and Production launch remain unapproved or incomplete.
Source-pinned version reference does not imply a legally effective
document, current mandate or decision beyond existing records.
Actual URL hosting/domain choice is deployment-scoped; link is
relative and does not invent a hostname.

Sprint 57 is on a separate Draft/Open PR stacked on Sprint 56.
Technical COMMENT is not human review or Release approval. No
Ready for Review, Merge, Stage, QA Gate, Release or Production
without explicit owner instruction.
