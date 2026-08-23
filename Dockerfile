# OCR preprocessing engine. One image, the role chosen at start-up.
#
# Build for linux/amd64 even on an ARM workstation, because that is what the Azure hosts run:
#   docker build --platform linux/amd64 -t ocr-agent .
#
# See Documentation/CONTAINER.md for the roles and the native dependency reasoning.

ARG DOTNET_VERSION=10.0
ARG OCRAGENT_TARGET_FRAMEWORK=net10.0

FROM node:20-bookworm-slim AS web-build
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build


FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG OCRAGENT_TARGET_FRAMEWORK
WORKDIR /src

# Project files first, so a source-only change does not invalidate the restore layer.
COPY Directory.Build.props ./
COPY OcrAgent/OcrAgent.csproj OcrAgent/
COPY OcrAgent.Mcp/OcrAgent.Mcp.csproj OcrAgent.Mcp/
COPY MyCloudProject.Common/MyCloudProject.Common.csproj MyCloudProject.Common/
COPY MyExperiment/MyExperiment.csproj MyExperiment/
COPY MyCloudProject/MyCloudProject.csproj MyCloudProject/
RUN dotnet restore OcrAgent.Mcp/OcrAgent.Mcp.csproj -r linux-x64 -p:OcrAgentTargetFramework=${OCRAGENT_TARGET_FRAMEWORK}
RUN dotnet restore MyCloudProject/MyCloudProject.csproj -r linux-x64 -p:OcrAgentTargetFramework=${OCRAGENT_TARGET_FRAMEWORK}

COPY OcrAgent/ OcrAgent/
COPY OcrAgent.Mcp/ OcrAgent.Mcp/
COPY MyCloudProject.Common/ MyCloudProject.Common/
COPY MyExperiment/ MyExperiment/
COPY MyCloudProject/ MyCloudProject/

# Naming the runtime identifier leaves out the Android, iOS, macOS, and Windows native binaries that
# several dependencies carry. That is most of the image.
RUN dotnet publish OcrAgent.Mcp/OcrAgent.Mcp.csproj -c Release -r linux-x64 --self-contained false --no-restore -p:OcrAgentTargetFramework=${OCRAGENT_TARGET_FRAMEWORK} -o /out/mcp \
 && dotnet publish OcrAgent/OcrAgent.csproj -c Release -r linux-x64 --self-contained false --no-restore -p:OcrAgentTargetFramework=${OCRAGENT_TARGET_FRAMEWORK} -o /out/cli \
 && dotnet publish MyCloudProject/MyCloudProject.csproj -c Release -r linux-x64 --self-contained false --no-restore \
      -p:OcrAgentTargetFramework=${OCRAGENT_TARGET_FRAMEWORK} -p:UseAppHost=false -p:ErrorOnDuplicatePublishOutputFiles=false -o /out/worker


FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app/mcp

# The .NET 10 runtime image uses Ubuntu 24.04, where Tesseract 5.3 and Leptonica 1.82 match the
# fixed names expected by the managed wrapper. GTK3 is not for a user interface: OpenCV links its
# display module whether or not anything ever draws a window, so the loader has to satisfy it.
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
COPY --from=build /out/worker /app/worker
COPY --from=web-build /web/dist /app/mcp/wwwroot
COPY entrypoint.sh /app/entrypoint.sh

# Two naming mismatches sit between Debian and the OCR wrapper. It loads Tesseract and Leptonica by
# fixed names from an x64 folder beside the assembly, and it opens them through libdl.so, which is
# the development symlink that no runtime image ships. Both fail silently: the engine starts
# perfectly well and simply has no OCR tools.
# chmod is a separate step because Azure Container Registry builds without BuildKit, which is
# what COPY --chmod needs.
RUN chmod +x /app/entrypoint.sh \
 && ln -s /usr/lib/x86_64-linux-gnu/libdl.so.2 /usr/lib/x86_64-linux-gnu/libdl.so \
 && for app in /app/mcp /app/cli /app/worker; do \
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
