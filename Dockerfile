# OCR preprocessing engine. One image, the role chosen at start-up.
#
# Build for linux/amd64 even on an ARM workstation, because that is what the Azure hosts run:
#   docker build --platform linux/amd64 -t ocr-agent .
#
# See Documentation/CONTAINER.md for the roles and the native dependency reasoning.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Project files first, so a source-only change does not invalidate the restore layer.
COPY OcrAgent/OcrAgent.csproj OcrAgent/
COPY OcrAgent.Mcp/OcrAgent.Mcp.csproj OcrAgent.Mcp/
RUN dotnet restore OcrAgent.Mcp/OcrAgent.Mcp.csproj -r linux-x64

COPY OcrAgent/ OcrAgent/
COPY OcrAgent.Mcp/ OcrAgent.Mcp/

# Naming the runtime identifier leaves out the Android, iOS, macOS, and Windows native binaries that
# several dependencies carry. That is most of the image.
RUN dotnet publish OcrAgent.Mcp/OcrAgent.Mcp.csproj -c Release -r linux-x64 --self-contained false --no-restore -o /out/mcp \
 && dotnet publish OcrAgent/OcrAgent.csproj -c Release -r linux-x64 --self-contained false --no-restore -o /out/cli


FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim AS runtime

# Bookworm is deliberate. The Tesseract wrapper asks for library names that hard-code Leptonica
# 1.82.0, and bookworm is where Tesseract 5.3 and Leptonica 1.82.0 meet; Ubuntu 22.04 is still on
# Tesseract 4.1. GTK3 is not for a user interface: OpenCV links its display module whether or not
# anything ever draws a window, so the loader has to satisfy it.
RUN apt-get update \
 && apt-get install -y --no-install-recommends \
        libtesseract5 \
        liblept5 \
        libgomp1 \
        libgtk-3-0 \
        libdrm2 \
        libatomic1 \
 && rm -rf /var/lib/apt/lists/*

COPY --from=build /out/mcp /app/mcp
COPY --from=build /out/cli /app/cli
COPY entrypoint.sh /app/entrypoint.sh

# Two naming mismatches sit between Debian and the OCR wrapper. It loads Tesseract and Leptonica by
# fixed names from an x64 folder beside the assembly, and it opens them through libdl.so, which is
# the development symlink that no runtime image ships. Both fail silently: the engine starts
# perfectly well and simply has no OCR tools.
# chmod is a separate step because Azure Container Registry builds without BuildKit, which is
# what COPY --chmod needs.
RUN chmod +x /app/entrypoint.sh \
 && ln -s /usr/lib/x86_64-linux-gnu/libdl.so.2 /usr/lib/x86_64-linux-gnu/libdl.so \
 && for app in /app/mcp /app/cli; do \
        mkdir -p "$app/x64"; \
        ln -s /usr/lib/x86_64-linux-gnu/libtesseract.so.5 "$app/x64/libtesseract50.so"; \
        ln -s /usr/lib/x86_64-linux-gnu/liblept.so.5 "$app/x64/libleptonica-1.82.0.so"; \
    done

# The OCR tools are registered only once Tesseract, its trained data, and every native library below
# them have loaded, so asking for one by name covers all of it in a single check.
RUN /app/entrypoint.sh cli list-tools | grep -q RunOcr

# Written state lives outside /app, so the application directory stays read-only to the app user.
ENV OCR_Storage__AzureBlob__CacheRootPath=/var/ocr/cache \
    OCR_Storage__Local__RootPath=/var/ocr/local-storage \
    ASPNETCORE_HTTP_PORTS=8080

RUN mkdir -p /var/ocr/cache /var/ocr/local-storage && chown -R app:app /var/ocr

USER app
EXPOSE 8080

ENTRYPOINT ["/app/entrypoint.sh"]
CMD ["mcp"]
