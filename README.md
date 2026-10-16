# Ubiquitous Server

An independent, unofficial game server for a discontinued mobile MMORPG (see
[Legal](#legal)), written in C# on .NET 10. Every value it uses is a game fact
a player can observe, comes from the operator's own client data files, or is
this project's own design choice. It sends identity, rules, state and events;
[ubiquitous-client](https://github.com/UbiquitousCore/ubiquitous-client)
renders from the player's own files.

> **Target build: 1.1.4.** Rules and data follow the 1.1.4 client. Other game
> versions can be supported: builds close to 1.1.4 should need little work,
> more distant ones more.
>
> **Status: playable over WebSocket.** Login, movement, combat, spells, quests,
> inventory and social systems run end to end against the client, persisting
> to JSON files or Postgres. Open work is in the
> [issue tracker](https://github.com/UbiquitousCore/ubiquitous-server/issues).

## Quick start

```bash
just setup   # once: writes .env and server.json from their examples
just dev     # run with editable JSON saves, no Docker
just up      # or the full stack (server plus Postgres) in Docker
just dev --port 9000      # another port; `just port=9000 up` for Docker
dotnet build && dotnet test
```

No client data is needed to build, test or run; the server falls back to
synthetic content. To use your own client files, drop the build into
`incoming/` and run `just ingest` (see
[docs/development/setup.md](docs/development/setup.md)).

## Configuration

Game settings live in `server.json` (git-ignored; `just setup` copies it from
`server.example.json`). Both `just dev` and `just up` read it and serve
`ws://127.0.0.1:8781/`:

```json
{
  "festivalContentEnabled": true,
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
