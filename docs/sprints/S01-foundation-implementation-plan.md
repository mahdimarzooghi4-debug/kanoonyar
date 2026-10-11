# Sprint 01 — Technical Implementation Plan
Status: DRAFT / PLANNING ONLY / CODE NOT AUTHORIZED
Base main: 8e81807b3d08614be8423ed2f83c8a6272a45b57
References: T01–T06, PB01

## Goal
Create a buildable, testable repository foundation for ASP.NET Core, three React applications, and a non-operational Python AI skeleton, without activating sensitive business workflows.

## Implementation sequence
1. Pin supported .NET SDK, Node.js and Python versions using official compatibility and CI evidence. Record exact versions in repository configuration; do not guess.
2. Create ASP.NET Core solution, API host, BuildingBlocks and module boundary conventions. Add a minimal health endpoint and error contract.
3. Create React/TypeScript workspace for headquarters, center and audience; shared RTL design primitives and smoke tests. Instructors use center app.
4. Create AI Python package skeleton and contract tests, without model, training data, production inference or direct operational DB access.
5. Add GitHub Actions jobs for backend build/test, frontend lint/typecheck/build, Python tests, secret scan and architectural boundary tests.
6. Review dependency direction, config safety, tenant boundary placeholders, CI logs and documentation before Code Review.

## Verification matrix
| ID | Test | Expected |
|---|---|---|
| S01-01 | .NET solution restore/build | Success on pinned SDK |
| S01-02 | Backend unit + architecture tests | Success; invalid module dependency detected |
| S01-03 | API health smoke | Explicit healthy response without real data |
| S01-04 | Build each React app | Three independent successful builds |
| S01-05 | RTL UI smoke | Persian direction and shared shell render |
| S01-06 | Python package tests | Success without external AI service/model |
| S01-07 | Secret/config audit | No real secrets, PII or credentials committed |
| S01-08 | CI exact SHA | All required jobs green for current PR HEAD |

## Security and scope boundaries
No real audience/child data, production identities, payment integration, delegated permission policy, scoring, AI production model or deployment. A green CI does not authorize Stage, QA, release or production.

## Decisions to resolve during planning
- Exact .NET SDK and frontend runtime/tool versions.
- Build/test package choices with compatibility verification.
- Host/project assembly layout and enforceable architecture tests.
- CI trigger, branch protections and reviewer requirements.
- Approval of Code start.

## Gates
Planning → explicit Code Authorization → Code + Tests → exact-head CI → Code Review. Keep PR Draft/Open; no merge, Stage, QA or Production without explicit instruction.
