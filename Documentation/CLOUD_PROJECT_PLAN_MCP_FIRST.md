# Cloud Project Plan — MCP-First (v0.2)

> **Superseded by `CLOUD_PROJECT_PLAN_V1.md`.** Its MCP-first idea was adopted and its
> first sprint is now delivered, so the remaining work has been re-planned by phase.
> This file is kept for history only. Do not execute it.

> This is the incremental, MCP-first variant of the cloud plan. It shares the same
> target system as `CLOUD_PROJECT_PLAN.md` but changes the order of delivery: we
> start with an MCP server that calls `OcrAgent` in-process and grow the queue,
> storage, and Web UI around it. Keep both files so we can choose which plan to
> execute.

## Project Title

Cloud-Native AI Preprocessing Agent for OCR — Cloud Computing Phase

## Context

In the Software Engineering course we built `OcrAgent`, a .NET image preprocessing and receipt-extraction system. It runs on a single machine and exposes its capability through two front-ends. `AgentRunner` is an interactive chat agent that connects GPT-4o-mini through the Microsoft.Agents.AI SDK, so a user types a natural-language command and the model selects and chains the right tools. `CliRunner` is a non-interactive front-end for scripted use. Behind both, `PluginAggregator` registers 28 callable tools across six categories, a `ToolBindingRegistry` invokes any tool by name, and a workflow engine chains tools into deterministic JSON pipelines such as `receipt_extract_v3`. Images live on the local filesystem or SFTP, OCR runs through a native Tesseract build, and document detection uses a U2-Net ONNX model.

The Cloud Computing phase carries this **whole system** forward, not just the workflow path. The goal is to remove the single-machine assumptions and turn the full capability — natural-language agent chat, single-tool invocation, and deterministic workflows — into a deployable, multi-entry cloud service. The existing `OcrAgent` engine stays the processing core; the cloud adds the storage backbone, hosting, and remote access around it.

We reuse the course template (`MyCloudProjectSample`) and its contracts, but we extend it with the components this project actually needs: an Azure-backed storage layer, structured result storage in Azure SQL, a cloud-hosted agent, an MCP server, and a web frontend.

## Objective

Transform `OcrAgent` into a cloud system that exposes its full capability over the network. A user can issue a natural-language command to the cloud-hosted agent, invoke a single preprocessing tool, or run a deterministic workflow. Over time these can be reached from the MCP server, a REST call, a web upload, or a queue message. The work runs in a container on Azure, images and artifacts live in Blob Storage, run metadata is recorded in Table Storage, and structured receipt data is persisted in Azure SQL with vector search.

## Delivery Approach: MCP-First and Incremental

We build this system in thin vertical slices instead of standing up the whole architecture at once. The sections below describe the target shape, but we do not expect to reach it in a single pass. Each sprint ships something that runs, and we let the design change as we learn what the project actually needs. We cannot picture the finished system in one shot, so the plan is written to absorb that.

The first slice is the shortest path to a cloud-callable system that reuses the entire `OcrAgent` engine: an MCP server that invokes `OcrAgent` in-process. There is no queue in the middle at this stage. The MCP server holds a reference to the engine and calls agent, single-tool, and workflow modes directly, so any MCP client can drive the full capability remotely with the fewest moving parts.

We defer the queue on purpose. The course template's queue-triggered path (Queue → worker → Blob → Table) stays a required deliverable, and we keep `IExperiment` and `IStorageProvider` in place so it can be switched on cleanly. But we add it later, once the engine runs in the cloud and there is a concrete need for asynchronous, decoupled execution. The Web UI follows the same rule: it comes after the MCP path is solid. This keeps the early sprints focused and avoids committing to infrastructure before we need it.

The rest of this document describes the target components. Their order of arrival is set by the Sprint Plan, not by the order they happen to be listed here.

## Component Transformation

This maps each existing component to its cloud form.

### Storage layer → Azure Blob

`OcrAgent` already defines `IImageStore` with `LocalFileImageStore` and `SftpImageStore`. We add a third implementation, `AzureBlobImageStore`, behind the same interface. Plugins keep reading and writing images by reference. They do not learn that the backing store is now Blob Storage. This is the cleanest part of the migration because the abstraction was designed for it.

### Run metadata → Azure Table Storage

Each experiment run writes one row implementing `ITableEntity`: run ID, workflow ID, input blob name, output prefix, success flag, duration, step count, and the final output reference. This is the record the template expects from `UploadResultAsync`.

### Structured output → Azure SQL

The rule-based extractor already produces a `ReceiptData` object (merchant, date, line items, totals, validation). We persist that into Azure SQL so it becomes queryable across runs: totals by merchant, receipts in a date range, line-item search. We also store an embedding vector per receipt so the database can support similarity search and duplicate detection using Azure SQL vector columns.

### Execution core → three modes, one engine

The cloud experiment core wraps the full `OcrAgent` capability, not a single pipeline. A job declares which mode it wants:

- **Agent mode.** A natural-language command goes to `AgentRunner`, which lets GPT-4o-mini pick and chain tools through the Microsoft.Agents.AI SDK. This is the same conversational behavior from the SE project, now driven by a request field instead of a console prompt.
- **Tool mode.** A single tool is invoked by name through `ToolBindingRegistry`, the same path the CLI `tool` command uses. This exposes all 28 tools individually over the cloud.
- **Workflow mode.** `WorkflowRunner` runs a deterministic JSON pipeline such as `receipt_extract_v3`.

In every mode the input image is registered into the active image store, the work runs, and the outputs are returned to the caller. In the first phase the MCP server calls this core in-process, so the same engine serves agent, tool, and workflow requests with no queue in between. When we later add the queue-triggered template path, it reuses this same core unchanged: the entry point changes, the execution core does not.

### AI agent → cloud-hosted agent

The chat agent is a first-class cloud capability, not just a local convenience. `AgentRunner` runs inside the container and reaches GPT-4o-mini through OpenAI or Azure OpenAI. Conversation requests arrive over the MCP server first, and over the REST API and Web UI as they are added. The agent selects and chains tools, and intermediate and final image references are stored just like any other run. The model key is supplied as a secret at deployment time. Multi-turn context is kept per session so the agent can refer back to references it created earlier, the same way it does in the console.

### Console worker → containerized engine

The engine runs inside a Linux container that already contains Tesseract, the OpenCV native runtime, the tessdata files, and the U2-Net model. In the first phase the MCP server hosts the engine in-process inside this container. When the queue-triggered template path is added later, the same container image also runs the worker loop; the sample worker structure stays intact.

### Tools → MCP server

The plugins are already annotated as callable tools. We expose them through an MCP server so any MCP-capable client can drive the cloud system remotely: send a natural-language command to the agent, invoke a single tool, run a workflow, check job status, and fetch results. This is a direct continuation of the SE project's tool-calling design and the headline cloud feature.

The MCP server is also our first cloud entry point. In the initial phase it holds the `OcrAgent` engine in-process and calls it directly, with no queue in between. This gives us a working, remotely callable cloud system early, and everything else — Blob storage, the queue path, the REST API, the Web UI — is added around it as the project grows.

### New: REST API (later phase)

Once the MCP path is solid, a thin ASP.NET API gives a second, HTTP-native entry into the core. It accepts all three modes — an agent command, a single-tool call, or a workflow run — dispatches the job, and reads results back. The Web UI calls this API, and it is also where the queue-triggered template path is wired in when we add asynchronous execution.

### New: Web UI (later phase)

A frontend that offers both interaction styles from the SE project. A chat panel sends natural-language commands to the cloud agent and shows the streamed response with intermediate image references. An upload-and-run panel registers a receipt, triggers a workflow, polls status, and shows the processed image with the extracted fields. Hosted on Azure. This closes the "no GUI" gap we listed as future work in the SE paper. It is built after the MCP path, so it consumes an interface that already works.

## Azure Services and Their Justification

| Service | Role | Replaces / Adds | Phase |
|---------|------|-----------------|-------|
| MCP server | Remote access to agent, tools, and workflows | New headline feature | First |
| OpenAI / Azure OpenAI | GPT-4o-mini for the agent's tool selection | Carried from SE project, now cloud-hosted | First |
| Docker | Package native OCR dependencies reproducibly | Enables cloud hosting | First |
| Azure App Service / Container Apps | Host the MCP server, and later the worker, API, and UI | New | First |
| Azure Key Vault (or App settings secrets) | Holds the model API key and connection strings | New, keeps secrets out of the image | First |
| Azure Blob Storage | Input images and output artifacts | Replaces local/SFTP storage | Later |
| Azure SQL | Queryable structured receipt data + vector search | New value-add | Later |
| Azure Queue Storage | Job triggers, template execution path | New, required by template | Later |
| Azure Table Storage | Per-run experiment log | New, required by template | Later |

## Primary Risk: Native Dependencies

The largest risk is that `OcrAgent` depends on native components: Tesseract, the OpenCvSharp runtime, the tessdata files, and a U2-Net ONNX model. These are heavy and platform-sensitive.

Two consequences drive the schedule:

1. Docker is the enabler, not a nice-to-have. A Linux container with the native libraries preinstalled is the only reliable way to run this on Azure. Because the MCP server hosts the engine, we prove the container builds and runs OCR early — right after the MCP server works locally — before adding storage, the queue, or the UI on top of it.
2. We standardize the cloud solution on .NET 8. The existing `OcrAgent` targets .NET 9, so we either multi-target it or port the parts the experiment needs to .NET 8. This is resolved in Sprint 1 so later sprints build on a stable base.

## Goals

### Primary (must ship)

- MCP server exposes the full engine remotely: agent command, single-tool invocation, and workflow run.
- The MCP server calls `OcrAgent` in-process, and the whole thing runs in a container.
- Implements `IExperiment` and `IStorageProvider`; template structure intact, so the queue-triggered path can be switched on later.
- The natural-language agent runs in the cloud and reaches GPT-4o-mini securely.

### Secondary (the transformation story)

- Container deployed to Azure and reachable remotely.
- `AzureBlobImageStore` replaces local storage through the existing interface.
- Queue-triggered experiment (Queue → Blob → Table) wired in through the template contracts.
- Azure SQL stores queryable structured receipt data.
- REST API to submit jobs (any mode) and fetch results.

### Stretch (standout features)

- Web UI on Azure with a chat panel and an upload-and-run panel.
- Vector similarity search over receipts in Azure SQL.
- Basic monitoring and structured logging.

The tiers are ordered by delivery as much as by importance: we ship the MCP-first core first, then grow the storage backbone, the queue path, and the Web UI around it. The primary tier stays shippable even if later tiers slip.

## Sprint Plan

The sprint contents reflect the MCP-first order. Dates follow the course schedule; the work inside each sprint is what we adjust as the design evolves.

### Sprint 1 (01-06 – 14-06): Plan and Foundation

- Write this cloud project plan and the experiment specification.
- Adopt the MCP-first, incremental delivery approach.
- Decide the .NET 8 strategy for reusing `OcrAgent` (multi-target or port).
- Copy the template, add `OcrAgent` to the solution, and get everything building.

**Acceptance:** plan and specification committed; the cloud solution builds with `OcrAgent` referenced.

### Sprint 2 (15-06 – 28-06): MCP Server over the Engine (in-process)

- Stand up an MCP server that references `OcrAgent` and calls it in-process, with no queue.
- Expose the capability as MCP tools: the individual plugins, `run_workflow`, `agent_chat`, and `get_run`.
- Use the existing local image store for now; storage migration comes later.
- Prove an MCP client can run a single tool, a workflow, and an agent command end to end locally.

**Acceptance:** an MCP client drives all three modes against the local engine through the MCP server.

### Sprint 3 (29-06 – 12-07): Containerize and Deploy the MCP Server

- Build the Docker image with Tesseract, the OpenCV runtime, tessdata, and U2-Net.
- Run the MCP server inside the container; prove OCR and an agent command work there.
- Supply the model key as a secret/environment variable for agent mode.
- Deploy the container to Azure and reach the MCP server remotely.

**Acceptance:** a remote MCP client drives the cloud-hosted engine across all three modes.

### Sprint 4 (13-07 – 26-07): Blob Storage and Structured Data

- Implement `AzureBlobImageStore` behind `IImageStore` and switch the cloud engine to Blob.
- Add Azure SQL and persist structured receipt data; add the vector column.

**Acceptance:** cloud runs read and write images from Blob, and structured receipt results are queryable in SQL.

### Sprint 5 (27-07 – 09-08): Queue Path, Template Contract, and REST API

- Wire the queue-triggered template path: implement `AzureStorageProvider` (Queue → Blob → Table) behind `IExperiment` and `IStorageProvider`.
- Add the REST API accepting all three modes and exposing result queries.

**Acceptance:** a queue message drives a complete run (outputs in Blob, row in Table), and jobs can also be submitted through the REST API.

### Sprint 6 (10-08 – 23-08): Web UI, Hardening, Documentation, Demo

- Build the Web UI with a chat panel (agent) and an upload-and-run panel (workflow), plus status and result viewing.
- Add monitoring, structured logging, and error handling.
- Complete the README, the cloud documentation, and the exercise write-ups.
- Capture screenshots of the queue, blob, table, and SQL.
- Record the demo and submit a review request roughly two weeks before the deadline.

**Acceptance:** a receipt can be processed end to end through the MCP server and the Web UI, documentation is complete, and the submission is ready ahead of 23-08-2026.

## Deliverables

- `Documentation/CLOUD_PROJECT_PLAN_MCP_FIRST.md` (this file).
- `Documentation/EXPERIMENT_SPECIFICATION.md`.
- Cloud solution adapted from the template, referencing `OcrAgent`.
- MCP server hosting `OcrAgent` in-process and covering agent, tool, and workflow modes.
- `AzureBlobImageStore`, `AzureStorageProvider`, and the experiment implementation for the later queue-triggered path.
- Dockerfile and Azure deployment configuration.
- Azure SQL schema for structured receipt data.
- Cloud-hosted agent, MCP server, and Web UI.
- `SampleFiles/` with test inputs and example request messages for each mode.
- Exercise documentation and the final cloud README.
