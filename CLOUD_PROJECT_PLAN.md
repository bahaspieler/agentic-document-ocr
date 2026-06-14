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

