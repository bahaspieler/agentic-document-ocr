# System Architecture

## Scope

The cloud project keeps the image-processing engine built during the Software Engineering course
and adds remote access, durable state, cloud storage, and asynchronous execution. The same
`OcrAgent` assembly serves the console, the ASP.NET host, and the course queue worker. Entry points
adapt their transport contracts to that engine instead of implementing separate processing
pipelines.

The deployed system has two runtime boundaries:

- `ca-ocr-agent` serves the React application, REST API, SSE streams, and stateless MCP endpoint.
- `ca-ocr-worker` has no ingress. It polls Azure Queue through the unchanged course host and handles
  single-image agent jobs plus deterministic bulk parent/child orchestration.

Both Container Apps use one image from Azure Container Registry. They have different entry roles
and persistence settings.

## Runtime and deployment

The solution targets .NET 10. The Docker image is built for `linux/amd64` on
`mcr.microsoft.com/dotnet/aspnet:10.0`. The image includes:

- the published `OcrAgent.Mcp`, `OcrAgent`, and `MyCloudProject` applications;
- the compiled React, Vite, and TypeScript application;
- workflow definitions, Tesseract language data, and the U2NETP ONNX model;
- OpenCV, ONNX Runtime, Tesseract 5, and Leptonica native dependencies.

`entrypoint.sh` selects one of three roles:

- `mcp` starts `OcrAgent.Mcp.dll` and is the image default;
- `cli` starts `OcrAgent.dll` with the remaining command-line arguments;
- `worker` starts `MyCloudProject.dll`.

The serving app exposes port 8080 through HTTPS ingress. It keeps at least one replica active for
the submitted Web UI and permits up to three by default. MCP request state is not stored in a
replica, and REST conversation state lives in SQL, so no session affinity is needed.

The worker runs separately with no ingress and one replica. Its engine writes temporary files under
`/var/ocr`; `MyExperiment.AzureStorageProvider` publishes final results to the course storage
account.

Azure Container Apps, the registry, and Azure SQL are placed in Germany West Central beside the
storage account. The existing `RG_Hidden_Layer` resource group remains an administrative boundary,
not a location choice. Deployment scripts never create or delete that group.

## Entry paths and responsibility

### Web UI and REST

The browser loads the SPA and calls `/api/*` on the same origin. It does not implement MCP. The REST
surface provides image upload, conversations, direct jobs, workflow discovery, run history,
artifacts, structured receipts, correction revisions, and corrected exports.

Agent turns use `text/event-stream`. The server emits typed `token`, `tool`, `step`, `run`,
`artifact`, `done`, and `error` events. Completed activity is stored with the turn so reopening a
conversation does not depend on replaying a live stream.

The Process Image flow supports two paths. Workflow mode calls a named deterministic pipeline.
Agent-assisted mode sends a one-shot instruction to `AgentService`, which selects a tool or
workflow. Conversation chat also uses `AgentService`, but restores and saves a durable model session
around each turn.

### MCP

`OcrAgent.Mcp` exposes the engine through Streamable HTTP in Azure and stdio for local clients. The
HTTP transport uses stateless session mode. A request may reach any serving replica.

The MCP server publishes the typed `AIFunction` tool surface, workflow prompts, run lookup tools,
and server instructions. `RunWorkflow` reports step progress as MCP progress notifications. The
calling MCP client owns the conversation and supplies its model. The server therefore does not
offer an `agent_chat` tool and does not call the server-side model for MCP requests.

MCP tools execute in-process against `PluginAggregator`; they do not enqueue jobs. The Web UI and
queue worker do not call the MCP endpoint.

### CLI

`CliRunner` invokes tools and workflows directly through the engine registry. It also supports
image registration, workflow discovery, run queries, and help. Console chat is a thin caller of
`AgentService` and needs a model key. Direct tool and workflow commands do not need a language
model.

### Queue worker

The queue path follows the contracts in `MyCloudProject.Common`. `MyCloudProject` owns the polling
loop and calls the implementations supplied by `MyExperiment`.

A queue message is plain text, for example:

```text
Extract receipt data using workflow receipt_extract_v3 from RCPT001.
```

Bulk parents are also natural language:

```text
Remove background from all images in cord-smoke.
```

`AzureStorageProvider` exposes the message through the template's input-file contract.
For a single command, `Experiment.RunAsync` locates the named image in `training-files`, imports it
into a job-scoped local engine store, and invokes a one-shot `AgentService` call.

For a bulk parent, the storage adapter lists the canonical
`training-files/images/<dataset-name>/` prefix. The planner calls AI once to choose and validate
either one ordered chain of image-transform tools or one workflow. It stores that choice as
`PlanJson` in a parent batch row before publishing one internal child message per sorted image.
Batch IDs and item IDs are stable functions of the parent request, command, and source Blob. A
redelivered parent reuses persisted `PlanJson`; it does not replan.

Each child carries the exact source Blob and persisted plan. It validates and executes the plan
directly through `ExecutionCore`, without an agent session or per-image AI decision. If its stable
item result already exists, the worker returns that durable result instead of processing the image
again. JSON is used only for internal child messages; operator-facing parent messages remain natural
language, and there is no queue conversation history.

The worker uploads artifacts to `result-files` and writes parent/item `ExperimentResult` rows to the
`results` Table. Messages are deleted only after durable writes complete. Unexpected failures retry;
the default fifth failed delivery moves the message to `<queue>-poison`. The worker runs one replica.

## Shared engine

### EngineHost

`OcrAgent.Hosting.EngineHost.Create()` is the common composition root. It loads configuration,
creates the selected image and run stores, creates SQL-backed or null stores, and builds one
`PluginAggregator`. Console, server, and worker entry points therefore use the same plugin and
workflow configuration.

Optional services have null implementations. A checkout can run with local image storage, no run
store, no SQL, and no authentication. Features that depend on those services remain unavailable
without preventing the processing engine from starting.

### PluginAggregator and ToolBindingRegistry

`PluginAggregator` constructs the processing plugins and converts their described methods into
AI-callable functions. `ToolBindingRegistry` provides case-insensitive lookup and direct invocation
for workflows, CLI commands, and REST jobs.

The processing functions cover six areas:

- image analysis and document contour extraction;
- geometric correction and resizing;
- contrast, brightness, denoising, sharpening, color, and illumination adjustment;
- binarization, morphology, and document background removal;
- Tesseract OCR and OCR-result scoring;
- rule-based receipt parsing and JSON/CSV export.

Each image-producing tool reads an image reference, writes a new image and JSON sidecar, and returns
the output reference. The sidecar records the parent and operation parameters. This forms a lineage
chain without placing intermediate image metadata in SQL.

OCR tools register only when Tesseract and its language data load successfully. The container build
checks for `RunOcr` so a native dependency failure cannot produce an apparently valid image with no
OCR support.

### Inference, OCR, and extraction

`U2NetpOnnxPredictor` loads `u2netp.onnx` once and produces a foreground probability mask at
320 by 320 pixels. Contour extraction, perspective correction, and background removal can use that
mask. Low-quality predictions fall back to classical OpenCV strategies.

`TesseractOcrEngine` returns text, confidence, lines, words, and bounding boxes. `RunOcr` can test
natural grayscale, Otsu, adaptive, and Sauvola variants under several page segmentation modes.
`OcrScoringPlugin` selects a candidate from confidence, structure, receipt keywords, and text
density.

Receipt extraction is rule-based. It normalizes OCR tokens, separates receipt zones, extracts
merchant and transaction fields, parses line items, and checks totals. OCR confidence and receipt
consistency are separate values. The application does not treat either value as measured
field-level accuracy.

### Workflow engine

`WorkflowCatalog` loads JSON definitions from `OcrAgent/Workflows/Definitions`.
`WorkflowCompiler` resolves registered tools, disabled steps, arguments, and branches.
`WorkflowRunner` executes the graph with a fresh context and budget. Steps exchange non-image data
through `$context.*` values, while the output image reference becomes the next step's input.

Each workflow receives a UUID version 7 run ID and a scoped output directory such as:

```text
wf_receipt_extract_v3/<runId>/
```

The scope appears in output references. A later request or another replica can therefore resolve an
artifact without relying on the process that created it.

`receipt_extract_v3` is the main receipt pipeline. It assesses quality, detects the document,
corrects geometry, prepares the image, runs multi-variant OCR, parses the receipt, and exports JSON
and CSV.

### ExecutionCore and AgentService

`OcrAgent.Execution.ExecutionCore` gives REST jobs and the worker one transport-neutral contract
for agent, tool, and workflow modes. It returns execution output, recorded runs, and discovered
artifacts. REST direct jobs use tool and workflow modes. The queue adapter uses agent mode.

`AgentService` wraps the Microsoft Agents AI tool-calling loop. It accepts natural language and
calls the same functions registered for workflows and MCP. REST conversation handling supplies a
serialized `AgentSession`; queue execution creates one session for the single command and discards
it afterward. Bulk execution is different: only the parent planner invokes AI, and every child
executes the persisted deterministic plan.

MCP stays on its native typed-tool path because the MCP client already provides this reasoning
layer.

## Persistence

### Image and artifact stores

All plugins depend on `IImageStore`. Three implementations exist:

- `AzureBlobImageStore` is used by the deployed serving app.
- `LocalFileImageStore` supports development and the worker's temporary processing.
- `SftpImageStore` remains an optional remote provider inherited from the console project.

Plugins require filesystem paths for OpenCV. The Blob and SFTP providers therefore cache remote
files locally for reads and write results back through their provider. Local cache files are
transient; Blob references and sidecars are the durable source for the serving app.

`IArtifactStore` adds safe run-directory listing and stream access. REST endpoints validate run
scope and filenames before previewing or downloading images, JSON, CSV, OCR output, or traces.

### Serving run records

The deployed serving app uses `AzureTableRunStore` and the `OcrRuns` Table. One logical job writes
one `RunRecord`, including mode, name, references, success, duration, trace, output directory, and
conversation ID when present.

Workflow step tools do not create duplicate top-level rows. `RunScope` associates their activity
with the workflow record. `GetRun`, `ListRuns`, REST history, and artifact discovery read this same
record.

### Azure SQL

SQL is enabled for the deployed Web UI and created idempotently at host startup. The schema contains:

- `Users` for Auth0 identities and named API-key principals;
- `Conversations` for ownership, titles, serialized model sessions, and `rowversion`;
- `ConversationTurns` for idempotency keys, messages, activity, run IDs, and attachments;
- `Receipts` for original and current structured data, review state, and concurrency metadata;
- `ReceiptItems` for the current normalized line-item projection;
- `ReceiptRevisions` for immutable correction history.

`SqlSessionStore`, `SqlUserStore`, and `SqlReceiptStore` implement these operations. Their null
counterparts keep SQL optional for local engine use.

The original extraction remains in `RawJson`. A correction writes `CurrentJson`, updates line
items, increments the current revision, and inserts an audit row in one transaction.

### Course storage

The worker's result path is separate from the serving run store:

- the trigger arrives through the configured Azure Queue;
- single-image sources come from `training-files`, while bulk datasets use
  `training-files/images/<dataset-name>/`;
- published child artifacts go to `result-files/<groupId>/<itemId>/`;
- parent batch and child item `ExperimentResult` rows go to the `results` Table.

The worker disables `AzureTableRunStore`. Its course result row is not an `OcrRuns` row, and it does
not persist Web UI conversations or receipts to SQL.

Bulk dispatch rejects an empty dataset or one above the configured cap, which defaults to 2,000
images. Stable IDs, persisted plans, existing-item result checks, isolated queue retries, poison
handling after five failed deliveries by default, and a single worker replica make redelivery
bounded and observable.

## State, concurrency, and recovery

The HTTP MCP server holds no protocol session between requests. Server instructions, prompts, and
tools are reconstructed from application configuration and are identical on every replica.

REST conversations are durable. Before a turn, the API loads the serialized model session from SQL.
After completion, it stores the session and turn activity using SQL `rowversion`. Two simultaneous
updates cannot silently overwrite one another; one receives a conversation conflict.

Every turn carries an idempotency key. Repeating a completed request returns the stored answer
instead of appending the same user message twice.

Storage scope, run attribution, agent progress, and conversation attribution use async-local
contexts. Concurrent requests do not share one mutable workflow directory or assign runs to the
wrong conversation.

Blob and SQL state survive replica replacement and deployment. A local cache miss causes the Blob
store to download the referenced image again.

## Authentication and access

Authentication remains optional only for offline development. When enabled, ASP.NET's generic JWT
handler validates Auth0 issuer, audience, signature, and lifetime, and the shared authorization
policy requires `access:ocr`. A parallel scheme validates named `X-API-Key` credentials by comparing
their SHA-256 hashes with configured `OCR_Auth__ApiKeys__Name` entries. Production `/api` and
Streamable HTTP MCP requests accept either scheme. Static SPA files, `/api/config`, and OAuth
Protected Resource Metadata remain public so clients can begin authentication.

The Auth0 API audience must equal the canonical `ResourceUri`. Protected-resource metadata
advertises that URI, Auth0's authorization server, and `access:ocr`; the Auth0 tenant uses the
Resource Parameter Compatibility Profile for MCP RFC 8707 requests. Strict open DCR plus a default
third-party user grant lets provider-neutral clients such as Cursor, Claude, and Copilot-compatible
clients register automatically.

The React application gates the workspace on Auth0 Universal Login, with database, Google, and
GitHub connections. API calls, SSE requests, uploads, artifact previews, downloads, and receipt
exports attach the bearer token. Conversation and resource access is checked against the
authenticated Auth0 subject; API-key principals receive stable identities derived from their
configured names. The Web UI does not automatically assign earlier anonymous browser history to a
signed-in account; the claim endpoint remains available only for an explicit migration client.

A first-party native client supports device authorization through `scripts/auth0-token.py`; the
Auth0 API policy issues these user access tokens for 24 hours. `scripts/api-key-create.py` reveals a
raw static key once and persists only its SHA-256 hash. Raw API keys and short-lived
`AUTH0_MANAGEMENT_API_TOKEN` values must never be committed.

The deployed Auth0 implementation has been verified through interactive Web login, anonymous
rejection, OAuth discovery, and authenticated API-key REST/MCP workflows.
Microsoft Entra was abandoned after the DAENET tenant denied the project identity app-registration
permissions; it is retained only as factual project history, not as current architecture.

Secrets are injected through Container Apps secret references. The current deployment does not use
Azure Key Vault.

## End-to-end flows

### REST conversation turn

1. The browser uploads an image and receives a durable image reference.
2. The API validates the scoped Auth0 token or named API key, loads or creates the SQL conversation,
   and checks ownership.
3. `AgentService` restores the model session and streams text and activity to the browser.
4. Tool and workflow calls process Blob-backed references through the shared engine.
5. Artifacts go to Blob, the logical job goes to `OcrRuns`, and exported receipt data goes to SQL.
6. The API saves the turn, activity, attachments, and serialized session using its idempotency key
   and row version.

### MCP workflow

1. The MCP client authenticates through automatic Auth0 OAuth DCR or `X-API-Key`, then initializes
   and receives server instructions, prompts, and typed tools.
2. The client's model calls `RunWorkflow` with an existing Blob reference.
3. The server executes the workflow in-process and reports step progress.
4. The workflow writes scoped Blob artifacts and one Azure Table run record.
5. The client receives the final reference and may pass it to a later standalone tool call.

### Queue command

1. `MyCloudProject` receives one plain-language queue message.
2. `AzureStorageProvider` retains its message ID and pop receipt.
3. A single-image command resolves one source and runs a one-shot agent.
4. A bulk parent resolves one dataset, persists one validated AI-selected `PlanJson`, and fans out
   stable child messages.
5. Each child deterministically applies the persisted plan to its exact source Blob, skipping an
   already persisted item result.
6. The provider uploads outputs under `result-files/<group>/<item-id>/`, writes the result row, and
   deletes the message after durable writes succeed.

## Learning and evaluation boundary

The architecture orchestrates pretrained U2NETP, Tesseract, language-model, and classical
image-processing components. It is not a model-training or fine-tuning pipeline. CORD supplies
1,000 CC BY 4.0 receipt images as an evaluation and robustness corpus, not learning artifacts.
`cord-smoke` is a small integration subset; the full 1,000-image corpus is an optional benchmark.
The project therefore does not claim PR20 training compliance. Reproducible bulk queue, Blob,
Table, retry, throughput, OCR, and extraction evidence is the relevant evaluation. A deployed
two-image smoke batch verified planning, fan-out, execution, and result persistence; the full CORD
benchmark and dataset-level metrics remain pending.

The Web UI remains scoped to single-image processing and conversations. Bulk orchestration is
queue-only because the professor specifically requested queue-worker bulk processing.

## Project boundaries

```text
OcrAgent/
  Agent/             AgentService, instructions, console runner
  Execution/         Shared execution and artifact discovery
  Hosting/           EngineHost composition
  Inference/         U2NETP prediction and contour extraction
  Ocr/               Tesseract engine and result models
  Plugins/           Analysis, geometric, enhancement, segmentation, OCR, extraction
  Runs/              Run records, scopes, Table and null stores
  Sql/               SQL schema and session/user/receipt stores
  Storage/           Local, Blob, SFTP, and artifact-store abstractions
  Workflows/         Definitions, compiler, runner, executors, and tools

OcrAgent.Mcp/
  Api/               REST models and endpoints
  Program.cs         ASP.NET, MCP, REST, SSE, and SPA host
  OcrAgentToolHost.cs

web/                 React, Vite, and TypeScript SPA
MyCloudProject/      Unchanged course polling host
MyCloudProject.Common/
MyExperiment/        Course storage provider and experiment adapter
UnitTests/           Backend unit and integration tests
scripts/             Deployment, lifecycle, and verification
```

`MyCloudProject` and `MyCloudProject.Common` remain unchanged to preserve the course template.
Cloud-specific queue behavior is implemented in `MyExperiment`.

## Deliberate exclusions

The current system does not implement receipt embeddings, vector similarity, duplicate detection,
or an MCP receipt-query tool. It also does not use Key Vault, GPU inference, or a learned receipt
extraction model. These are possible follow-up tasks, not current architecture.

The previous `SE_Course_SYSTEM_ARCHITECTURE.md` describes the console-era engine. This document is
the canonical architecture for the cloud project.
