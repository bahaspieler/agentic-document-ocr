# Course Queue Worker

The implementation keeps the course template's processing loop and supplies its storage and
experiment adapters. `MyCloudProject` and `MyCloudProject.Common` remain unchanged.

## Message format

A queue message is the same natural-language command a user gives the Web UI agent:

```text
Remove the background from RCPT001.
```

```text
Extract receipt data using workflow receipt_extract_v3 from RCPT001.
```

The referenced image must exist in the configured `training-files` container. It may be inside a
folder such as `images/RCPT001.png`; matching uses the filename without its extension.

## Processing

1. `MyCloudProject` polls the queue and gives one message to the experiment as one independent job.
2. `AzureStorageProvider` materializes the message body as the template's local input file.
3. `MyExperiment` reads the command, asks the provider to resolve and download its named image, and
   maps both to an agent-mode execution request.
4. `ExecutionCore` invokes `AgentService`, which creates a one-shot model session and calls the
   shared OCR tools or workflow.
5. `AzureStorageProvider` uploads the collected files to `result-files`.
6. One result row is written to the configured Table, using the primary OCR run ID as both
   `ExperimentId` and `RowKey`.
7. The queue message is deleted only after Blob and Table writes succeed.

MCP and REST are not involved: they are network transports. The worker directly reuses the same
`AgentService`, execution core, tools, workflows, run records, and artifact discovery code.

## Result

The result row contains the command, run ID, success/error state, model answer, source image blob,
timing, and output-file URLs. Result blobs use:

```text
<group-id>/<run-id>/<artifact-name>
```

Storage errors leave the message uncommitted for Azure Queue retry. Invalid commands, unresolved
image references, and missing model configuration produce a failure row and are committed.

## Configuration

| Variable | Purpose |
|---|---|
| `MyConfig__StorageConnectionString` | Course Queue/Blob/Table account |
| `MyConfig__GroupId` | Table partition |
| `MyConfig__TrainingContainer` | Source images |
| `MyConfig__ResultContainer` | Produced files |
| `MyConfig__ResultTable` | Command results |
| `MyConfig__Queue` | Natural-language commands |
| `MyConfig__WorkRoot` | Temporary worker files |
| `OPENAI_API_KEY` | Required by `AgentService` |

## Run and verify

Deploy or start the no-ingress worker, then submit a manual command:

```bash
scripts/azure-worker-deploy.sh
scripts/azure-queue-produce.sh \
  --image SampleFiles/images/RCPT001.png \
  --message "Remove the background from RCPT001."
```

`azure-queue-produce.sh` uses `COURSE_STORAGE_CONNECTION_STRING`, falling back to
`AZURE_STORAGE_CONNECTION_STRING`. It creates the selected container and queue if needed, verifies
the uploaded Blob, and only then publishes one plain-text message per `--message` or non-empty
`--message-file` line. It overwrites an existing Blob with the selected name, but does not delete
existing messages or results, and it does not wait for a result.

Use the separate verifier for a repeatable end-to-end assertion:

```bash
python3 scripts/verify-queue-worker.py --image SampleFiles/images/RCPT001.png
```

The deployed `ca-ocr-worker` Container App has no ingress and one replica. The verifier uploads the
sample, publishes two commands, waits for their Table rows, and checks every referenced result Blob.

For local container execution, build and start the serving role with `docker compose up --build`.
The worker is opt-in because it consumes the Azure queue named in `.env`:

```bash
docker compose --profile worker up --build worker
```

Stop it with `docker compose --profile worker stop worker`. This changes the local worker container
only; it does not stop or remove the deployed Container App.
