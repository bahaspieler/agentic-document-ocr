# Deployment

The serving instance and course queue worker run as separate Azure Container Apps pulled from one
image. The serving app has HTTPS ingress; the worker has no ingress and continuously polls the
configured course queue.

Images enter through Blob Storage. A file named `DEPLOY01.jpg` in the configured container is the
reference `DEPLOY01`. `RegisterImage` is a local convenience that needs a path the server can read,
and a deployed container has no shared filesystem with the client.

## Resources

All of these live in `RG_Hidden_Layer`. The scripts never create or delete the group.

| Resource | Default name | Purpose |
|----------|----------------|---------|
| Resource group | `RG_Hidden_Layer` | Team group; bound, never deleted |
| Storage account | `sthiddenlayerocr` | Blob images and Table run records; never touched by teardown |
| Container registry | `acrhiddenlayerocr` | Holds the `ocr-agent` image |
| Container Apps environment | `cae-ocr-agent` | Hosts the app |
| Container app | `ca-ocr-agent` | Web UI, REST API, and MCP server over one HTTPS origin |
| Worker container app | `ca-ocr-worker` | Course Queue → Blob → experiment → Blob/Table path |
| Azure SQL server | `sql-hidden-layer-ocr` | Logical server for serving-application data |
| Azure SQL database | `ocr-agent` | Basic tier: conversations, users, receipts, line items, and correction revisions |

Names are overridable in `.env` (`AZURE_RESOURCE_GROUP`, `AZURE_ACR_NAME`,
`AZURE_CONTAINER_APP_NAME`, `AZURE_WORKER_APP_NAME`, `AZURE_CONTAINER_APP_ENV`, and the SQL names),
as is the replica ceiling (`AZURE_MAX_REPLICAS`).

The scripts resolve the storage account's region for new resources and fall back to the resource
group's location only when the account cannot be resolved. Every workflow step reads an image and
writes another, so a container in a second region pays a round trip on each one.
`RG_Hidden_Layer` is registered in East US, `sthiddenlayerocr` is in Germany West Central, and the
deployed resources sit in Germany West Central with the data. A group's location is only the default
for new resources and is not where the data is. `azure-deploy.sh` warns if it finds an existing
environment somewhere other than the selected region; a Container Apps environment cannot be moved,
so the fix is a teardown and fresh deploy, and the app gets a new hostname.

## Scripts

```bash
scripts/azure-sql.sh               # create/update SQL when durable Web data is required
python3 scripts/auth0-setup.py --domain DOMAIN --app-url URL
python3 scripts/auth0-token.py      # first-party device-flow user token
python3 scripts/api-key-create.py Professor  # named static credential
scripts/azure-deploy.sh            # create/update serving; builds linux/amd64 in ACR
scripts/azure-deploy.sh --skip-build
scripts/azure-worker-deploy.sh     # create/update the no-ingress worker
scripts/azure-queue-produce.sh     # upload an image and enqueue manual commands
scripts/azure-start.sh             # activate and keep min 1
scripts/azure-stop.sh              # deactivate the revision; URL stays
scripts/azure-worker-start.sh      # activate the worker after it was stopped
scripts/azure-worker-stop.sh       # deactivate only the worker
scripts/verify-deployment.sh       # upload a receipt, run MCP checks, assert Blob and Table
python3 scripts/verify-webui.py URL # verify SPA, REST, SQL, SSE, idempotency, concurrency
python3 scripts/verify-webui.py URL --image receipt.jpg  # also verify artifacts and corrections
python3 scripts/verify-queue-worker.py  # enqueue two commands and verify Blob/Table results
scripts/azure-worker-teardown.sh   # delete only the worker
scripts/azure-teardown.sh          # delete serving, environment, and registry
```

`azure-deploy.sh` is idempotent. It registers the `Microsoft.App` and `Microsoft.OperationalInsights`
providers if needed, creates the registry and environment when they are missing, builds a uniquely
tagged image with `az acr build --platform linux/amd64`, and creates or updates the app. The storage
connection string is a Container Apps secret, not an image layer. The model and SQL connection
strings are stored the same way as `openai-key` and `sql-cs`.

The serving deployment is fail-closed: `azure-deploy.sh` refuses to continue until the Auth0
domain, SPA client ID, audience, and canonical resource URI exist in `.env`. Obtain a short-lived
Auth0 Management API token with only the permissions needed for setup, then run:

```bash
export AUTH0_MANAGEMENT_API_TOKEN="<short-lived-token>"
python3 scripts/auth0-setup.py \
  --domain <tenant>.eu.auth0.com \
  --app-url https://<container-app-hostname>/
unset AUTH0_MANAGEMENT_API_TOKEN
```

The canonical `--app-url`, including its trailing slash, becomes both the Auth0 API audience and
`OCR_Auth__ResourceUri`. Their equality is required for MCP RFC 8707 compatibility. The helper
creates or updates an RS256 API with `access:ocr`, a public SPA, a first-party native device-flow
client with a 24-hour API access-token policy, strict open DCR with a default third-party user
grant, and Auth0's Resource Parameter Compatibility Profile. Provider-neutral clients such as
Cursor, Claude, and Copilot-compatible clients can then perform automatic OAuth DCR.

Promote the Auth0 database, Google, and GitHub connections to the tenant domain so Universal Login
can offer them. The script writes only public runtime identifiers to the gitignored `.env`. The raw
Management API token must never be saved there, committed, logged, or included in screenshots.

The earlier Microsoft Entra approach was abandoned because the DAENET tenant denied the project
identity permission to create app registrations. Entra is not part of the current design.

`azure-worker-deploy.sh` reuses the image already selected by the serving app. It supplies the course
`MyConfig__*` values, local OCR work storage, and the optional model key. It does not supply SQL or
authentication settings. The worker is deployed with one replica so one consumer owns parent
planning and deterministic child processing.

The supported operational order is:

1. Run `azure-sql.sh` when SQL does not already exist.
2. Configure the Auth0 tenant with `auth0-setup.py` using the canonical deployed URL, promote the
   Universal Login connections, and unset the Management API token.
3. Run `azure-deploy.sh`, then `azure-worker-deploy.sh`.
4. Run `azure-worker-start.sh` only when a previously stopped worker must be reactivated.
5. Submit manual work with `azure-queue-produce.sh`, or run the queue verifier.
6. Obtain an Auth0 user token with `auth0-token.py`, or export a generated raw API key, and run the
   serving and Web verifiers.
7. Stop each role independently, or use the explicit teardown scripts when deletion is intended.

The manual producer uploads one image and publishes only the commands supplied by the operator. It
does not poll for results:

```bash
scripts/azure-queue-produce.sh \
  --image SampleFiles/images/RCPT001.png \
  --message-file SampleFiles/queue/remove-background.txt \
  --message-file SampleFiles/queue/extract-receipt.txt
```

Use `verify-queue-worker.py` instead when an end-to-end assertion is required. It creates the course
entities if missing, uploads the image, publishes its two built-in commands, waits for both rows, and
checks every output Blob.

The replica range is min 1 / max `AZURE_MAX_REPLICAS`, which defaults to 3, so the submitted Web UI
stays live. MCP is stateless and REST conversations live in SQL, so Container Apps can spread requests
without session affinity. Blob, Table, and SQL are shared by every replica.

Container Apps will not accept `max-replicas 0`, so `azure-stop.sh` deactivates the active revision
instead. That stops compute without deleting the app or its hostname.

`azure-teardown.sh` prints the named resources, requires `yes`, and refuses anything else. It never
calls `az group delete` and never touches the storage account. The Log Analytics workspace the
environment created is reported and left alone unless `--include-logs` is passed.

## Verification

```bash
export OCR_AUTH_ACCESS_TOKEN="$(python3 scripts/auth0-token.py)"
scripts/verify-deployment.sh
scripts/verify-deployment.sh --image /path/to/receipt.jpg
python3 scripts/verify-webui.py https://<container-app-hostname>
```

Before executing tools, the verifier proves an anonymous MCP request receives `401` with an OAuth
protected-resource metadata challenge. The Web verifier independently proves an anonymous REST call
receives `401`. Both then use `OCR_AUTH_ACCESS_TOKEN` for authorized requests. To verify the static
credential path instead, generate a named key once, retain its displayed raw value securely, and
export it only for the client process:

```bash
python3 scripts/api-key-create.py Professor
export OCR_AUTH_API_KEY="<raw-key-printed-once>"
unset OCR_AUTH_ACCESS_TOKEN
scripts/verify-deployment.sh
python3 scripts/verify-webui.py https://<container-app-hostname>
```

The generator stores only the SHA-256 hash as `OCR_Auth__ApiKeys__Professor`; requests carry the
raw value in `X-API-Key`. Raw API keys must never be committed or placed in `.env`.

The Auth0 deployment was verified. The healthy active revision rejected
anonymous access, returned JSON protected-resource metadata, accepted interactive Web login and a
named API key, and passed REST, SSE, MCP, OCR, Blob, Table, and SQL assertions.

The script uploads the file to Blob twice, as `DEPLOY01.<extension>` and `DEPLOY02.<extension>`, drives
[verify-blob-store.py](../scripts/verify-blob-store.py) against the deployed HTTPS URL, then lists
blobs under each run's `wf_receipt_extract_v3/<runId>/` prefix and queries the `OcrRuns` table for
the matching row.

The second reference exists so the two concurrent runs both meet an image no replica has cached yet,
which is when a shared download can go wrong. Driving them from the first reference would not test
anything: the single-tool checks have already pulled it down. With a replica ceiling above one the
pair may also be answered by different replicas, which is the case the stateless server is there to
make work.

One check exists for the reference format specifically: after the workflow, the verifier calls
`BinarizeImage` on the reference the run reported. A workflow writes under a scope it releases when it
finishes, so that call only resolves because the reference names the scope it lives in.

Two checks cover what the server tells a client rather than what it computes. Before anything runs, the
verifier reads the `instructions` from the `initialize` response and fails if the behavioural rules are
missing or if the tool catalogue has crept back into them, and it lists the prompts. During the workflow
it asks for progress and counts the notifications that arrive against the number of steps the summary
says ran, so a pipeline that reports nothing while it works is a failure rather than a silence.

A completed workflow is not evidence on its own. An image that fails to decode produces a blank
matrix rather than an error, and the run finishes with an empty OCR result, so the verifier reads
the recognised text back out of the workflow summary and fails when a run that invoked `RunOcr`
returns nothing.

`--image` on `verify-blob-store.py` is unchanged and still goes through `RegisterImage` for local
servers.

Connecting an MCP client by hand is covered in [MCP_CLIENT_TESTING.md](MCP_CLIENT_TESTING.md).
The Web UI verifier additionally creates an authenticated durable conversation, checks token and
tool events over SSE, verifies stored activity on reload, restores a second turn from SQL, retries an
idempotency key, and races two turns to prove optimistic concurrency leaves one consistent history.
With `--image`, it also verifies attachment persistence, structured workflow output, safe artifact
download, receipt revision auditing, and corrected JSON/CSV exports.

The queue verifier uploads the sample receipt, sends plain-language tool and workflow commands,
waits for their run-ID-keyed result rows, and checks every output URL. Details are in
[QUEUE_WORKER.md](QUEUE_WORKER.md).

## Queue-only bulk deployment

Upload each dataset below the canonical training-container prefix:

```text
training-files/images/<dataset-name>/<image-file>
```

Then enqueue a natural-language parent command such as:

```text
Remove background from all images in cord-smoke.
```

The parent asks AI exactly once to select and validate either an ordered image-transform tool chain
or one workflow. It persists `PlanJson`, derives a stable batch ID, and fans out stable per-image
child IDs. Children reuse the plan without per-image AI, skip a durable item result that already
exists, and publish artifacts under `result-files/<group>/<item-id>/`.

`MyConfig__MaximumBatchImages` defaults to 2,000. A message that repeatedly fails is moved after the
default five dequeue attempts to `<queue>-poison`. Inspect the parent and child Table rows, result
Blob prefixes, source and poison queues, and worker logs when collecting evaluation evidence.

CORD contains 1,000 receipt images under CC BY 4.0. Use a small `cord-smoke` subset for quick
integration checks and the full corpus only as an optional reproducible inference benchmark. CORD
is not training data for this project. A deployed two-image smoke batch completed with two
successful children and background-removal artifacts for both inputs. This verifies deployment
wiring, not the unexecuted full CORD benchmark.

## Local Compose

`docker compose up --build` starts only the serving role at `http://localhost:5290`. It uses the same
root Dockerfile and `ocr-agent:local` image as direct Docker commands. One-off CLI commands can reuse
that image:

```bash
docker compose run --rm serving cli list-tools
docker compose run --rm serving cli workflow receipt_extract_v3 RCPT001
```

The worker is intentionally behind a profile because it consumes the real Azure queue configured in
`.env`:

```bash
docker compose --profile worker up --build worker
```

Do not enable the profile merely to test the serving app. Stop Compose with `docker compose down`;
this removes local containers and networks only and does not delete Azure resources.

## Cost

The deployment intentionally keeps one serving replica and the Basic SQL database live. To stop
serving compute after a demo:

```bash
scripts/azure-stop.sh
```

The worker has its own start/stop controls because stopping the serving app should not silently stop
course queue processing.

Start it again with `scripts/azure-start.sh` shortly before you need it. Do not delete the
resource group or the storage account to save money; those hold the graded artifacts and the run
history.
