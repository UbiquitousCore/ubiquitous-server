# Development

## Running without a toolchain

`./run.sh` (`run.bat` on Windows) needs only Docker: `ingest` runs the
importer in a container (`ingest` service, `tools` stage of the Dockerfile)
against `incoming/` and `data/`, `start` brings up the stack. Everything below
is the developer path.

## One command

`./setup.sh` installs the .NET 10 SDK (to `~/.dotnet`) and `just` when
missing, checks for Python 3, and with `--docker` checks Docker and Compose.
On Windows run `setup.bat` (winget installs Git, .NET 10, just and Python;
open a new terminal and run it again), then use Git Bash or PowerShell for
`just`.

```bash
./setup.sh   # once: tools, then .env (generated secrets) and server.json
just dev     # development: saves as editable JSON files, no Docker, no Postgres
just up      # production-like: the server plus Postgres in Docker, same port
just down    # stop the Docker stack
```

Both `dev` and `up` serve `ws://127.0.0.1:8781/` (health at `/health`), so
the `ubiquitous-client` client needs no change between them. Without a package in
`data/generated` the server runs on synthetic content. `just --list` shows
the common tasks; without `just`, the same commands are `dotnet build`,
`dotnet test` and `dotnet run --project src/server -- --port 8781`.

## Server settings

Game settings live in `server.json` (git-ignored; copy
`server.example.json` or run `just setup`): the port, festival content,
and the `modules` section — `"playerbots": { "enabled": true,
"randomBotCount": 5 }` starts that many bots. Both `just dev` and
`just up` read it, so one edit covers both. Secrets stay out of it: they
come from `.env` (local) or `.env.production` (`just up .env.production`)
or the real environment, and anything set there wins over the file.
Precedence is defaults, then `server.json` (`--config` or `UBIQUITOUS_CONFIG`
points elsewhere), then `UBIQUITOUS_*` environment variables, then flags.

## Accounts

Accounts are made from the command line, never over the wire:
`just account create <name>` (file saves), `./run.sh account create <name>`
(Docker, server running), or `dotnet run --project src/server -- account
create <name>`. `password <name>` changes one, `gm <name> on|off` grants or removes GM (admin
tools such as teleport; it applies at their next sign-in and covers every
character on the account), and `list` shows them with GMs marked. The
password is read without echo (or one line from piped stdin), needs 8
characters, and is stored as a salted hash. Postgres takes the change at once;
file saves reload `accounts.json` when it changes, so the server need not stop.
The first account also writes an empty `characters.json`; from then on the
development logins below are gone, since the server only falls back to them
when those files are missing.

## Development logins

For `just dev` (file saves) only. With a Postgres connection the server
ignores them and `dev.enterWorld` is refused, whatever `.env` says.

`./setup.sh` turns these on in `.env` with a generated password for
`developer`; without one nobody can sign in. With
`UBIQUITOUS_DEVELOPMENT_ACCOUNTS=true` and no saved `accounts.json`, two logins
exist: `UBIQUITOUS_DEVELOPMENT_USER_ID` (default `developer`) and
`UBIQUITOUS_DEVELOPMENT_SECOND_USER_ID` (default `developer2`), each only when its
`UBIQUITOUS_DEVELOPMENT_PASS_HASH` / `UBIQUITOUS_DEVELOPMENT_SECOND_PASS_HASH` is set. Each
holds two level-60 characters with every spell rank and a full talent build.
Never enable them on a shared server.

## Importing your own client data

Drop the client build (usually the `.ipa`) into `incoming/` — a git-ignored
folder this repository never commits — and run one command:

```bash
just ingest
```

It pulls `tables.bin` out of the build, unpacks the tables, the per-map
world archives and the collision models, imports and installs the package
(keeping saves), counts the playable looks, and bakes walkable ground for
every map. `UBIQUITOUS_INCOMING_DIR` points it at a folder outside the checkout
(`.env` works, or a shell export); `just ingest /path/to/build` skips the
folder entirely and reads that path. A folder holding only `tables.bin`
imports without world or collision data.

The steps, for re-runs and debugging:

```bash
just extract          # unpack tables.bin into data/raw
just extract-world    # optional: world archives, needed to bake buildings
just reimport         # import, validate and install, keeping saves
just bake             # bake walkable ground
```

Never commit anything under `data/`, or any client file. The server targets
build 1.1.4 and warns on any other.

## Docker

`just up` builds and starts the stack in the background and waits for
`/health`: Postgres plus the server, with `data/generated/` mounted
read-only and runtime state in the `ubiquitous-state` volume
(`UBIQUITOUS_STATE_DIRECTORY`). When `server.json` exists it is mounted read-only
too, so the same game settings apply; `.env` supplies only secrets. For
production secrets, keep them in `.env.production` and run
`just up .env.production` (and `just down .env.production`), or export them. The port is published on loopback only; set `UBIQUITOUS_PUBLISH_ADDRESS=0.0.0.0` to serve a LAN. Patrol routes saved under Docker
land inside the container unless `content/` is mounted.

## Tests

- Tests assert what a client can observe (messages, their order, state), not
  call graphs, and are named for behaviour.
- Fixtures are synthetic and live beside their tests.
- Pin message order where the server guarantees one; no racy asserts.
- Compare wire values as float32, exactly, unless the rule is tolerant.
- Time comes from the `TimeProvider` held by `SharedWorld`; use
  `FakeTimeProvider` via `TestSessions.Create(time: ...)`, never sleeps.
- Drive integration tests through `TestClient` (see `DuelTests.cs`).
- Postgres tests skip unless `UBIQUITOUS_TEST_POSTGRES_CONNECTION_STRING` is set.
- After renaming a message, key or code, regenerate the wire fixtures with
  `UBIQUITOUS_WRITE_WIRE_FIXTURES=1 dotnet test` and copy them to `ubiquitous-client`'s
  `client/test/fixtures/`.
