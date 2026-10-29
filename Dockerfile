# The SDK stages run on the builder's own architecture and cross-compile, so
# an arm64 image does not compile .NET under emulation.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/common/common.csproj src/common/
COPY src/data/data.csproj src/data/
COPY src/game/game.csproj src/game/
COPY src/persistence/persistence.csproj src/persistence/
COPY src/protocol/web/web.csproj src/protocol/web/
COPY src/scripts/scripts.csproj src/scripts/
COPY src/server/server.csproj src/server/
COPY src/world/world.csproj src/world/
COPY src/module-api/module-api.csproj src/module-api/
# Modules are built into the server, so their projects are needed to restore it.
COPY modules/ modules/
RUN dotnet restore src/server/server.csproj -a $TARGETARCH
COPY src/ src/
# Patrol routes are linked into the publish output by server.csproj.
COPY content/ content/
RUN dotnet publish src/server/server.csproj -c Release --no-restore -a $TARGETARCH -o /app/publish

# `docker compose run --rm ingest`: turns the client build in incoming/ into
# data/generated/, so importing needs nothing installed on the host.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS ingest-build
ARG TARGETARCH
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ src/
COPY tools/ tools/
COPY env.example ./
RUN dotnet publish tools/asset-extractor -c Release -a $TARGETARCH -o /app/extractor

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS ingest
ENV DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 HOME=/tmp
WORKDIR /work
COPY --from=ingest-build /app/extractor /opt/extractor
ENTRYPOINT ["dotnet", "/opt/extractor/asset-extractor.dll"]
CMD ["--ingest", "incoming"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends wget libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
RUN mkdir -p /data/state && chown app:app /data/state
COPY --from=build /app/publish .
ENV UBIQUITOUS_BIND_ADDRESS=0.0.0.0
ENV UBIQUITOUS_WEBSOCKET_PORT=8781
EXPOSE 8781
USER app
ENTRYPOINT ["dotnet", "server.dll"]
