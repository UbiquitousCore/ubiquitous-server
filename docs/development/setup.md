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

The admin page manages accounts, characters and settings in a browser:
<http://localhost:8790> with the Docker stack, or
`dotnet run --project tools/admin/web` from a clone (file saves; see
[tools/admin/README.md](../../tools/admin/README.md)).

Accounts are also made from the command line, never over the game socket:
`dotnet run --project src/server -- account create <name>` (file saves), or
`docker compose exec server dotnet server.dll account create <name>` (the
Docker stack, running). `password <name>` changes one, `gm <name> on|off`
grants or removes GM (admin tools such as teleport; it applies at their next
sign-in and covers every character on the account), and `list` shows them
with GMs marked. The password is read without echo (or one line from piped
stdin), needs 8 characters, and is stored as a salted hash. Postgres takes the
change at once; file saves reload `accounts.json` when it changes, so the
server need not stop. The server makes empty `accounts.json` and
`characters.json` on its first start, so until an account exists nobody can
sign in. Everything is saved as you play and kept across restarts. Real
accounts are numbered from 100, and listing accounts changes nothing on disk.

## Testing as a GM

There are no ready-made logins. Make an account, run `account gm <name> on`,
and sign in again: a GM has the admin tools in game (teleport, level, gold,
items, every spell), and the admin page sets level and gold and gives items
to any character that is signed out.

`UBIQUITOUS_DEVELOPMENT_ENTRY=true` (file saves only) lets `dev.enterWorld
{characterRef}` join a character without a password, for local tools. Never
turn it on for a shared server.

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

Never commit anything under `data/`, or any client file. Any build in
`tools/asset-extractor/fingerprints/` imports; the server logs what a build's
files leave out.

## Docker

`docker compose up -d` starts the stack: a one-shot `secrets` container that
generates the database password into a volume (or keeps the
`POSTGRES_PASSWORD` an older `.env` holds, since an existing database was
made with it), Postgres, the server with `data/generated/` mounted read-only
and runtime state in the `ubiquitous-state` volume
(`UBIQUITOUS_STATE_DIRECTORY`), the published client, and Caddy in front of
both, and the admin page (`tools/admin`). The server reads every
`UBIQUITOUS_*` line of `.env` itself as it starts (`UBIQUITOUS_ENV_FILE`
names the host folder's copy), except the paths, bind address and database
the compose file sets itself, so `docker compose restart server` applies a
change. Images come from GHCR; `up --build` builds them from the checkout
instead. CI publishes `ubiquitous-server`, `ubiquitous-server-ingest`,
`ubiquitous-server-admin` and `ubiquitous-client` for amd64 and arm64 on
pushes to `main`.

The server's own port is published on loopback only, for a client run with
`npm run dev`; set `UBIQUITOUS_PUBLISH_ADDRESS=0.0.0.0` to serve it to a LAN
directly. Players reach the stack through the proxy instead: plain
`http://localhost` on the machine running it, and https on `UBIQUITOUS_SITE`
from other devices (see the README). Patrol routes saved under Docker land
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
- Tests that read the operator's own client build (its `.ipa`, app directory or
  a directory holding its packs) skip unless `UBIQUITOUS_TEST_CLIENT_BUILD`
  names it.
- After renaming a message, key or code, regenerate the wire fixtures with
  `UBIQUITOUS_WRITE_WIRE_FIXTURES=1 dotnet test` and copy them to `ubiquitous-client`'s
  `client/test/fixtures/`.
