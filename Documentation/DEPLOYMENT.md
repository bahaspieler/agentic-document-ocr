# Deployment

The serving instance of the OCR engine runs as an Azure Container App, pulled from a container
registry in the same resource group as the storage account. There is no worker instance yet; that
role is a placeholder until the queue path.

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
| Container app | `ca-ocr-agent` | MCP server, HTTPS, scales 0 to `AZURE_MAX_REPLICAS` |

Names are overridable in `.env` (`AZURE_RESOURCE_GROUP`, `AZURE_ACR_NAME`,
`AZURE_CONTAINER_APP_NAME`, `AZURE_CONTAINER_APP_ENV`), as is the replica ceiling
(`AZURE_MAX_REPLICAS`).

New resources are created in the storage account's region, not the resource group's. Every workflow
step reads an image and writes another, so a container in a second region pays a round trip on each
one. `RG_Hidden_Layer` is registered in East US, `sthiddenlayerocr` is in Germany West Central, and
everything above sits in Germany West Central with it. A group's location is only the default for
new resources and is not where the data is. `azure-deploy.sh` warns if it ever finds an existing
environment somewhere other than the storage account's region; a Container Apps environment cannot
be moved, so the fix is a teardown and a fresh deploy, and the app gets a new hostname.

## Scripts

```bash
scripts/azure-deploy.sh            # create or update; builds linux/amd64 in ACR
scripts/azure-deploy.sh --skip-build
scripts/azure-start.sh             # activate, min 0
scripts/azure-start.sh --warm      # activate, min 1
scripts/azure-stop.sh              # deactivate the revision; URL stays
scripts/azure-teardown.sh          # delete app, environment, registry only
scripts/verify-deployment.sh       # upload a receipt, run MCP checks, assert Blob and Table
```

`azure-deploy.sh` is idempotent. It registers the `Microsoft.App` and `Microsoft.OperationalInsights`
providers if needed, creates the registry and environment when they are missing, builds a uniquely
tagged image with `az acr build --platform linux/amd64`, and creates or updates the app. The storage
connection string is a Container Apps secret, not an image layer. `OPENAI_API_KEY` is stored the same
way when it is present in `.env`.

The replica range is min 0 / max `AZURE_MAX_REPLICAS`, which defaults to 3. The MCP server runs in
stateless mode, so nothing a client sent earlier has to still be held by the replica that answers the
next request, and Container Apps is free to spread load without session affinity. Anything a run needs
to outlive the request that started it is in Blob Storage or the run table, both shared by every
replica. Scale to zero is the idle default, so a ceiling above one costs nothing while the app is
unused; `--warm` keeps one replica running for a demo.

Container Apps will not accept `max-replicas 0`, so `azure-stop.sh` deactivates the active revision
instead. That stops compute without deleting the app or its hostname.

`azure-teardown.sh` prints the named resources, requires `yes`, and refuses anything else. It never
calls `az group delete` and never touches the storage account. The Log Analytics workspace the
environment created is reported and left alone unless `--include-logs` is passed.

## Verification

```bash
scripts/verify-deployment.sh
scripts/verify-deployment.sh --image /path/to/receipt.jpg
```

The script uploads the file to Blob twice, as `DEPLOY01.jpg` and `DEPLOY02.jpg`, drives
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

## Cost

Idle cost is the registry and the Container Apps environment, not a running replica. After a demo:

```bash
scripts/azure-stop.sh
```

Start it again with `scripts/azure-start.sh --warm` shortly before you need it. Do not delete the
resource group or the storage account to save money; those hold the graded artifacts and the run
history.
