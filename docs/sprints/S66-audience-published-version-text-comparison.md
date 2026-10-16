# Sprint 66 — Published national-version text comparison

## Product contract

The audience portal already reads only official published national-program versions and can display a selected earlier publication beside the latest published version. Sprint 66 adds a literal comparison for the published objective and mandatory-requirement text.

For each section, the comparison:
- matches complete strings exactly, preserving source wording, ordering and duplicate entries;
- labels entries as unchanged, added in the current publication, or present only in the earlier publication;
- keeps goals and mandatory requirements separate;
- renders source text as ordinary escaped React text.

The result describes text equality only. It does not infer legal meaning, priority, implementation status, center availability, eligibility, enrollment, consent or approval. The exact-version links and publication-only API remain the source of record.

## Verification

Playwright coverage checks additions and removals in both sections, an unchanged phrase, repeated identical entries, and the exact multiset behavior when one repeated phrase is removed. The existing audience suite continues to verify publication-only reads, version pinning, malformed data rejection and no registration or enrollment actions.

No backend route, identity, permission, decision, local-center execution or production deployment is added. CI and a technical COMMENT are not independent human approval.
