# Reproducible Sample Files

`images/RCPT001.png` is the receipt used by the queue-worker verifier. The image is copied from the
team's earlier Software Engineering project so the cloud experiment no longer depends on a sibling
folder.

`queue/remove-background.txt` and `queue/extract-receipt.txt` contain the exact plain-text message
bodies accepted by the queue worker. The deployed worker does not consume a JSON job descriptor.

`workflows/receipt_extract_v3.json` is a submitted copy of the workflow exercised by the receipt
command. The executable definition remains under `OcrAgent/Workflows/Definitions`.

Run the deployed queue experiment from the project root:

```bash
scripts/azure-worker-start.sh
scripts/azure-queue-produce.sh \
  --image SampleFiles/images/RCPT001.png \
  --message-file SampleFiles/queue/remove-background.txt \
  --message-file SampleFiles/queue/extract-receipt.txt
```

The producer verifies the source upload and enqueues both commands, but deliberately does not poll
or assert results. Use the end-to-end verifier when those checks are required:

```bash
python3 scripts/verify-queue-worker.py --image SampleFiles/images/RCPT001.png
```

The verifier uploads the image to `training-files`, sends both command bodies to `trigger-queue`,
waits for matching rows in the `results` Table, and checks each URL written under `result-files`.
