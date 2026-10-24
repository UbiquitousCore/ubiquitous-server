FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
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
RUN dotnet restore src/server/server.csproj
COPY src/ src/
# Patrol routes are linked into the publish output by server.csproj.
COPY content/ content/
RUN dotnet publish src/server/server.csproj -c Release --no-restore -o /app/publish

# Data import tooling, for `docker compose run ingest` (see run.sh): just, python
# and the asset extractor, so importing needs nothing installed on the host.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS tools
ENV NUGET_PACKAGES=/nuget DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
RUN apt-get update \
    && apt-get install -y --no-install-recommends python3 curl ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && curl -fsSL https://just.systems/install.sh | bash -s -- --to /usr/local/bin
WORKDIR /work
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig justfile ./
COPY src/ src/
COPY tools/ tools/
RUN dotnet build tools/asset-extractor -v quiet \
    && chmod -R a+rwX /nuget /work
CMD ["just", "ingest"]

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
