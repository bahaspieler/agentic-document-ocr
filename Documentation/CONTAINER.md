# Container

One image carries the React Web UI, REST/MCP host, engine, native OCR stack, assets, and all three
entry points. The role is chosen when the container starts.

## Build

```bash
docker build --platform linux/amd64 -t ocr-agent .
```

The platform flag is not optional. Azure runs amd64 hosts, and an image built on an ARM workstation
without it would be an ARM image that the host refuses. On ARM the flag means QEMU emulation, which
costs build time; `receipt_extract_v3` still finishes in about 12 seconds under it. The deploy script
avoids that by building in Azure Container Registry, which is already amd64.

The build context is the project root. A Node 20 stage runs `npm ci` and builds `web/`; its static
output is copied into `/app/mcp/wwwroot` after the .NET publish.

## Roles

```bash
docker run -p 5290:8080 --env-file .env ocr-agent               # Web UI + REST + MCP, the default
docker run --rm --env-file .env ocr-agent cli runs list         # any CLI command
docker run --rm --env-file .env ocr-agent cli workflow receipt_extract_v3 CTR001
docker run --rm --env-file .env ocr-agent worker                # course queue loop
```

`entrypoint.sh` reads the first argument and passes the rest through, with `CMD ["mcp"]` making the
server the default. `worker` starts the unchanged `MyCloudProject` polling host.

The roles publish to `/app/mcp`, `/app/cli`, and `/app/worker`; each carries the native aliases it
needs. The worker reuses the same OCR engine assemblies and model assets through its project
dependency.

The MCP half of the server keeps nothing per client between requests. REST conversation payloads,
idempotency rows, users, and structured receipts live in SQL; run artifacts and records live in Blob
and Table. Every replica therefore reads the same durable state.
So several containers of this image can serve the same set of clients with no routing rules, and one
container answering a request whose predecessor went elsewhere is not a special case. An image reference
produced inside a workflow names the run directory it was written to, which is what lets a second
container resolve it without having taken part in the run.

All .NET roles are published for `linux-x64` rather than portable. Several dependencies ship native binaries
for Android, iOS, macOS, and Windows that a Linux container can never load, and naming the runtime
identifier leaves them out. That alone is the difference between a 1.65 GB image and a 315 MB one.

The worker is a separate no-ingress Container App. It uses local job-scoped engine storage under
`/var/ocr`, uploads graded artifacts through the template provider, and does not connect to the Web
UI's SQL session store. See [QUEUE_WORKER.md](QUEUE_WORKER.md).

## Configuration

Secrets arrive through `--env-file .env` at run time and are never baked into a layer. The keys are
the ones in [CONFIGURATION.md](CONFIGURATION.md); the image only presets what is specific to running
in a container:

| Variable | Value | Why |
|----------|-------|-----|
| `OCR_Storage__AzureBlob__CacheRootPath` | `/var/ocr/cache` | The blob store stages files on local disk, and the default resolves under `/app`, which the app user cannot write |
| `OCR_Storage__Local__RootPath` | `/var/ocr/local-storage` | Same reason, for the local provider |
| `ASPNETCORE_HTTP_PORTS` | `8080` | The server listens here; map it to whatever the host uses |

The container runs as the non-root `app` user that the .NET base image provides. Only `/var/ocr` is
writable, which is why both stores are pointed there.

## Native dependencies

Three native stacks have to resolve inside the image, and each fails quietly rather than loudly. The
build ends with `cli list-tools | grep -q RunOcr`, which is the cheapest check that covers all of
them: that tool is registered only when Tesseract, its trained data, and every library underneath
have loaded, so a broken image never gets built.

**OpenCV.** `OcrAgent.csproj` picks its runtime package by host platform, and the Linux branch takes
`OpenCvSharp4.official.runtime.linux-x64` at `4.13.0.20260627`, deliberately ahead of the managed
`4.11`. Builds up to 4.11 link against Ubuntu 20.04 libraries that Debian does not have at all
(`libjpeg.so.8`, `libtiff.so.5`, OpenEXR 2.5) plus `libtesseract.so.4`, which would collide with the
Tesseract 5 the OCR engine needs. The 4.13 build dropped all of those and needs only the GTK3 family
Debian ships. Native exports are additive, so the older managed package binds against the newer
native one; macOS has always run the opposite gap, a 4.8 native under the same managed 4.11.

The GTK3 packages are not there for a user interface. OpenCV's Linux build links its display module
whether or not anything draws a window, and the loader has to resolve that at load time.

**Tesseract.** The wrapper ships Windows binaries only, and it loads Tesseract and Leptonica by the
fixed names `tesseract50` and `leptonica-1.82.0` from an `x64` directory beside the assembly. Debian
installs the same libraries as `libtesseract.so.5` and `liblept.so.5`, so the image symlinks one
naming scheme onto the other there. Putting the same aliases in `/usr/lib` instead does not work:
the wrapper does not fall back to the system loader for these, so the `x64` directory is the only
location that counts.

One more symlink is needed and is far less obvious: the wrapper opens those libraries through
`libdl.so` by that exact name. That is the development symlink, absent from every runtime image, so
without it the load fails before Tesseract is ever reached. `libdl.so.2` is present, and pointing one
at the other is the whole fix.

Both mismatches fail silently. The engine starts normally and simply has no OCR tools.

The runtime uses `mcr.microsoft.com/dotnet/aspnet:10.0`, currently based on Ubuntu 24.04. Its
Tesseract 5.3 and Leptonica 1.82 libraries match the compatibility aliases created by the
Dockerfile. The bundled `eng.traineddata` is used instead of relying on a distribution-specific
language-data package.

**ONNX Runtime.** `Microsoft.ML.OnnxRuntime` already carries its Linux native library and needs
nothing beyond `libgomp1`. Debian installs that anyway as a dependency of Tesseract, so it is named
explicitly rather than relied on by accident.

## Checking an image

The engine drops a tool when its native library is missing instead of refusing to start, so a broken
image looks perfectly healthy from the outside. The build's own smoke test catches that before an
image exists. To check an image you were handed:

```bash
docker run --rm ocr-agent cli list-tools | grep -E "RunOcr|ScoreOcrResult"
docker run --rm --env-file .env ocr-agent cli workflow receipt_extract_v3 CTR001
```

The first proves Tesseract loaded, since those two tools are registered only when it does. The second
proves it works, as long as the OCR text in the result is real text rather than empty.

Registering a new image needs the file visible inside the container, so mount the directory:

```bash
docker run --rm --env-file .env -v "$PWD/samples:/samples:ro" \
    ocr-agent cli register /samples/receipt-1.jpg CTR001
```

For the server role, `scripts/verify-blob-store.py` runs against the container unchanged, given an
image path that resolves inside it.
