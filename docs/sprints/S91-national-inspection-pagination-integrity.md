# Sprint 91 — National inspection pagination integrity

## Business
Keep the headquarters technical coverage workbench internally consistent when it reads multiple pages. A repeated program/version group can otherwise be displayed twice if the duplicate occurs after the first row of a later API page.

## Technical
- Treat each program/version pair as the stable identity of one aggregate row.
- Before retaining a fetched page, compare every returned pair with all rows on previously retained pages and reject any collision. Also reject duplicate pairs inside the newly received page as a defensive contract check.
- Keep the current page set atomic: a page containing a repeated pair is not made visible.
- Add a browser regression case where the first row on the later page is new and a subsequent row repeats an earlier group.

## Acceptance
- A clean page with new pairs remains navigable.
- A repeated pair at any position in a later page moves the workbench to the existing safe error state and displays no table.
- No row, private center evidence, or additional identity is introduced.

## Boundaries
This is read-path integrity only. It does not change database ordering, authorization, review identity, human approval, issuance, or operational state. Existing API access remains default-deny. CI and technical review are not independent human approval, Stage, QA, release approval, or Production evidence.
