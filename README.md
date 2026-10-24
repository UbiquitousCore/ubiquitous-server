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

Two ways in. Both start the same way: put your own legally obtained 1.1.4
client build (the `.ipa`) in `incoming/`. This repo does not host any .ipa files, these should be supplied yourself.

### Just run the server (needs only Docker)

Install [Docker Desktop](https://docs.docker.com/get-docker/) (or OrbStack /
Docker Engine), then:

```bash
./run.sh ingest    # once: builds the data package from incoming/*.ipa
./run.sh start     # server + Postgres; ws://127.0.0.1:8781/
```

The import is memory-hungry. If `ingest` is killed partway (a bare "Killed" or
exit code 137), raise Docker Desktop's memory limit (Settings → Resources) and
run it again; OrbStack and Docker Engine on Linux have no such cap by default.

On Windows use `run.bat ingest` and `run.bat start` (or `.\run.ps1`).
`./run.sh stop` stops it, `./run.sh logs` follows the log. Nothing else is
installed on your machine; the first run creates `.env` with a generated
database password.

Nobody can sign in until you make an account, with the server running:

```bash
./run.sh account create alice     # asks for a password (8+ characters)
./run.sh account password alice   # change it later
./run.sh account gm alice on      # make alice a GM (admin panel and tools); "off" removes it
./run.sh account list             # GMs are marked
```

GM status applies at that player's next sign-in. It belongs to the account, so
every character on it is a GM. Docker never uses development logins: with
Postgres the server ignores them, so real accounts are the only way in.

On Windows: `run.bat account create alice`. Players sign in to
[ubiquitous-client](https://github.com/UbiquitousCore/ubiquitous-client) with
that name and password; they make their characters in the game.

### Updating

There is no published image yet, so the server is built from your checkout.
After pulling new changes, rebuild and restart it:

```bash
git pull
./run.sh start     # rebuilds the server image, then restarts it (run.bat start on Windows)
```

Your saves are kept: accounts, characters, inventory and quests are in the
Postgres database; mail, guilds, friends and the auction house are files in the
server's state volume. Both are Docker volumes, so they survive a rebuild and
are removed only by `docker compose down -v`.
Run `./run.sh ingest` again only when a change touches the importer or the data
it produces; the importer image is rebuilt on each `ingest`.

### Develop on it (.NET 10, just)

```bash
./setup.sh         # Windows: setup.bat. Installs .NET 10, just, Python if missing
just ingest        # once: the same import, run natively
just dev           # run with editable JSON saves, no Docker
just up            # or the Docker stack, built from your checkout
```

`just dev` rebuilds and reloads while you edit (live where .NET can, a restart
where it cannot); for the Docker stack, `just up` rebuilds the image after a
pull.

Make a login with `just account create alice` (also `password`, `gm` and
`list`; works while the server runs). `setup` also prints a throwaway test login
that is always there in `just dev`, next to any real accounts, and never in Docker. Point [ubiquitous-client](https://github.com/UbiquitousCore/ubiquitous-client)
at `ws://127.0.0.1:8781/` and sign in.

No client data is needed to build, test or run; the server falls back to a
tiny synthetic world. Other commands: `just --list`, `just port=9000 up`,
`dotnet build && dotnet test`. Details:
[docs/development/setup.md](docs/development/setup.md).

## Configuration

Game settings live in `server.json` (git-ignored; `just setup` copies it from
`server.example.json`). Both `just dev` and `just up` read it and serve
`ws://127.0.0.1:8781/`:

```json
{
  "festivalContentEnabled": false,
  "modules": { "playerbots": { "enabled": true, "randomBotCount": 5 } }
}
```

Secrets come from `.env`, `.env.production` (`just up .env.production`) or the
environment. Precedence: defaults, `server.json` (`--config` or
`UBIQUITOUS_CONFIG` points elsewhere), `UBIQUITOUS_*` variables, flags.

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
