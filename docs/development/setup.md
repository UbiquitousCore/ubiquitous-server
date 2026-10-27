# Development

Two tools at most: the .NET 10 SDK, or Docker. Neither path has logic of its
own; both run the same `server` and `asset-extractor` commands.

## Running from source

```bash
dotnet run --project tools/asset-extractor -- --ingest incoming   # once, see below
dotnet watch --project src/server run                              # rebuilds and reloads on edits
```

or, with nothing but Docker:

```bash
docker compose run --rm ingest
docker compose up dev
```

`dev` runs the same `dotnet watch` in the SDK image, with the checkout
mounted and its build output in a volume, so it never collides with a build
on the host. It polls for changes, since bind mounts from macOS and Windows
do not deliver change events.

Both serve `ws://127.0.0.1:8781/` (health at `/health`) with saves as
editable JSON files in `data/generated/`, no Postgres, so the
`ubiquitous-client` client's `npm run dev` needs no change between them.
Without a package in `data/generated` the server runs on synthetic content.

## Server settings

Game settings live in `server.json` (git-ignored; copy
`server.example.json`): the port, festival content, and the `modules`
section — `"playerbots": { "enabled": true, "randomBotCount": 5 }` starts
that many bots. The server reads `server.json` and `.env` from the folder it
is started in, so a plain `dotnet run` from the repository root picks both
up. Precedence is defaults, then `server.json` (`--config` or
`UBIQUITOUS_CONFIG` points elsewhere), then `.env`, then `UBIQUITOUS_*`
environment variables, then flags. A variable set in the environment wins
over the same name in `.env`.

## Accounts

Accounts are made from the command line, never over the wire:
`dotnet run --project src/server -- account create <name>` (file saves), or
`docker compose exec server dotnet server.dll account create <name>` (the
Docker stack, running). `password <name>` changes one, `gm <name> on|off`
grants or removes GM (admin tools such as teleport; it applies at their next
sign-in and covers every character on the account), and `list` shows them
with GMs marked. The password is read without echo (or one line from piped
stdin), needs 8 characters, and is stored as a salted hash. Postgres takes the
change at once; file saves reload `accounts.json` when it changes, so the
server need not stop. The first account also writes an empty
`characters.json`; until both files exist the server runs on the development
fixtures, so it takes that first account only once it restarts. Real accounts are numbered from 100, and listing accounts
changes nothing on disk. The development logins below stay available next to
them.

## Development logins

For file saves only, and always there with them, whether or not real
accounts exist: their characters are added to the saves (once, then kept as
you play them). With a Postgres connection the server ignores them and
`dev.enterWorld` is refused, whatever `.env` says.

Copy `env.example` to `.env`, set `UBIQUITOUS_DEVELOPMENT_ACCOUNTS=true` and a
password in `UBIQUITOUS_DEVELOPMENT_PASS_HASH`; without one nobody can sign
in. With no saved `accounts.json`, two logins exist:
`UBIQUITOUS_DEVELOPMENT_USER_ID` (default `developer`) and
`UBIQUITOUS_DEVELOPMENT_SECOND_USER_ID` (default `developer2`), each only when
its `UBIQUITOUS_DEVELOPMENT_PASS_HASH` / `UBIQUITOUS_DEVELOPMENT_SECOND_PASS_HASH`
is set. Each holds two level-60 characters with every spell rank and a full
talent build. Never enable them on a shared server.

## Importing your own client data

Drop the client build (usually the `.ipa`) into `incoming/` — a git-ignored
folder this repository never commits — and run one command:

```bash
dotnet run --project tools/asset-extractor -- --ingest incoming
```

(`docker compose run --rm ingest` runs the same command in a container.) It
pulls `tables.bin` out of the build, unpacks the tables, the per-map world
archives and the collision models into `data/raw/`, imports, validates and
installs the package (keeping saves; the one it replaced goes to
`data/generated.previous`), counts the playable looks, and bakes walkable
ground for every map. The argument may be any folder, the `.ipa` itself, or
a `tables.bin`; `--data <dir>` writes somewhere other than `data/`. A folder
holding only `tables.bin` imports without world or collision data.

The steps, for re-runs and debugging, are the extractor's own commands
(see `tools/asset-extractor/README.md`), for example:

```bash
dotnet run --project tools/asset-extractor -- --bake data/generated \
  --tables-dir data/raw/tables --models data/raw/model --world-dir data/raw/world
```

Never commit anything under `data/`, or any client file. The server targets
build 1.1.4 and warns on any other.

## Docker

`docker compose up -d` starts the stack: a one-shot `secrets` container that
generates the database password into a volume (or keeps the
`POSTGRES_PASSWORD` an older `.env` holds, since an existing database was
made with it), Postgres, the server with `data/generated/` mounted read-only
and runtime state in the `ubiquitous-state` volume
(`UBIQUITOUS_STATE_DIRECTORY`), the published client, and Caddy in front of
both. Images come from GHCR; `up --build` builds the server from the
checkout instead. CI publishes `ubiquitous-server`, `ubiquitous-server-ingest`
and `ubiquitous-client` for amd64 and arm64 on pushes to `main`.

The server's own port is published on loopback only, for a client run with
`npm run dev`; set `UBIQUITOUS_PUBLISH_ADDRESS=0.0.0.0` to serve it to a LAN
directly. Players reach the stack through the proxy instead, on
`UBIQUITOUS_SITE` (see the README). Patrol routes saved under Docker land
inside the container unless `content/` is mounted.

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
