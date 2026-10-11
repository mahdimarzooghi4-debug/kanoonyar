# Sprint 01 — Foundation Code Review (first pass)
Status: REVIEWED WITH OPEN FINDINGS — NOT ACCEPTED
Follow-up: F01 RESOLVED on a064b47185319a149769bc439cd2308aca25e55f; CI 38126763262 and 38126760705 SUCCESS.
Reviewed HEAD: c3619fb34dc1835549bd207ad23a844135048a83
CI: 38123384134 SUCCESS; 38123382741 SUCCESS
Scope: Backend skeleton, Frontend, AI skeleton, CI, architecture test and local instructions.

## Findings
### F01 — RESOLVED: frontend dependencies are lockfile-reproducible
A generated `frontend/package-lock.json` is committed; CI uses `npm ci`. Both exact-head runs for a064b471 succeeded. Do not remove the lockfile or revert to `npm install`.

### F02 — Medium: three frontend experiences are build entries, not independent apps
Vite multi-page inputs create three HTML entrypoints within one workspace and one build artifact. The root demo entry still exists. T06's three apps with shared packages is not fully realized. Remedy: define independent build/test commands and output isolation, or explicitly approve a shared multipage deployment architecture.

### F03 — Medium: architecture test only scans two forbidden strings
The test detects two hard-coded references in the API source, not module-to-module assembly dependencies, unauthorized database access, or AI-to-operational data flows. Remedy: define enforceable project references/assembly rules and negative tests that prove a forbidden dependency is detected.

### F04 — Medium: test coverage is skeleton-only
Backend has a tautological test and a static source scan; no HTTP health endpoint integration test. Frontend has no automated rendered UI/RTL test. AI has only placeholder contract tests. Remedy: add executable API smoke and UI smoke; do not claim domain/security test coverage.

### F05 — Medium: CI security/reproducibility incomplete
Actions are pinned to major tags rather than immutable SHAs; SDK setup uses broad `10.0.x`, frontend Node `22`, and Python `3.11`. Dependency audit/secret scanning and dependency license/security review are not present. Remedy: pin/verify toolchains and actions according to approved security policy and add a separate scan gate.

### F06 — Low: solution/module skeleton incomplete
The code currently contains a minimal API project, not the proposed `Kanoonyar.sln`, BuildingBlocks and module boundary project structure. Remedy: add solution/project skeletons and validate graph.

## Confirmed positives
- Exact reviewed HEAD has two successful CI runs.
- Backend builds and foundation tests run; Frontend typechecks/builds; AI unit tests run.
- No operational AI model or real business workflows were introduced in reviewed files.
- PR remains Draft/Open and is not merged.

## Decision
Code Review is **not approved**. Fix findings in priority order, run exact-HEAD CI and repeat review. Do not merge, promote to Stage or claim Sprint acceptance.

## Follow-up after e112b873
- F02 partial evidence: both CI runs 38129063525 and 38129060520 passed the three-experience artifact/RTL checks. Build-output isolation is verified; independent app deployment is **not** verified. F02 remains open.
- F03 detector characterization added at 819719e3 with positive and negative fixtures. This tests only the existing string detector, not real module dependency graph or database isolation. F03 remains open pending structural enforcement.

## Follow-up after a05090ba
- Exact-head CI 38129207133 and 38129203700: SUCCESS.
- Added project-reference guard (355c5d90) for the API csproj, including explicit forbidden and permitted reference fixtures. This is a limited foundation safeguard, **not** full 14-module graph enforcement. F03 remains open.

## Follow-up after 25a71696
- Exact-head CI 38129908312 and 38129905491: SUCCESS, including ASP.NET Core in-memory HTTP tests for `/health/live` and `/health/ready`.
- F04 remains open for real browser/UI tests and future authenticated domain behavior; health endpoint HTTP coverage is verified.
- Frontend artifact checks were strengthened at 286b876a to inspect React entrypoints, shared UI shell imports and isolated JavaScript bundles. These are static/build tests, not real browser rendering tests.

## Browser CI acceptance evidence — 5d1d6f49
- Exact-head runs 38131112923 and 38131109324: SUCCESS.
- Frontend CI uses `npm ci`, builds three separate RTL bundles, and runs Chromium Playwright smoke tests against headquarters, center and audience previews. This closes the Foundation browser smoke subfinding of F04, not future domain-specific browser coverage.
- F01 lockfile reproducibility remains verified by `npm ci`; F02 independent deployment and F03 complete dependency graph remain open. F04 broader authenticated/domain tests remain open. No Stage/Production evidence is claimed.

## Solution and dependency graph follow-up — 9a36c48
- Exact-head CI runs 38132787744 and 38132784486: SUCCESS.
- Added `backend/Kanoonyar.slnx` containing API, Contracts and API Tests; CI builds/tests the complete solution.
- API now has an explicit dependency on the dependency-free Contracts project. `ProjectGraphBoundaryTests` validates existing production project-reference edges and rejects unauthorized edges, with corrected negative fixture (2f811303). This is current-project graph enforcement, not enforcement of future modules, package dependencies or runtime data flows. F03 remains open for expanded module graph governance.
- F06 solution-file subfinding resolved; module/BuildingBlocks skeleton still incomplete. PR remains Draft/Open; no Stage or Production acceptance.

## CI security follow-up — 07a423ed
- Exact-head runs 38133454567 and 38133451052: SUCCESS.
- Workflow has read-only default token permissions and a blocking `npm audit --omit=dev --audit-level=high` step for frontend production dependencies. Existing backend, frontend Chromium and AI jobs remained green.
- F05 remains open: GitHub Actions still use mutable major tags, SDK/toolchains are not fully immutable, npm development dependency and Python/.NET advisory scans and secret scanning are not yet covered. The npm audit is a narrow control, not a comprehensive security clearance.

## Immutable Actions follow-up — a7034487
- Exact-head CI runs 38133971829 and 38133968850: SUCCESS.
- All `actions/checkout`, `actions/setup-dotnet`, `actions/setup-node`, and `actions/setup-python` uses in Foundation CI are pinned to verified full upstream commit SHAs rather than mutable major tags.
- F05 remains open: comprehensive Python/.NET dependency advisories, development-dependency auditing, secret scanning and exact toolchain verification are not yet established. Pinning Actions does not itself establish full supply-chain security.

## Vite security follow-up
- Vite updated from 7.1.12 to 7.3.7 with regenerated npm lockfile.
- Exact-head Foundation CI 38135913185 and 38135917448: SUCCESS, including full npm audit and Chromium tests.
- F05 remains open for Python/.NET advisory checks and secret scanning.
