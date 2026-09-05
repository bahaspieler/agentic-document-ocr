# Queue Worker

The implementation keeps the queue processing loop and extends its storage and experiment
adapters with deterministic bulk dispatch, idempotency, and poison-message handling.

## Commands and source layout

A single-image queue message is plain natural language:

```text
Remove the background from RCPT001.
```

```text
Extract receipt data using workflow receipt_extract_v3 from RCPT001.
```

The referenced image must exist in the configured `training-files` container. Matching uses its
filename without the extension.

A bulk parent message is also plain natural language and must explicitly request all images in one
named dataset, for example:

```text
Remove background from all images in cord-smoke.
```

Bulk datasets use this canonical Blob layout:

```text
training-files/images/<dataset-name>/<image-file>
```

The dataset name in the command must uniquely match a folder directly below `images/`. Supported
images are sorted by Blob name before dispatch. A request is rejected if the folder is empty or
exceeds `WorkerOptions__MaximumBatchImages`, whose default is 2,000.

## Processing

For a single image, `OcrAgent.Worker.Azure` resolves the named source and runs one one-shot agent command.

For a bulk parent:

1. The worker derives a stable batch ID from the parent queue message ID and command.
2. The planning agent selects and validates either one ordered chain of image-transform tools or one
   workflow. A workflow cannot be mixed with tool steps.
3. The parent result row stores the selected `PlanJson` before fan-out. A redelivered parent reuses
   that persisted plan instead of asking the model again.
4. The worker lists `training-files/images/<dataset-name>/` and publishes one deterministic child
   message per image. Each child has a stable item ID derived from the batch ID and Blob name.
5. A child validates and executes the persisted plan directly through `ExecutionCore`; there is no
   per-image AI planning or conversation.
6. A child whose item result row already exists is skipped and returns that durable result.

The parent planner therefore makes one AI decision for the batch. Fan-out and child execution are
deterministic. JSON is an internal child-message contract, not the operator-facing parent format.

MCP and REST are not involved: they are network transports. The worker directly reuses the same
`AgentService`, execution core, tools, workflows, run records, and artifact discovery code.

## Result

Single-image and child result rows contain the command, success/error state, source Blob, timing,
plan metadata where applicable, and output-file URLs. The parent batch row records its batch ID,
dataset, item count, dispatch status, and `PlanJson`. Result blobs preserve the existing contract:

```text
result-files/<group-id>/<item-id>/<artifact-name>
```

For a legacy single-image result, `<item-id>` is its experiment/run key. Storage or unexpected
processing errors leave a message uncommitted for retry. After
`WorkerOptions__MaximumDequeueAttempts` failed deliveries (default 5), the worker copies the failed
message and error details to `<queue>-poison` and deletes it from the source queue. The deployed
worker uses one replica, preventing competing consumers from processing the fan-out concurrently.

## Configuration

| Variable | Purpose |
|---|---|
| `WorkerOptions__StorageConnectionString` | Worker Queue/Blob/Table account |
| `WorkerOptions__GroupId` | Table partition |
| `WorkerOptions__TrainingContainer` | Source images |
| `WorkerOptions__ResultContainer` | Produced files |
| `WorkerOptions__ResultTable` | Command results |
| `WorkerOptions__Queue` | Natural-language commands |
| `WorkerOptions__WorkRoot` | Temporary worker files |
| `WorkerOptions__MessageVisibilityTimeoutSeconds` | Processing lease for one received message |
| `WorkerOptions__MaximumBatchImages` | Bulk-image cap (default 2,000) |
| `WorkerOptions__MaximumDequeueAttempts` | Failed deliveries before `<queue>-poison` (default 5) |
| `OPENAI_API_KEY` | Required by `AgentService` |

## Run and verify

Deploy or start the no-ingress worker, then submit a manual command:

```bash
scripts/azure-worker-deploy.sh
scripts/azure-queue-produce.sh \
  --image SampleFiles/images/RCPT001.png \
  --message "Remove the background from RCPT001."
```

`azure-queue-produce.sh` uses `WORKER_STORAGE_CONNECTION_STRING`, falling back to
`AZURE_STORAGE_CONNECTION_STRING`. It creates the selected container and queue if needed, verifies
the uploaded Blob, and only then publishes one plain-text message per `--message` or non-empty
`--message-file` line. It overwrites an existing Blob with the selected name, but does not delete
existing messages or results, and it does not wait for a result.

Use the separate verifier for a repeatable end-to-end assertion:

```bash
python3 scripts/verify-queue-worker.py --image SampleFiles/images/RCPT001.png
```

The deployed `ca-ocr-worker` Container App has no ingress and one replica. The verifier uploads the
sample, publishes two single-image commands, waits for their Table rows, and checks every referenced
result Blob. It does not constitute a submitted bulk batch.

Prepare and submit a bulk smoke run with:

```bash
python3 -m pip install "datasets>=2.14"
python3 scripts/prepare-cord-dataset.py --mode smoke
scripts/azure-queue-produce-bulk.sh \
  --upload-dir SampleFiles/datasets/cord/smoke/images \
  --dataset cord-smoke \
  --command "Remove background from all images in cord-smoke"
python3 scripts/verify-bulk-queue.py
```

The preparation script creates the local `images/` directory and defaults to 10 images. Pass
`--smoke-count <N>` to select another smoke size.

The producer uploads every supported image before publishing exactly one parent command. The
verifier reports parent/item counts and artifact URLs. Its `--requeue-failed` option is a dry run
unless `--yes` is also provided; retries are emitted as ordinary single-image commands.

## CORD evaluation corpus

CORD is a CC BY 4.0 corpus of 1,000 receipt images. In this project it is an evaluation and
robustness corpus for pretrained inference components, not training data. A small `cord-smoke`
subset is appropriate for fast queue and artifact checks; running all 1,000 images is an optional
full benchmark for reproducible throughput, retry, artifact-integrity, OCR, and extraction
evidence. A separately trimmed two-image background-removal batch completed successfully as a
deployment smoke check; it was not the default generated smoke package. Do not report full-corpus
results until the 1,000-image batch has actually been submitted and measured.

The Web UI remains intentionally scoped to single-image processing and conversations. Bulk
processing belongs to the queue worker so fan-out, retries, idempotency, and durable item status
remain asynchronous worker concerns.

For local container execution, build and start the serving role with `docker compose up --build`.
The worker is opt-in because it consumes the Azure queue named in `.env`:

```bash
docker compose --profile worker up --build worker
```

Stop it with `docker compose --profile worker stop worker`. This changes the local worker container
only; it does not stop or remove the deployed Container App.
