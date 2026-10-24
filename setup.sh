#!/usr/bin/env bash
# Developer setup for macOS, Linux and WSL / Git Bash: installs what is missing,
# writes .env and server.json, creates incoming/. Safe to run again.
# To only run the server, use ./run.sh (Docker only) instead.
#   ./setup.sh            .NET SDK + just + config (enough for `just dev`)
#   ./setup.sh --docker   also check that Docker and Compose work (for `just up`)
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

want_docker=0
for arg in "$@"; do
  case "$arg" in
    --docker) want_docker=1 ;;
    -h|--help) sed -n 2,6p "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "unknown option: $arg"; exit 2 ;;
  esac
done

say() { printf '\n==> %s\n' "$*"; }
have() { command -v "$1" >/dev/null 2>&1; }
missing=()

export PATH="$HOME/.dotnet:$HOME/.local/bin:$PATH"

say ".NET 10 SDK"
if have dotnet && dotnet --list-sdks | grep -q '^10\.'; then
  echo "found $(dotnet --version)"
elif have curl; then
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "${TMPDIR:-/tmp}/dotnet-install.sh"
  bash "${TMPDIR:-/tmp}/dotnet-install.sh" --channel 10.0 --install-dir "$HOME/.dotnet"
  echo "installed to ~/.dotnet (the justfile finds it there; for your own shell: export PATH=\"\$HOME/.dotnet:\$PATH\")"
else
  missing+=("curl, to download the .NET SDK (https://dotnet.microsoft.com/download/dotnet/10.0)")
fi

say "just (task runner)"
if have just; then
  echo "found just $(just --version | awk '{print $2}')"
elif have brew; then
  brew install just
elif have curl; then
  mkdir -p "$HOME/.local/bin"
  curl -fsSL https://just.systems/install.sh | bash -s -- --to "$HOME/.local/bin"
  echo "installed to ~/.local/bin; add it to PATH: export PATH=\"\$HOME/.local/bin:\$PATH\""
else
  missing+=("just (https://just.systems)")
fi

say "Python 3 (used by just ingest)"
if have python3; then echo "found $(python3 --version)"; else missing+=("python3"); fi

if [ "$want_docker" = 1 ]; then
  say "Docker"
  if ! have docker; then
    missing+=("Docker (https://docs.docker.com/get-docker/ — Docker Desktop, OrbStack or Colima on macOS)")
  elif ! docker info >/dev/null 2>&1; then
    missing+=("a running Docker daemon: start Docker Desktop / OrbStack, then run ./setup.sh --docker again")
  elif ! docker compose version >/dev/null 2>&1; then
    missing+=("the Docker Compose v2 plugin (docker compose)")
  else
    echo "found $(docker --version), $(docker compose version | head -1)"
  fi
fi

if [ "${#missing[@]}" -gt 0 ]; then
  printf '\nStill needed:\n'; printf '  - %s\n' "${missing[@]}"
  exit 1
fi

say "Folders and configuration"
UBIQUITOUS_DEV_SETUP=1 bash scripts/setup/init-config.sh

say "Restore and build"
dotnet build -v quiet --nologo

cat <<'EOF'

Ready. Next:
  1. cp /path/to/your-client.ipa incoming/
  2. just ingest        # builds the data package from it (minutes, once)
  3. just dev           # or: just up   (Docker: server + Postgres)
  4. just account create <name>   # your own login (the test login above works only until you do)
The server listens on ws://127.0.0.1:8781/. Without step 1-2 it still runs,
on a tiny synthetic world.
EOF
