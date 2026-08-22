# Web UI and REST API

Phase 7 serves a React, Vite, and TypeScript single-page application from the same ASP.NET process
and origin as `/api/*` and MCP. No CORS setup or second hosting product is required.

## UI

`web/` contains the frontend. It provides:

- streaming agent chat with visible tool calls and workflow steps;
- durable conversation create, open, rename, and delete actions;
- anonymous conversations held by unguessable ids in browser `localStorage`;
- optional Microsoft login and claim-on-login;
- image upload and deterministic workflow execution;
- run history, receipt index, validation score, and artifact preview.

For local frontend work, use Node 20:

```bash
cd web
npm ci
npm run dev
```

The production Docker build runs `npm run build` and copies `web/dist` into the host's `wwwroot`.

## REST surface

| Endpoint | Purpose |
|----------|---------|
| `GET /api/config` | Public runtime capabilities used to initialize MSAL |
| `POST /api/images` | Multipart image upload |
| `GET /api/workflows` | Workflow catalogue |
| `POST /api/jobs` | Direct tool or workflow run |
| `GET /api/runs[/{id}]` | Azure Table run history |
| `GET /api/artifacts/{reference}` | Stream a self-describing image reference |
| `GET /api/receipts[/{id}]` | Query structured SQL receipts |
| `/api/conversations/*` | Conversation CRUD, claim, history, and SSE turns |

The message endpoint uses `text/event-stream` with `token`, `tool`, `step`, `done`, and `error`
events. Every request supplies an idempotency key. The model session is serialized after a completed
turn and updated with SQL `rowversion`; a racing request receives `conversation_conflict` rather than
overwriting history.

## Microsoft login

Authentication is optional. Anonymous users retain durable conversations because the browser retains
their ids. After login, `/api/conversations/claim` assigns those rows to the Microsoft identity.

The Entra registration must:

1. allow accounts in any organizational directory and personal Microsoft accounts;
2. expose an `access_as_user` delegated scope under `api://<client-id>`;
3. register the deployed HTTPS origin as a SPA redirect URI;
4. set `OCR_Auth__Enabled=true`, `OCR_Auth__ClientId=<client-id>`, and
   `OCR_Auth__TenantId=common`.

The current project identity cannot create app registrations; a tenant administrator must perform
that one-time setup. The application remains fully functional anonymously until then.

## Verification

```bash
scripts/verify-webui.py https://<container-app-hostname>
```

The verifier covers the SPA, REST discovery, SQL receipt queries, multi-turn session restoration,
idempotent retry, and optimistic-concurrency conflict handling.
