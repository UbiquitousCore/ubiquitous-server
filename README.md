# Ubiquitous Server

An independent, unofficial game server for a discontinued mobile MMORPG (see
[Legal](#legal) for which), written in C# on .NET 10. Every value it uses is
a game fact a player can observe, comes from the operator's own client data
files, or is this project's own design choice.

> **Status: playable over WebSocket.** Login, character entry, visibility,
> movement, combat, spells, quests, inventory and the social systems run end to
> end against the `ubiquitous-client` client, persisting to JSON files or Postgres.
> [`docs/BEHAVIOUR.md`](docs/BEHAVIOUR.md) describes what each system does;
> [`TODO.md`](TODO.md) lists what is missing.

The server sends identity, rules, state and events; `ubiquitous-client` renders from the
player's own client files. Game logic is transport-independent and never sees
raw client formats.

## Quick start

```bash
just setup   # once: writes .env and server.json from their examples
just dev     # run with editable JSON saves, no Docker
just up      # or the full stack (server plus Postgres) in Docker
```

```bash
dotnet build
dotnet test
```

No client data is needed to build, test or run; the server falls back to
synthetic content. With your own client files, drop the build into
`incoming/` and run `just ingest`; the steps and the folder setting are in
[docs/development/setup.md](docs/development/setup.md).

## Configuration

Game settings live in `server.json` (git-ignored; `just setup` copies it
from `server.example.json`), and both `just dev` and `just up` read it, on
the same port (`ws://127.0.0.1:8781/`), so one edit covers both modes:

```json
{
  "festivalContentEnabled": true,
  "modules": { "playerbots": { "enabled": true, "randomBotCount": 5 } }
}
```

Secrets stay out of it: they come from `.env` (local Docker), from
`.env.production` (`just up .env.production`), or from the real
environment — and anything set there wins over the file. Full precedence:
defaults, then `server.json` (`--config` or `UBIQUITOUS_CONFIG` points elsewhere),
then `UBIQUITOUS_*` variables, then flags.

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
| [TODO.md](TODO.md) | open work |

## License

Licensed under AGPL-3.0-or-later (see
[LICENSE](LICENSE) and [NOTICE](NOTICE)).

## Legal

This is an unofficial, non-commercial fan project. It is not affiliated with,
endorsed by, or sponsored by Gameloft SE or any rights holder. "Order & Chaos
Online" is a trademark of its owner and is used here only to say which game
files this software is compatible with.

Every line of code here was written for this project, and it is licensed
under AGPL-3.0-or-later. It contains no game code and no game files — no art,
audio, models or text files — and never ships or downloads any. Some game
rules the server needs, such as experience per level, base stats and
starting equipment, are written in this project's own code; see
[docs/PROVENANCE.md](docs/PROVENANCE.md).

To run it with real game data you must supply your own legally obtained copy
of the game; the data importer runs locally on your machine. This project
does not tell you where to obtain the game and will not help you do so.

File-format and protocol details are recorded only as far as needed for
interoperability; see [docs/PROVENANCE.md](docs/PROVENANCE.md).

Rights holders: if you believe anything here infringes your rights, open an
issue. It will be reviewed and removed promptly.
