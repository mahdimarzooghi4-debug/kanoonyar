# Sprint 98 — Verifiable national inspection CSV receipt

## Business
Make exported inspection evidence easier to verify after transfer, while proving the backend's 10,000-group limit against a real disposable PostgreSQL fixture.

## Technical
- Add a versioned response receipt with canonical row count and SHA-256 of the exact UTF-8 CSV bytes.
- Verify the receipt contract, row count range, content type, BOM, and file digest in the browser before starting download.
- Display the verified row count and digest in the workbench for the downloaded artifact.
- Seed 10,001 synthetic grouped inspection rows in PostgreSQL and verify the export returns HTTP 413 before any file result is produced.
- Verify the normal export receipt fields and digest against its returned bytes.
- Add a browser test proving that a mismatched digest blocks download, while preserving coverage for successful export and row-limit rejection.

## Boundaries
SHA-256 is a consistency checksum only: it is not a signature, a statement of authenticity, or an approval. Runtime read authorization remains default-deny and OIDC-required. The export remains aggregate-only and bounded. No authority, human review, issuance, or decision is created; PostgreSQL fixtures are synthetic and disposable.

## Acceptance
- A valid successful export response contains a versioned receipt, an exact row count no greater than 10,000, and SHA-256 for the exact file bytes.
- A malformed receipt, invalid BOM/type, or digest mismatch creates no download.
- A PostgreSQL result set of 10,001 groups returns 413 and is not returned as a file.
- Existing filters, aggregate CSV schema, and default-deny authorization remain intact.

## Delivery
PR remains Draft/Open/unmerged. No Stage, QA gate, release approval, or Production action is included.
