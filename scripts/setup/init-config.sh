#!/usr/bin/env bash
# Writes .env (generated secrets) and server.json when missing, and creates the
# folders the other scripts expect. Shared by setup.sh and run.sh; safe to re-run.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/../.."

random() { head -c 24 /dev/urandom | base64 | tr -dc 'A-Za-z0-9' | head -c "${1:-20}"; }

mkdir -p incoming data/generated data/cache
if [ ! -e .env ]; then
  db_pass="$(random 24)"
  sed -e "s|^POSTGRES_PASSWORD=.*|POSTGRES_PASSWORD=${db_pass}|" env.example > .env
  # Development logins are for `just dev` only (the server ignores them with Postgres).
  if [ -n "${UBIQUITOUS_DEV_SETUP:-}" ]; then
    dev_pass="$(random 16)"
    sed -i.bak -e "s|^UBIQUITOUS_DEVELOPMENT_ACCOUNTS=.*|UBIQUITOUS_DEVELOPMENT_ACCOUNTS=true|" \
        -e "s|^UBIQUITOUS_DEVELOPMENT_PASS_HASH=.*|UBIQUITOUS_DEVELOPMENT_PASS_HASH=${dev_pass}|" .env && rm -f .env.bak
    echo "wrote .env with generated secrets"
    echo "  test login for just dev: developer / ${dev_pass}   (also in .env)"
  else
    echo "wrote .env with a generated database password"
  fi
  chmod 600 .env
fi
if [ ! -e server.json ]; then cp server.example.json server.json; echo "wrote server.json"; fi
