# Web UI and REST API

Phase 7 serves a React, Vite, and TypeScript single-page application from the same ASP.NET process
and origin as `/api/*` and MCP. No CORS setup or second hosting product is required.

## UI

`web/` contains the frontend. It provides:

- streaming agent chat with Markdown, image attachments, and persistent tool/run/workflow activity;
- durable conversation create, open, rename, and delete actions;
- an Auth0 Universal Login gate when production authentication is enabled;
- account-specific conversation history without automatic anonymous-history import;
- workflow or agent-assisted image processing with an in-place result workspace;
- searchable run history, generic artifact preview/download, and run detail;
- separate OCR confidence and consistency results;
- optimistic receipt correction with immutable revision history and corrected JSON/CSV exports.

The Web UI intentionally remains a single-image and conversation surface. Bulk dataset processing
is queue-only because the professor specifically requested it through the queue worker.

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
| `GET /api/config` | Public runtime capabilities used to initialize the Auth0 SPA client |
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

## Auth0 login and API access

Authentication is optional only for local/offline development. In the production configuration,
the compiled SPA and `GET /api/config` are public bootstrap resources, while every other REST route
and the HTTP MCP endpoint require either a valid Auth0 token carrying `access:ocr` or a valid named
`X-API-Key`. OAuth Protected Resource Metadata is also public. The UI shows a sign-in gate before it
issues protected API requests, redirects through Auth0 Universal Login, and attaches the resulting
access token afterward.

`python3 scripts/auth0-setup.py --domain <tenant-domain> --app-url <canonical-url>` configures:

1. an Auth0 API exposing `access:ocr`, whose audience equals the canonical `ResourceUri`;
2. a public SPA client for the browser;
3. a first-party native client for device flow and 24-hour user access tokens;
4. strict open DCR, a default third-party user grant, and the Resource Parameter Compatibility
   Profile for provider-neutral MCP clients; and
5. public domain, client, audience, scope, resource URI, and device-client identifiers in the
   gitignored `.env`.

Promote the Auth0 database, Google, and GitHub connections to the tenant domain so Universal Login
offers all three. The setup command consumes a short-lived `AUTH0_MANAGEMENT_API_TOKEN`; that token
and all raw API keys must never be committed or saved in `.env`. Named API-key configuration values
such as `OCR_Auth__ApiKeys__Professor` contain SHA-256 hashes only.

An enabled but incomplete authentication configuration stops application startup; it never silently
falls back to anonymous access.

The Auth0 Web flow was verified against the deployed application. A user completed Universal Login
and reached an account-specific conversation list; a different account in another browser received
an empty list. The previous Entra plan is historical, not current UI behavior.

## Verification

```bash
export OCR_AUTH_ACCESS_TOKEN="$(python3 scripts/auth0-token.py)"
scripts/verify-webui.py https://<container-app-hostname>
scripts/verify-webui.py https://<container-app-hostname> --image /path/to/receipt.jpg
```

Alternatively, set `OCR_AUTH_API_KEY` to the raw value printed once by
`python3 scripts/api-key-create.py <Name>`; the verifier then sends `X-API-Key`. The base verifier
first proves an anonymous protected REST request returns `401`, then covers the SPA, authorized REST
discovery, SQL receipt queries, persisted turn activity,
multi-turn restoration, idempotent retry, and conversation concurrency. `--image` additionally
checks chat attachment reload, a structured workflow result, artifact download headers, an
optimistic receipt edit, its revision, and corrected exports.
