# Development

## One command

.NET 10 SDK (`scripts/setup/install-dotnet.ps1`); optionally `just` and
Python 3.

```bash
just setup   # once: writes .env and server.json from their examples
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

## Development logins

With `UBIQUITOUS_DEVELOPMENT_ACCOUNTS=true` and no saved `accounts.json`, two logins
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
`just up .env.production`, or export them. Patrol routes saved under Docker
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
