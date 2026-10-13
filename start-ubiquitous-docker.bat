@echo off
setlocal
cd /d "%~dp0"

if exist server.json (
  docker compose -f docker-compose.yml -f docker-compose.server-json.yml up --build
) else (
  docker compose up --build
)
