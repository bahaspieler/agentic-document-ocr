# Web UI and REST API

Phase 7 serves a React, Vite, and TypeScript single-page application from the same ASP.NET process
and origin as `/api/*` and MCP. No CORS setup or second hosting product is required.

## UI

`web/` contains the frontend. It provides:

- streaming agent chat with Markdown, image attachments, and persistent tool/run/workflow activity;
- durable conversation create, open, rename, and delete actions;
- anonymous conversations held by unguessable ids in browser `localStorage`;
- optional Microsoft login and claim-on-login;
- workflow or agent-assisted image processing with an in-place result workspace;
- searchable run history, generic artifact preview/download, and run detail;
- separate OCR confidence and consistency results;
- optimistic receipt correction with immutable revision history and corrected JSON/CSV exports.

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
| `GET /api/runs/{id}/artifacts[/{name}]` | List, preview, or download any run artifact |
| `GET /api/artifacts/{reference}` | Stream a self-describing image reference |
| `GET /api/receipts[/{id}]` | Query structured SQL receipts |
| `PATCH /api/receipts/{id}` | Save an optimistic, audited correction |
| `GET /api/receipts/{id}/revisions` | Immutable correction history |
| `GET /api/receipts/{id}/corrected.{json,csv}` | Corrected exports |
| `/api/conversations/*` | Conversation CRUD, claim, history, and SSE turns |

The message endpoint uses `text/event-stream` with `token`, `tool`, `step`, `run`, `artifact`,
`done`, and `error` events. Completed activity, exact run ids, and attachment references are stored
with the turn and restored without inspecting model-session internals. Every request supplies an
idempotency key. The model session is serialized after a completed turn and updated with SQL
`rowversion`; a racing request receives `conversation_conflict` rather than overwriting history.

## Quality and corrections

`OCR confidence` is the OCR engine's document score. `Consistency checks` is a separate normalized
score for required fields and arithmetic checks. A failed subtotal/items arithmetic check caps the
consistency score and the review label remains `Needs review`; a high consistency score does not
invent OCR confidence.

`RawJson` remains the extraction evidence. Editing writes `CurrentJson`, replaces the current
line-item projection, increments `CurrentRevision`, and inserts an immutable `ReceiptRevisions` row
inside one SQL transaction. The UI sends the receipt `rowVersion`; stale editors receive HTTP 409.

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
scripts/verify-webui.py https://<container-app-hostname> --image /path/to/receipt.jpg
```

The base verifier covers the SPA, REST discovery, SQL receipt queries, persisted turn activity,
multi-turn restoration, idempotent retry, and conversation concurrency. `--image` additionally
checks chat attachment reload, a structured workflow result, artifact download headers, an
optimistic receipt edit, its revision, and corrected exports.
