set shell := ["bash", "-uc"]
set dotenv-load
# Plain `bash` on Windows usually resolves to WSL, which can't see the Windows
# dotnet install; use Git Bash instead.
set windows-shell := ["C:/Program Files/Git/bin/bash.exe", "-uc"]

export PATH := if os_family() == "windows" { env_var("PATH") } else { env_var("HOME") + "/.dotnet:" + env_var("PATH") }

raw_root := "data/raw"
raw := "data/raw/tables"
raw_world := "data/raw/world"
generated := "data/generated"
staging := "data/cache/import"

# The drop folder `just ingest` reads: the client .ipa goes here by default,
# or point it elsewhere (a shell export, .env, or `just ingest /path`).
incoming_dir := env_var_or_default("UBIQUITOUS_INCOMING_DIR", "incoming")

port := env_var_or_default("UBIQUITOUS_WEBSOCKET_PORT", "8781")
tables := env_var_or_default("UBIQUITOUS_TABLES", "")
world_dir := env_var_or_default("UBIQUITOUS_WORLD_DIR", "")
world_or_tables := if world_dir != "" { world_dir } else { tables }

_default:
    @just --list --unsorted

# Build every project.
build:
    dotnet build

# Run every test project.
test:
    dotnet test

# Build and test.
check: build test

# Remove build output.
clean:
    dotnet clean

# First run: installs missing tools, writes .env and server.json (see setup.sh).
setup *args:
    ./setup.sh {{args}}

# Run the server for development: saves stay as editable JSON files, no
# Docker, no Postgres, and it rebuilds and restarts when source files change.
# Reads server.json when it exists (`just setup`).
# Another port: `just dev --port 9000`, or `just port=9000 up` for any recipe.
dev *args:
    #!/usr/bin/env bash
    set -euo pipefail
    config=()
    if [ -f server.json ]; then config=(--config server.json); fi
    UBIQUITOUS_POSTGRES_CONNECTION_STRING= dotnet watch --project src/server --non-interactive run -- --port {{port}} ${config[@]+"${config[@]}"} {{args}}

# Run the full stack in Docker: the server plus Postgres, on the same port
# as `just dev`. Reads server.json too when it exists; `just up
# .env.production` takes secrets from that file instead of .env.
up envfile='' *args:
    #!/usr/bin/env bash
    set -euo pipefail
    compose=(docker compose)
    if [ -n "{{envfile}}" ]; then compose+=(--env-file "{{envfile}}"); fi
    if [ -f server.json ]; then
      compose+=(-f docker-compose.yml -f docker-compose.server-json.yml)
      echo "using server.json for game settings"
      if command -v python3 >/dev/null; then
        file_port=$(python3 -c "import json;print(json.load(open('server.json')).get('webSocketPort',''))" 2>/dev/null || true)
        if [ -n "${file_port}" ] && [ "${file_port}" != "{{port}}" ]; then
          echo "warning: server.json says webSocketPort ${file_port} but Docker maps {{port}};"
          echo "keep them the same (or set UBIQUITOUS_WEBSOCKET_PORT=${file_port}) or the client will dial the wrong port"
        fi
      fi
    fi
    if [ -n "{{envfile}}" ]; then
      if [ ! -f "{{envfile}}" ]; then echo "no {{envfile}}"; exit 2; fi
    elif [ ! -f .env ]; then
      echo "no .env — run: ./setup.sh"; exit 2
    fi
    UBIQUITOUS_WEBSOCKET_PORT={{port}} "${compose[@]}" up --build -d {{args}}
    for _ in $(seq 1 30); do
      if curl -fsS -m 3 "http://127.0.0.1:{{port}}/health" >/dev/null 2>&1; then
        echo "server healthy on port {{port}}"; exit 0
      fi
      sleep 2
    done
    echo "the stack is up but /health did not answer on port {{port}} — see: just docker-logs"
    exit 1

# Stop the Docker stack.
down envfile='' *args:
    docker compose {{ if envfile != "" { "--env-file " + envfile } else { "" } }} down {{args}}

# Follow the Docker server's log.
docker-logs:
    docker compose logs -f server

# Manage real accounts for `just dev` (file saves). Safe while the server runs.
#   just account create alice | just account password alice | just account list
account *args:
    #!/usr/bin/env bash
    set -euo pipefail
    config=()
    if [ -f server.json ]; then config=(--config server.json); fi
    UBIQUITOUS_POSTGRES_CONNECTION_STRING= dotnet run --project src/server -- account {{args}} ${config[@]+"${config[@]}"}

# Run the server in the foreground, exactly as configured (Postgres when
# UBIQUITOUS_POSTGRES_CONNECTION_STRING is set, files when it is not).
serve *args:
    #!/usr/bin/env bash
    set -euo pipefail
    config=()
    if [ -f server.json ]; then config=(--config server.json); fi
    dotnet run --project src/server -- --port {{port}} ${config[@]+"${config[@]}"} {{args}}

# Run the server in the background, logging to local/server.log.
serve-bg:
    #!/usr/bin/env bash
    set -euo pipefail
    mkdir -p local
    dotnet build -v quiet
    config=()
    if [ -f server.json ]; then config=(--config server.json); fi
    nohup src/server/bin/Debug/net10.0/server --port {{port}} ${config[@]+"${config[@]}"} > local/server.log 2>&1 &
    echo "server starting on port {{port}}; log: local/server.log"
    sleep 8
    curl -fsS -m 8 "http://127.0.0.1:{{port}}/health" && echo || echo "health check did not answer yet"

# Stop a backgrounded server.
stop:
    #!/usr/bin/env bash
    pid=$(lsof -nP -iTCP:{{port}} -sTCP:LISTEN -t 2>/dev/null | head -1 || true)
    if [ -z "${pid}" ]; then echo "nothing listening on {{port}}"; exit 0; fi
    kill "${pid}" && echo "stopped ${pid}"

[doc("Set a character's level in the save (server must be stopped)")]
level name new_level:
    #!/usr/bin/env bash
    set -euo pipefail
    if lsof -nP -iTCP:{{port}} -sTCP:LISTEN -t >/dev/null 2>&1; then
        echo "the server is running on {{port}} — stop it first (just stop), or it will write the old level back"
        exit 1
    fi
    python3 - "{{name}}" "{{new_level}}" <<'PY'
    import json, sys, pathlib
    name, level = sys.argv[1], int(sys.argv[2])
    path = pathlib.Path("data/generated/characters.json")
    rows = json.loads(path.read_text())
    hit = [r for r in rows if r["name"] == name]
    if not hit:
        sys.exit(f"no character called {name} in {path}")
    for r in hit:
        was = r["level"]
        r["level"] = level
        r["xp"] = 0
        print(f"{name}: level {was} -> {level}")
    path.write_text(json.dumps(rows, indent=2))
    PY

# Follow the backgrounded server's log.
logs:
    tail -f local/server.log

# Is the server answering?
health:
    curl -fsS -m 8 "http://127.0.0.1:{{port}}/health" && echo

# List the tables a client archive carries, without writing anything.
inspect archive=tables:
    @test -n "{{archive}}" || { echo "set UBIQUITOUS_TABLES, or: just inspect /path/to/tables.bin"; exit 2; }
    dotnet run --project tools/asset-extractor -- --inspect "{{archive}}"

# Unpack a client archive, once, so later imports read plain files.
extract archive=tables:
    @test -n "{{archive}}" || { echo "set UBIQUITOUS_TABLES, or: just extract /path/to/tables.bin"; exit 2; }
    dotnet run --project tools/asset-extractor -- --extract "{{archive}}" --out "{{raw_root}}"
    @echo "unpacked to {{raw_root}} (tables in {{raw}})"

[doc("Add a client build's collision models to the unpacked tables; a later build only fills models the first lacks")]
extract-collision source=world_or_tables:
    @test -n "{{source}}" || { echo "point at a client build (the .ipa, the unpacked app, or a folder of its packs): just extract-collision /path/to/build"; exit 2; }
    dotnet run --project tools/asset-extractor -- --extract-collision "{{source}}" --out "{{raw_root}}"

[doc("Put the per-map world archives beside the tables (needs the client build, not tables.bin)")]
extract-world source=world_or_tables:
    @test -n "{{source}}" || { echo "nothing to read. Point at the client build itself — the .ipa, or the unpacked app directory — not at tables.bin, which carries no maps. Set UBIQUITOUS_WORLD_DIR, or: just extract-world /path/to/build"; exit 2; }
    dotnet run --project tools/asset-extractor -- --extract-world "{{source}}" --out "{{raw_root}}"
    @echo "world archives in {{raw_world}}"

[doc("Copy one pack (usually tables.bin) out of a client build into a folder")]
extract-pack build pack out:
    dotnet run --project tools/asset-extractor -- --extract-pack "{{build}}" --pack "{{pack}}" --out "{{out}}"

[doc("Turn a dropped-in client build into a baked package: drop the .ipa into incoming/ (or $UBIQUITOUS_INCOMING_DIR, or pass its path) and run this")]
ingest source='':
    #!/usr/bin/env bash
    set -euo pipefail
    src="{{source}}"
    if [ -z "${src}" ]; then src="{{incoming_dir}}"; fi
    dropdir="${src}"
    build=""; tables=""; full_build=0
    if [ -f "${src}" ]; then
      dropdir="$(dirname "${src}")"
      if [ "$(basename "${src}" | tr '[:upper:]' '[:lower:]')" = "tables.bin" ]; then
        tables="${src}"; build="${dropdir}"
        ipa="$(find "${dropdir}" -maxdepth 3 -iname '*.ipa' -print -quit)"
        pack="$(find "${dropdir}" -maxdepth 3 \( -iname model_phy.bin -o -iname res.bin \) -print -quit)"
        if [ -n "${ipa}" ]; then build="${ipa}"; full_build=1;
        elif [ -n "${pack}" ]; then build="$(dirname "${pack}")"; full_build=1; fi
      else
        build="${src}"
      fi
    elif [ -d "${src}" ]; then
      tables="$(find "${src}" -maxdepth 3 -iname tables.bin -print -quit)"
      ipa="$(find "${src}" -maxdepth 3 -iname '*.ipa' -print -quit)"
      pack="$(find "${src}" -maxdepth 3 \( -iname model_phy.bin -o -iname res.bin \) -print -quit)"
      build="${src}"
      if [ -n "${ipa}" ]; then build="${ipa}"; full_build=1;
      elif [ -n "${pack}" ]; then build="$(dirname "${pack}")"; full_build=1; fi
    else
      echo "nothing at ${src} — drop the client .ipa into ${src}/ and run again (or: just ingest /path/to/build)"
      exit 2
    fi
    if [ -f "${build}" ]; then
      case "$(basename "${build}" | tr '[:upper:]' '[:lower:]')" in *.ipa) full_build=1;; esac
    fi
    if [ -z "${tables}" ]; then
      if [ "${full_build}" = 0 ]; then echo "no tables.bin under ${src}"; exit 2; fi
      echo "pulling tables.bin out of ${build}"
      dotnet run --project tools/asset-extractor -- --extract-pack "${build}" --pack tables.bin --out "${dropdir}"
      tables="$(find "${dropdir}" -maxdepth 3 -iname tables.bin -print -quit)"
      if [ -z "${tables}" ]; then echo "no tables.bin in ${build}"; exit 1; fi
    fi
    if [ "${full_build}" = 1 ]; then
      dotnet run --project tools/asset-extractor -- --extract-world "${build}" --out "{{raw_root}}"
      echo "world archives in {{raw_world}}"
      dotnet run --project tools/asset-extractor -- --extract-collision "${build}" --out "{{raw_root}}"
    else
      echo "tables only — skipping world archives and collision models"
    fi
    just reimport "${tables}"
    if [ "${full_build}" = 1 ]; then
      dotnet run --project tools/asset-extractor -- \
        --extract-looks "${build}" --tables-dir "{{raw}}" --package "{{generated}}" \
        || echo "warning: no looks in ${build} — bots fall back to plain bodies"
    fi
    just bake
    echo "ingested from ${build}: package in {{generated}}, saves kept"

# Build a data package from the already-unpacked tables, into staging.
import:
    #!/usr/bin/env bash
    set -euo pipefail
    test -d "{{raw}}" || { echo "no {{raw}} — run: just extract /path/to/tables.bin"; exit 2; }
    rm -rf "{{staging}}"
    args=(--import-from-dir "{{raw}}" --out "{{staging}}")
    worlds="{{world_dir}}"
    if [ -d "{{raw_world}}" ]; then worlds="{{raw_world}}"; fi
    if [ -n "${worlds}" ]; then
      while IFS= read -r -d '' archive; do args+=(--world-archive "${archive}"); done \
        < <(find "${worlds}" -maxdepth 1 -name '*.bin' -print0)
    fi
    dotnet run --project tools/asset-extractor -- "${args[@]}"
    echo "staged in {{staging}}"

[doc("Bake walkable ground for every map (or one, with map=14)")]
bake dir=generated map="":
    #!/usr/bin/env bash
    set -euo pipefail
    test -d "{{dir}}/terrain" || { echo "no {{dir}}/terrain — import a package with world archives first"; exit 2; }
    args=(--bake "{{dir}}")
    if [ -d "{{raw}}" ]; then args+=(--tables-dir "{{raw}}"); fi
    if [ -d "{{raw_root}}/model" ]; then args+=(--models "{{raw_root}}/model"); fi
    if [ -d "{{raw_world}}" ]; then args+=(--world-dir "{{raw_world}}"); fi
    if [ -n "{{map}}" ]; then args+=(--map "{{map}}"); fi
    dotnet run --project tools/asset-extractor -- "${args[@]}"

# Explain the bake at one place: `just nav-probe 13 -553.2,5.0,118.3`.
nav-probe map at radius="40" dir=generated:
    #!/usr/bin/env bash
    set -euo pipefail
    args=(--nav-probe "{{dir}}" --map "{{map}}" --at "{{at}}" --radius "{{radius}}")
    if [ -d "{{raw}}" ]; then args+=(--tables-dir "{{raw}}"); fi
    if [ -d "{{raw_root}}/model" ]; then args+=(--models "{{raw_root}}/model"); fi
    if [ -d "{{raw_world}}" ]; then args+=(--world-dir "{{raw_world}}"); fi
    dotnet run --project tools/asset-extractor -- "${args[@]}"

# Validate a package. Defaults to the one the server reads.
validate dir=generated:
    dotnet run --project tools/asset-extractor -- --check "{{dir}}"

# Extract, import, validate and install a package, keeping runtime state.
reimport archive=tables: (extract archive) import (validate staging)
    #!/usr/bin/env bash
    set -euo pipefail
    for keep in accounts.json characters.json guilds inventory mail quests auctions friends playerLooks.json .gitkeep; do
      if [ -e "{{generated}}/${keep}" ]; then cp -R "{{generated}}/${keep}" "{{staging}}/"; fi
    done
    if [ -d "{{generated}}/terrain" ] && [ ! -d "{{staging}}/terrain" ]; then
      cp -R "{{generated}}/terrain" "{{staging}}/terrain"
    fi
    if [ -d "{{generated}}/navigation" ] && [ ! -d "{{staging}}/navigation" ]; then
      cp -R "{{generated}}/navigation" "{{staging}}/navigation"
    fi
    # A dataset kept from the old package keeps its manifest entry too, or the
    # server reads past the file: player looks were lost this way.
    python3 - "{{generated}}/manifest.json" "{{staging}}/manifest.json" <<'PY'
    import json, pathlib, sys
    old, new = (pathlib.Path(p) for p in sys.argv[1:])
    if old.exists():
        kept = json.loads(old.read_text()).get("Datasets", {})
        manifest = json.loads(new.read_text())
        listed = manifest.setdefault("Datasets", {})
        for name, file in kept.items():
            if name not in listed and (new.parent / file).exists():
                listed[name] = file
        new.write_text(json.dumps(manifest, indent=2) + "\n")
    PY
    rm -rf "{{generated}}.previous"
    mv "{{generated}}" "{{generated}}.previous"
    mv "{{staging}}" "{{generated}}"
    echo "installed; the package it replaced is in {{generated}}.previous"

# Write the tiny synthetic package, for running with no client data at all.
synthetic:
    dotnet run --project tools/asset-extractor -- --synthetic --out "{{generated}}"
