# Ubiquitous Server

An independent, unofficial game server for a Order and Chaos Online (see
[Legal](#legal)), written in C# on .NET 10. Every value it uses is a game fact
a player can observe, comes from the operator's own client data files, or is
this project's own design choice.

This server only works with the [ubiquitous-client](https://github.com/UbiquitousCore/ubiquitous-client), and does not support official clients such as the iOS, Android, MAc or Windows versions as this server uses it's own protocol to communicate.

> **Target build: 1.1.4.** Rules and data follow the 1.1.4 client. Other game
> versions can be supported: builds close to 1.1.4 should need little work,
> more distant ones more.
>
> **Status: playable over WebSocket.** Login, movement, combat, spells, quests,
> inventory and social systems run end to end against the client, persisting
> to JSON files or Postgres. Open work is in the
> [issue tracker](https://github.com/UbiquitousCore/ubiquitous-server/issues).

## Quick start

You need [Docker](https://docs.docker.com/get-docker/) (Docker Desktop,
OrbStack or Docker Engine) and your own legally obtained 1.1.4 client build
(the `.ipa`). This repo does not host any .ipa files, these should be supplied
yourself. Nothing else is installed on your machine.

### Run a server

Make a folder for it, with `incoming` and `data` folders inside, and put
[`docker-compose.yml`](https://raw.githubusercontent.com/UbiquitousCore/ubiquitous-server/main/docker-compose.yml)
in it (save the link, or download it as below). Copy your `.ipa` into
`incoming`.

```bash
mkdir ubiquitous
cd ubiquitous
mkdir incoming data
curl -fsSLO https://raw.githubusercontent.com/UbiquitousCore/ubiquitous-server/main/docker-compose.yml
```

On Windows, type `curl.exe` rather than `curl` in PowerShell. A clone of this
repository works the same way, from its root. Then, in that folder:

```bash
docker compose run --rm ingest   # once: builds the game data from incoming/*.ipa
docker compose up -d             # server, database, client and proxy
docker compose exec server dotnet server.dll account create alice
```

Open <http://localhost> on this machine and choose your `.ipa`; the first visit unpacks it
in your browser, once. Sign in as `alice`. The containers restart on their
own after a crash or a reboot, as long as Docker itself starts with your
machine.

`account` also takes `password alice`, `gm alice on` (admin panel and GM
tools; `off` removes it) and `list`. GM status applies at the player's next
sign-in and covers every character on the account.

The import takes a few minutes and about 2 GB of memory. If `ingest` is
killed partway (a bare "Killed" or exit code 137), raise Docker Desktop's
memory limit (Settings → Resources) and run it again. On Linux, put
`UBIQUITOUS_UID=` and `UBIQUITOUS_GID=` with your own ids (`id -u`, `id -g`)
in a `.env` beside the compose file, so the files it writes are yours rather
than root's.

### Changing settings

Settings go in a text file named `.env` beside the compose file, one
`NAME=value` per line, and take effect with `docker compose up -d`. For
example, to turn the seasonal festival items off and run ten bots:

```
UBIQUITOUS_FESTIVAL_CONTENT_ENABLED=false
UBIQUITOUS_MODULE_PLAYERBOTS_ENABLED=true
UBIQUITOUS_MODULE_PLAYERBOTS_RANDOM_BOT_COUNT=10
```

| Setting | Default | Does |
|---|---|---|
| `UBIQUITOUS_FESTIVAL_CONTENT_ENABLED` | `true` | winter solstice and anniversary scenery, creatures and items |
| `UBIQUITOUS_MODULE_PLAYERBOTS_ENABLED` | `false` | start bots |
| `UBIQUITOUS_MODULE_PLAYERBOTS_RANDOM_BOT_COUNT` | `5` | how many bots play on their own (0-500) |
| `UBIQUITOUS_MAX_CONNECTIONS` | `500` | players connected at once |
| `UBIQUITOUS_DISABLED_RACES` | none | race ids that cannot be picked, comma-separated |
| `UBIQUITOUS_LOG_LEVEL` | `Information` | `Trace` to `Critical` |
| `UBIQUITOUS_SITE` | `localhost` | the address other devices open (below) |
| `UBIQUITOUS_HTTP_PORT`, `UBIQUITOUS_HTTPS_PORT` | `80`, `443` | the proxy's ports (then open `http://localhost:<port>`) |

Every other server setting works the same way, by its `UBIQUITOUS_*` name;
[env.example](env.example) lists them.

### Play from a phone or another computer

Browsers keep the unpacked game data only for a page on https (or on
localhost), so for other devices the stack serves everything through one
https address with its own certificate. In `.env`, name the address
other devices will use (this machine's LAN address here):

```
UBIQUITOUS_SITE=192.168.1.20
```

then run `docker compose up -d` again.

On each device, open `https://192.168.1.20/root.crt` once, accept the warning,
and install the certificate as trusted (iOS: Settings → General → About →
Certificate Trust Settings). Then open `https://192.168.1.20`.

To play from anywhere, give it a real domain instead
(`UBIQUITOUS_SITE=play.example.com`, with ports 80 and 443 forwarded to this
machine): the proxy then gets a public certificate on its own and no device
needs anything installed. [Tailscale](https://tailscale.com) is the way to
reach it from your own devices without opening ports.

### Updating

```bash
docker compose pull
docker compose up -d
```

Saves are kept: accounts, characters, inventory and quests are in the
Postgres volume; mail, guilds, friends and the auction house in the server's
state volume. Only `docker compose down -v` removes them. Run
`docker compose run --rm ingest` again only when a change touches the
importer, then `docker compose restart server`.

## Develop on it

Clone this repository and
[ubiquitous-client](https://github.com/UbiquitousCore/ubiquitous-client).
Two ways to run the server from source; both serve `ws://127.0.0.1:8781/`,
where the client's `npm run dev` looks for it, and both keep saves as
editable JSON files in `data/generated/` instead of Postgres.

**With .NET 10** (the usual way, for a debugger and an IDE):

```bash
dotnet run --project tools/asset-extractor -- --ingest incoming   # once
dotnet watch --project src/server run
```

Run both from the repository root: the server reads `.env` and
`server.json` from the folder it starts in.

**With Docker only:**

```bash
docker compose run --rm ingest
docker compose up dev            # dotnet watch inside a container
```

Run either instead of `docker compose up`, not beside it: they use the same
port. `docker compose up -d --build` runs the full stack built from your
checkout rather than the published images.

To sign in without making accounts, copy `env.example` to `.env`, set
`UBIQUITOUS_DEVELOPMENT_ACCOUNTS=true` and a password in
`UBIQUITOUS_DEVELOPMENT_PASS_HASH`, and sign in as `developer`. Real accounts
work too: `dotnet run --project src/server -- account create alice` (or
`docker compose exec dev dotnet run --project src/server -- account create alice`).
The very first one takes effect once the server restarts; later ones at once.

No client data is needed to build, test or run; the server falls back to a
tiny synthetic world. `dotnet build && dotnet test` checks everything. Details:
[docs/development/setup.md](docs/development/setup.md).

## Configuration

Game settings live in `server.json` (git-ignored; copy it from
`server.example.json`). The server reads it from the folder it is started in:

```json
{
  "festivalContentEnabled": false,
  "modules": { "playerbots": { "enabled": true, "randomBotCount": 5 } }
}
```

In a clone, the Docker stack reads it too when `.env` holds
`COMPOSE_FILE=docker-compose.yml:docker-compose.server-json.yml` (on Windows,
separate them with `;`). Settings apply in order: defaults, `server.json`,
`.env`, `UBIQUITOUS_*` environment variables, command-line flags.

## Documentation

| Doc | Covers |
|---|---|
| [AGENTS.md](AGENTS.md) | project contract: sources, naming, boundaries, done |
| [docs/BEHAVIOUR.md](docs/BEHAVIOUR.md) | what the server does, as a client sees it |
| [docs/PROVENANCE.md](docs/PROVENANCE.md) | where each value comes from |
| [docs/GAMEPLAY_READINGS.md](docs/GAMEPLAY_READINGS.md) | what the game shows, with dated readings and references |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | tick, delivery, scripts, data |
| [docs/architecture/](docs/architecture) | wire codes, walkable ground |
| [docs/development/setup.md](docs/development/setup.md) | setup, data import, Docker, tests |
| [Issues](https://github.com/UbiquitousCore/ubiquitous-server/issues) | open work |

## License

Licensed under AGPL-3.0-or-later (see
[LICENSE](LICENSE) and [NOTICE](NOTICE)).

## Legal

This is an unofficial, non-commercial fan project. It is not affiliated with,
endorsed by, or sponsored by Gameloft SE or any rights holder. "Order & Chaos
Online" is a trademark of its owner and is used here only to say which game
files this software is compatible with.

All code here was written for this project. It contains no game code and no
game files (art, audio, models or text) and never ships or downloads any. Game
rules the server needs, such as experience per level, base stats and starting
equipment, are written in this project's own code; see
[docs/PROVENANCE.md](docs/PROVENANCE.md).

To use real game data you must supply your own legally obtained copy of the
game; the importer runs locally. This project does not tell you where to
obtain it and will not help you do so.

File-format and protocol details are recorded only as far as needed for
interoperability; see [docs/PROVENANCE.md](docs/PROVENANCE.md).

Rights holders: if you believe anything here infringes your rights, open an
issue. It will be reviewed and removed promptly.
