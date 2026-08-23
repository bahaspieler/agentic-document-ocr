# Course Queue Worker

Phase 9 keeps the course template's processing loop and supplies the missing storage and experiment
implementations. `MyCloudProject` and `MyCloudProject.Common` remain unchanged.

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

1. The unchanged template receives the queue message.
2. `AzureStorageProvider` exposes the message as the template's downloaded input file.
3. `Experiment.RunAsync` reads the command and resolves its image from `training-files`.
4. The existing one-shot `AgentService` chooses the OCR tool or workflow.
5. Produced files are uploaded to `result-files`.
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

```bash
docker run --rm --env-file .env ocr-agent worker
scripts/azure-worker-deploy.sh
scripts/verify-queue-worker.py
```

The deployed `ca-ocr-worker` Container App has no ingress and one replica. The verifier uploads
`SampleFiles/images/RCPT001.png`, posts the two example commands, and checks their Table rows and
every referenced result blob.
