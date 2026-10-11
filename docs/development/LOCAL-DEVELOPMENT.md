# Kanoonyar — local foundation development

Status: Foundation only. No production configuration, identity, audience records or financial operations.

## Prerequisites
- .NET SDK compatible with `backend/global.json` (10.0.100 with latestPatch roll-forward).
- Node.js 22 and npm for the frontend workspace.
- Python 3.11+ for the AI contract skeleton.

## Backend
```sh
dotnet build backend/src/Kanoonyar.Api/Kanoonyar.Api.csproj -c Release
dotnet test backend/tests/Kanoonyar.Api.Tests/Kanoonyar.Api.Tests.csproj -c Release
dotnet run --project backend/src/Kanoonyar.Api/Kanoonyar.Api.csproj
```
Only `/health/live` and `/health/ready` are exposed by this skeleton.

## Frontend
```sh
cd frontend
npm install --no-audit --no-fund
npm run typecheck
npm run build
npm run dev
```
The Vite build uses three HTML entrypoints: `apps/headquarters/index.html`, `apps/center/index.html` and `apps/audience/index.html`. They are **not yet separately deployed apps**. The earlier root demo entry remains for development and should be retired after verified independent app packaging.

## AI contracts
```sh
cd ai
python -m pip install -e .
python -m unittest discover -s tests -v
```
This is a non-operational contract package; no PyTorch model is installed or executed.

## Safety
Never commit credentials, identity tokens, personal data, financial records or real child/guardian data. Use synthetic test fixtures only. CI success is not Stage, QA or Production approval.
