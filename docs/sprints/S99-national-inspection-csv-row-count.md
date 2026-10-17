# Sprint 99 — Verify national inspection CSV row count

## Business
Make the CSV receipt verifiable against the actual file content so a correct checksum cannot hide an incorrect row-count claim.

## Technical
- Decode the complete CSV as strict UTF-8 after checking its BOM.
- Count logical CSV records rather than physical lines, respecting escaped quotes and CRLF inside quoted cells.
- Require the counted data rows to equal the canonical row count in the versioned S98 receipt before creating a download.
- Reject malformed record boundaries, unclosed quoted fields, invalid UTF-8, missing header records, and count mismatches.
- Extend browser coverage with a valid digest but incorrect row-count receipt, plus a successful CSV record containing an embedded CRLF.

## Boundaries
The receipt remains a byte-consistency aid. It is not a signature, authenticity claim, approval, or authorization. The aggregate export remains OIDC-required and default-deny. No human review, issuance, or operational decision is introduced.

## Acceptance
- A valid export with quoted multiline fields is counted by logical records and downloads only when the count matches the receipt.
- A digest-valid response with an incorrect row count creates no download.
- Existing digest mismatch, BOM, content type, 413, and authorization behavior remains intact.

## Delivery
PR remains Draft/Open/unmerged. No Stage, QA gate, release approval, or Production action is included.
