# Configuration

How `OcrAgent` (console), `OcrAgent.Mcp` (server), and the queue worker are configured. The course
host retains its `MyConfig` section alongside the engine's `OCR_*` settings.

## Sources, in precedence order

1. Environment variables
2. `.env` file
3. `appsettings.{DOTNET_ENVIRONMENT}.json`, when that variable is set and the file exists
4. `appsettings.json`

Both applications call `EnvFile.Load()` at start-up. It searches for `.env` from the working
directory upwards, then from the application directory upwards, so one file at the project root
serves the console app, the MCP server, and the helper scripts. A variable already exported in the
shell is never overwritten, which keeps a container's real settings ahead of a stray file.

The JSON files are read from the working directory when it holds an `appsettings.json`, and from the
directory of the assembly otherwise. Starting a process in a folder with its own settings is a
deliberate choice, so it wins; the copy beside the assembly is the one that ships, which is what the
container uses, where the working directory is the filesystem root. Relative storage paths resolve
against whichever of the two was used, not against the working directory.

Every engine entry point loads configuration through one `EngineHost.Create()`, so the console,
server, and queue worker cannot disagree about engine settings.

Copy `.env.example` to `.env` and fill it in. `.env` is gitignored.

## Keys

Keys prefixed `OCR_` override `appsettings.json`, with a double underscore per nesting level:
`OCR_Runs__TableName` sets `Runs:TableName`.

| Key | Purpose |
|-----|---------|
| `AZURE_STORAGE_CONNECTION_STRING` | Storage account, used by both the image store and the run store |
| `OPENAI_API_KEY` | Chat model credentials, needed only for the agent |
| `OPENAI_CHATCOMPLETION_DEPLOYMENT` | Chat model to use (default `gpt-4o-mini`) |
| `OCR_Storage__Provider` | Image store: `local`, `sftp`, or `azureblob` |
| `OCR_Storage__AzureBlob__ContainerName` | Container holding images, sidecars, and run artifacts |
| `OCR_Storage__AzureBlob__BlobPrefix` | Optional prefix, so several projects can share a container |
| `OCR_Runs__Provider` | Run records: `none` to disable, or `azuretable` |
| `OCR_Runs__TableName` | Table holding run records (default `OcrRuns`) |
| `OCR_Runs__GroupId` | Partition key for this deployment's records |
| `OCR_Sql__Enabled` | Enables durable conversations, users, and receipt indexing |
| `OCR_Sql__ConnectionString` | Encrypted Azure SQL connection string (Container Apps secret `sql-cs`) |
| `OCR_Auth__Enabled` | Requires either a scoped Auth0 JWT or valid API key on production REST and HTTP MCP |
| `OCR_Auth__Domain` | Auth0 tenant domain without a scheme, for example `tenant.eu.auth0.com` |
| `OCR_Auth__ClientId` | Public Auth0 SPA client ID used by the Web UI |
| `OCR_Auth__Audience` | Auth0 API identifier; must equal the canonical `ResourceUri` |
| `OCR_Auth__ScopeName` | Required delegated permission, `access:ocr` |
| `OCR_Auth__ResourceUri` | Canonical deployed origin advertised through OAuth Protected Resource Metadata and used for MCP RFC 8707 resource parameters |
| `OCR_Auth__ApiKeys__Name` | SHA-256 hash of one named static API key; repeat with a distinct `Name` for each credential |
| `AUTH0_DEVICE_CLIENT_ID` | Public client ID of the first-party native device-flow application used by `auth0-token.py` |

The queue host also reads these unprefixed variables through the course template:

| Key | Purpose |
|-----|---------|
| `MyConfig__StorageConnectionString` | Course Queue, training/result Blob containers, and result Table |
| `MyConfig__GroupId` | Result row partition key |
| `MyConfig__TrainingContainer` | Source images; bulk datasets use `images/<dataset-name>/` within this container |
| `MyConfig__ResultContainer` | Graded output artifacts |
| `MyConfig__ResultTable` | Single-image rows plus bulk parent and child `ExperimentResult` rows |
| `MyConfig__Queue` | Trigger queue |
| `MyConfig__WorkRoot` | Writable temporary worker files |
| `MyConfig__MessageVisibilityTimeoutSeconds` | Queue message processing lease |
| `MyConfig__MaximumBatchImages` | Maximum images in one bulk dataset (default 2,000) |
| `MyConfig__MaximumDequeueAttempts` | Failed deliveries before `<queue>-poison` (default 5) |

The deployment helpers accept `COURSE_STORAGE_CONNECTION_STRING` as a worker-only override and
otherwise reuse `AZURE_STORAGE_CONNECTION_STRING`. Course entity names are independently
overridable; see [QUEUE_WORKER.md](QUEUE_WORKER.md).

With the default training container, the canonical bulk prefix is
`training-files/images/<dataset-name>/`. The worker runs one replica. A bulk parent persists its
AI-selected and validated `PlanJson` before deterministic fan-out; child messages reuse the plan
without per-image AI and skip work when their stable item result is already present.

`scripts/azure-queue-produce.sh` uses the same connection-string order and the canonical
`MyConfig__TrainingContainer` and `MyConfig__Queue` names. Command-line options can override the two
entity names for one invocation. `verify-queue-worker.py` also reads the `MyConfig__*` names, with
the older `COURSE_*` entity-name aliases retained as fallbacks.

Neither store needs its connection string set explicitly. When `Storage:AzureBlob:ConnectionString`
or `Runs:ConnectionString` is empty, the factory falls back to `AZURE_STORAGE_CONNECTION_STRING`.

## Sections

`Storage` selects and configures the image store; see the provider classes in `OcrAgent/Storage/`.
`Runs` selects and configures the run store and is documented in [RUN_RECORDS.md](RUN_RECORDS.md).
`Sql` configures durable conversations, user profiles, and structured receipt data. `Auth` configures
generic Auth0 JWT validation, named static API keys, and OAuth Protected Resource Metadata.
Authentication is disabled in the checked-in defaults for offline development. When enabled, the
host fails at startup unless the Auth0 domain, SPA client ID, scope, audience, and resource URI are
complete. `Audience` must equal the canonical `ResourceUri` (ignoring a trailing slash) so
provider-neutral MCP clients can send RFC 8707 `resource` parameters under Auth0's Resource
Parameter Compatibility Profile.

The server also publishes authorization-server/OpenID compatibility metadata on its own origin.
The advertised authorization endpoint preserves a client's callback, state, PKCE challenge, and
scope, adds the configured Auth0 audience and resource when the client omits them, and redirects to
Auth0. OAuth client IDs and `access:ocr` are public identifiers. Client secrets, access/refresh
tokens, API keys, storage connection strings, and Management API tokens are credentials.

When authentication is enabled, these public bootstrap routes support client discovery:

- `GET /.well-known/oauth-protected-resource` and
  `GET /.well-known/oauth-protected-resource/mcp`: protected-resource metadata;
- `GET /.well-known/oauth-authorization-server` and its `/mcp` form:
  authorization-server metadata;
- `GET /.well-known/openid-configuration`, its `/mcp` form, and
  `GET /mcp/.well-known/openid-configuration`: OpenID compatibility metadata; and
- `GET /oauth/authorize`: PKCE-preserving redirect to Auth0 with audience/resource injection.

Stateless MCP requests use `POST /mcp`. `GET`, `HEAD`, and `DELETE /mcp` intentionally return 405
instead of falling through to the Web UI's HTML.

Production `/api` and `/mcp` requests accept either a bearer JWT issued by the configured Auth0
domain for the configured audience and carrying `access:ocr`, or an `X-API-Key` whose SHA-256 hash
matches a named `ApiKeys` entry. The static UI, `/api/config`, and OAuth Protected Resource Metadata
remain public bootstrap resources.

## Scripts

`scripts/run-mcp-blob.sh` builds and starts the MCP server with the blob image store, frees the port
first, and relies on `.env` for everything else. `OCR_PORT`, `OCR_CONTAINER`, and `OCR_BLOB_PREFIX`
override per invocation.

`scripts/azure-sql.sh` creates the Basic database and writes its generated credentials only to the
gitignored `.env`. Azure hosting is documented in [DEPLOYMENT.md](DEPLOYMENT.md). The Azure helpers
read the same `.env` file. Pointing an MCP client at either server is covered in
[MCP_CLIENT_TESTING.md](MCP_CLIENT_TESTING.md).

Export a short-lived `AUTH0_MANAGEMENT_API_TOKEN`, then run:

```bash
python3 scripts/auth0-setup.py \
  --domain <tenant>.eu.auth0.com \
  --app-url https://<canonical-app-hostname>/
unset AUTH0_MANAGEMENT_API_TOKEN
```

The canonical `--app-url` becomes both the Auth0 API audience and `ResourceUri`. The script creates
or updates the API, public SPA, first-party native device-flow client, strict open DCR, the default
`access:ocr` third-party user grant, and the Resource Parameter Compatibility Profile. It writes
only public identifiers to the gitignored `.env`. Auth0 Universal Login also requires the database,
Google, and GitHub connections to be promoted to the tenant domain.

`--app-url` must be the canonical HTTPS production origin, including its trailing slash.
`auth0-setup.py` intentionally rejects `http://localhost`; local authentication can be disabled or
configured manually without changing the production audience.

An MCP application's client ID is client-side configuration and is not read from the server's
`.env`. `OCR_Auth__ClientId` belongs to the Web UI SPA. MCP clients either obtain their own ID
through DCR or use a separately registered public client whose callback URI matches that MCP
application.

`python3 scripts/auth0-token.py` performs device authorization through the first-party native client
and prints only the user access token to stdout. The configured API policy gives these user tokens a
24-hour lifetime. Use command substitution and do not persist the token.

Generate a named static credential with `python3 scripts/api-key-create.py Professor`. The script
prints the raw key once and stores only its SHA-256 hash as
`OCR_Auth__ApiKeys__Professor`; clients send the raw value in `X-API-Key`. Raw API keys and Auth0
Management API tokens must never be put in `.env`, committed, logged, or included in screenshots.
Generating a key updates the local `.env` only. Run `scripts/azure-deploy.sh --skip-build` to
publish the new hash through Container Apps configuration before using that key against production.

The Auth0 tenant and production settings are configured, and the deployed protection was verified
with interactive Web and MCP login, anonymous rejection, OAuth metadata, and named API-key
REST/MCP access. Microsoft Entra is historical only: it was abandoned after the DAENET tenant
denied the project identity permission to create app registrations.

Docker Compose passes `.env` to both roles. It explicitly maps the worker's course settings because
the worker uses `MyConfig__StorageConnectionString`, while the serving role uses
`AZURE_STORAGE_CONNECTION_STRING`.
