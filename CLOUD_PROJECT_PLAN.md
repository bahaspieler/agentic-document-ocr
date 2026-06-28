# Cloud Project Plan (v0.1)

## Project Title

Cloud-Native AI Preprocessing Agent for OCR — Cloud Computing Phase

## Context

In the Software Engineering course we built `OcrAgent`, a .NET image preprocessing and receipt-extraction system. It runs on a single machine and exposes its capability through two front-ends. `AgentRunner` is an interactive chat agent that connects GPT-4o-mini through the Microsoft.Agents.AI SDK, so a user types a natural-language command and the model selects and chains the right tools. `CliRunner` is a non-interactive front-end for scripted use. Behind both, `PluginAggregator` registers 28 callable tools across six categories, a `ToolBindingRegistry` invokes any tool by name, and a workflow engine chains tools into deterministic JSON pipelines such as `receipt_extract_v3`. Images live on the local filesystem or SFTP, OCR runs through a native Tesseract build, and document detection uses a U2-Net ONNX model.

The Cloud Computing phase carries this **whole system** forward, not just the workflow path. The goal is to remove the single-machine assumptions and turn the full capability — natural-language agent chat, single-tool invocation, and deterministic workflows — into a deployable, multi-entry cloud service. The existing `OcrAgent` engine stays the processing core; the cloud adds the storage backbone, hosting, and remote access around it.

We reuse the course template (`MyCloudProjectSample`) and its contracts, but we extend it with the components this project actually needs: an Azure-backed storage layer, structured result storage in Azure SQL, a cloud-hosted agent, an MCP server, and a web frontend.

## Objective

Transform `OcrAgent` into a cloud system that exposes its full capability over the network. A user can issue a natural-language command to the cloud-hosted agent, invoke a single preprocessing tool, or run a deterministic workflow. Each of these can be triggered from a queue message, a REST call, the MCP server, or a web upload. The work runs in a container on Azure, images and artifacts live in Blob Storage, run metadata is recorded in Table Storage, and structured receipt data is persisted in Azure SQL with vector search.

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

In every mode the downloaded blob is registered into the Blob-backed store, the work runs, and outputs plus metadata are uploaded. The queue-triggered template experiment uses workflow mode by default because it is deterministic and needs no model call, but the same core serves agent and tool requests from the API, MCP server, and Web UI.

### AI agent → cloud-hosted agent

The chat agent is a first-class cloud capability, not just a local convenience. `AgentRunner` runs inside the container and reaches GPT-4o-mini through OpenAI or Azure OpenAI. Conversation requests arrive over the REST API and the Web UI, the agent selects and chains tools, and intermediate and final image references are stored in Blob just like any other run. The model key is supplied as a secret at deployment time. Multi-turn context is kept per session so the agent can refer back to references it created earlier, the same way it does in the console.

### Console worker → containerized queue processor

The sample worker loop stays structurally the same. It now calls the real experiment and runs inside a Linux container that already contains Tesseract, the OpenCV native runtime, the tessdata files, and the U2-Net model.

### Tools → MCP server

The plugins are already annotated as callable tools. We expose them through an MCP server so any MCP-capable client can drive the cloud system remotely: send a natural-language command to the agent, invoke a single tool, run a workflow, check job status, and fetch results. This is a direct continuation of the SE project's tool-calling design and the headline cloud feature.

### New: REST API

A thin ASP.NET API is the single entry into the core. It accepts all three modes — an agent command, a single-tool call, or a workflow run — enqueues or dispatches the job, and reads results from Table Storage and SQL. The MCP server and the Web UI both call this API so there is one consistent path into the system.

### New: Web UI

A frontend that offers both interaction styles from the SE project. A chat panel sends natural-language commands to the cloud agent and shows the streamed response with intermediate image references. An upload-and-run panel registers a receipt, triggers a workflow, polls status, and shows the processed image with the extracted fields. Hosted on Azure. This closes the "no GUI" gap we listed as future work in the SE paper.

## Azure Services and Their Justification

| Service | Role | Replaces / Adds |
|---------|------|-----------------|
| Azure Blob Storage | Input images and output artifacts | Replaces local/SFTP storage |
| Azure Queue Storage | Job triggers, template execution path | New, required by template |
| Azure Table Storage | Per-run experiment log | New, required by template |
| Azure SQL | Queryable structured receipt data + vector search | New value-add |
| OpenAI / Azure OpenAI | GPT-4o-mini for the agent's tool selection | Carried from SE project, now cloud-hosted |
| Azure Key Vault (or App settings secrets) | Holds the model API key and connection strings | New, keeps secrets out of the image |
| Docker | Package native OCR dependencies reproducibly | Enables cloud hosting |
| Azure App Service / Container Apps | Host worker, API, agent, MCP server, UI | New |
| MCP server | Remote access to agent, tools, and workflows | New headline feature |

## Primary Risk: Native Dependencies

The largest risk is that `OcrAgent` depends on native components: Tesseract, the OpenCvSharp runtime, the tessdata files, and a U2-Net ONNX model. These are heavy and platform-sensitive.

Two consequences drive the schedule:

1. Docker is the enabler, not a nice-to-have. A Linux container with the native libraries preinstalled is the only reliable way to run this on Azure. We prove the container builds and runs OCR early, before building the API, MCP server, or UI on top of it.
2. We standardize the cloud solution on .NET 8. The existing `OcrAgent` targets .NET 9, so we either multi-target it or port the parts the experiment needs to .NET 8. This is resolved in Sprint 1 so later sprints build on a stable base.

## Goals

### Primary (grade floor — must ship)

- Queue-triggered experiment runs the receipt OCR workflow end to end.
- Input read from Blob, outputs written to Blob, run record written to Table.
- Implements `IExperiment` and `IStorageProvider`; template structure intact.
- Runs in a container deployed to Azure.

### Secondary (the transformation story)

- `AzureBlobImageStore` replaces local storage through the existing interface.
- The core supports all three modes in the cloud: agent command, single-tool invocation, and workflow run.
- The natural-language agent runs in the cloud and reaches GPT-4o-mini securely.
- Azure SQL stores queryable structured receipt data.
- REST API to submit jobs (any mode) and fetch results.

### Stretch (standout features)

- MCP server exposing the agent, individual tools, and workflows remotely.
- Web UI on Azure with a chat panel and an upload-and-run panel.
- Vector similarity search over receipts in Azure SQL.
- Basic monitoring and structured logging.

The solution is structured so the primary tier is always shippable even if the stretch tier slips.


## System Architecture Flow

The cloud version follows a clear request-processing flow:

1. A user submits a request through the Web UI, REST API, MCP server, or Azure Queue.
2. The request is normalized into a job message containing the execution mode, input reference, workflow or tool name, and optional natural-language command.
3. Input images are read from Azure Blob Storage through `AzureBlobImageStore`.
4. The experiment core selects one of three execution paths:
   - Agent mode for natural-language tool selection and chaining.
   - Tool mode for direct invocation of one preprocessing or extraction tool.
   - Workflow mode for deterministic JSON-based processing such as `receipt_extract_v3`.
5. Generated images, OCR results, and intermediate artifacts are written back to Azure Blob Storage.
6. Run metadata is written to Azure Table Storage.
7. Structured receipt fields are stored in Azure SQL for later querying, reporting, and duplicate detection.
8. The user receives the final status, output references, and extracted data through the original entry point.
