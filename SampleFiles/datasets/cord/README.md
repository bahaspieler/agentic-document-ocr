# CORD dataset package

This directory contains packaging metadata for **CORD: A Consolidated Receipt
Dataset for Post-OCR Parsing**. CORD v2 contains 1,000 Indonesian receipt
images: 800 train, 100 validation, and 100 test images.

## Attribution and license

CORD was created by NAVER CLOVA AI and the authors cited in `CITATION.bib`.
The dataset is licensed under
[Creative Commons Attribution 4.0 International](https://creativecommons.org/licenses/by/4.0/).
See `LICENSE-CC-BY-4.0.txt` and retain this attribution when redistributing
generated packages or derived samples.

Official references:

- Dataset project: <https://github.com/clovaai/cord>
- Versioned source: <https://huggingface.co/datasets/naver-clova-ix/cord-v2>
- Paper: <https://openreview.net/pdf?id=SJl3z659UH>

## Reproducible packaging

The downloader uses the public Hugging Face release, not Kaggle authentication,
and pins an immutable source revision.

```sh
python3 -m pip install "datasets>=2.14"
python3 scripts/prepare-cord-dataset.py --mode smoke
python3 scripts/prepare-cord-dataset.py --mode full
```

Output is deterministic:

```text
SampleFiles/datasets/cord/
├── smoke/
│   ├── images/cord-v2_<split>_<source-index>.<extension>
│   ├── manifest.json
│   └── SHA256SUMS
└── full/
    ├── images/...
    ├── manifest.json
    └── SHA256SUMS
```

Each manifest records the source dataset and revision, package mode, per-split
counts, total image count, package digest, and one entry per image:

```json
{
  "bytes": 123456,
  "filename": "cord-v2_train_0000.png",
  "package_path": "images/cord-v2_train_0000.png",
  "sha256": "<64 lowercase hexadecimal characters>",
  "source_index": 0,
  "split": "train"
}
```

The package digest is SHA-256 over sorted `"<file-sha256>  <package-path>\n"`
records, the same content written to `SHA256SUMS`. Original image bytes are
copied without re-encoding.

## Queue smoke run

```sh
scripts/azure-queue-produce-bulk.sh \
  --upload-dir SampleFiles/datasets/cord/smoke/images \
  --dataset cord-smoke \
  --command "Remove background from all images in cord-smoke"

python3 scripts/verify-bulk-queue.py
```

The repository does not generate and commit third-party binaries automatically.
The official v2 Parquet release is large, and even a balanced smoke sample must
read upstream shards that lack efficient random-row access. Run the pinned
packaging command locally, manually review/trim the resulting smoke images, and
commit only the small approved sample with its generated manifest and
`SHA256SUMS`. No ignore rule excludes that package.
