# Three independent frontend release artifacts

The three Vite applications share source packages but produce distinct static roots:

| Experience | Build | Static document root | Manifest |
|---|---|---|---|
| Headquarters | `npm run build:headquarters` | `dist/headquarters/` | `dist/manifests/headquarters.json` |
| Center | `npm run build:center` | `dist/center/` | `dist/manifests/center.json` |
| Audience | `npm run build:audience` | `dist/audience/` | `dist/manifests/audience.json` |

After `npm ci`, run `npm run build` and `npm run test:deployment`. The deployment check runs negative fixtures, verifies each bundle has its own `index.html` and JavaScript, and writes file size and SHA-256 inventories. Each static root can be served independently by a standard static web server. Do not publish `dist/` as a single mixed document root; serve the specific experience root. Do not treat manifests as signatures or attestation from a trusted build service.

**Not implemented:** DNS, TLS, hosted deployment, origin isolation, production OIDC/session integration, tenant access checks, deployment approvals or production verification. Separate static artifacts are a build capability, not a release authorization. The three environments remain source-workspace siblings with shared UI primitives.
