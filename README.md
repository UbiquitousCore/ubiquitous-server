# Ubiquitous Server

A community-run server for Order & Chaos Online, with a web client that plays
in your browser. Host a world for yourself and your friends, or play on one
someone else hosts.

> Unofficial fan project, not affiliated with Gameloft. You supply your own
> legally obtained copy of the game; nothing from it is hosted here.
> See [Legal](#legal).

**Status:** playable. Login, movement, combat, spells, quests, inventory and
social features work end to end. Open work is in the
[issue tracker](https://github.com/UbiquitousCore/ubiquitous-server/issues).

This server speaks its own protocol, so it works with
[ubiquitous-client](https://github.com/UbiquitousCore/ubiquitous-client)
(included below) and not with the official iOS, Android, Mac or Windows apps.

## What you need

- [Docker](https://docs.docker.com/get-docker/) (Docker Desktop or OrbStack on
  Mac and Windows, Docker Engine on Linux). Nothing else is installed.
- Your own **1.1.4** game file (the `.ipa`).
- About 2 GB of free memory for the one-time import, and about 1.5 GB of disk
  per player's browser for the unpacked game.

## Quick start

**1. Set up a folder.** Make a folder, put an `incoming` folder in it, copy your
`.ipa` into `incoming`, and download
[`docker-compose.yml`](https://raw.githubusercontent.com/UbiquitousCore/ubiquitous-server/main/docker-compose.yml)
next to it:

**macOS / Linux** (Terminal):

```bash
mkdir -p ubiquitous/incoming
cd ubiquitous
curl -fsSLO https://raw.githubusercontent.com/UbiquitousCore/ubiquitous-server/main/docker-compose.yml
```

**Windows** (PowerShell):

```powershell
mkdir ubiquitous\incoming
cd ubiquitous
Invoke-WebRequest https://raw.githubusercontent.com/UbiquitousCore/ubiquitous-server/main/docker-compose.yml -OutFile docker-compose.yml
```

Every command from here on is the same on every system, typed in that
`ubiquitous` folder.

**2. Import the game data and start.** The import takes a few minutes and asks
three questions; pressing Enter for each is fine.

```bash
docker compose run --rm ingest
docker compose up -d
```

**3. Make your account** (it asks for a password twice):

```bash
docker compose exec server dotnet server.dll account create alice
```

**4. Play.** Open <http://localhost>, choose your `.ipa` (the first visit
unpacks it in your browser, once; it is not uploaded anywhere), and sign in as
`alice`.

**Manage it from the admin page** (optional). Make `alice` a GM, then open
<http://localhost:8790> on this machine and sign in as `alice` to manage
accounts, characters and settings:

```bash
docker compose exec server dotnet server.dll account gm alice on
```

> **Admin page:** <http://localhost:8790> on this machine; from a phone or
> another computer, `https://<your address>:8443` once
> [turned on](docs/HOSTING.md#the-admin-page).

The server restarts by itself after a crash or reboot, as long as Docker
starts with your machine.

Stop and start it later with:

```bash
docker compose stop
docker compose start
```

## Updating to a new release

Your accounts, characters and `.env` are kept. In the `ubiquitous` folder,
first fetch the latest compose file:

**macOS / Linux:**

```bash
curl -fsSLO https://raw.githubusercontent.com/UbiquitousCore/ubiquitous-server/main/docker-compose.yml
```

**Windows** (PowerShell):

```powershell
Invoke-WebRequest https://raw.githubusercontent.com/UbiquitousCore/ubiquitous-server/main/docker-compose.yml -OutFile docker-compose.yml
```

Then, on any system, pull the new images and restart on them:

```bash
docker compose pull
docker compose up -d --force-recreate
```

[docs/HOSTING.md](docs/HOSTING.md#updating) says when the import needs running
again.

## Next

| I want to... | Read |
|---|---|
| let friends or my phone connect, make more accounts, make someone an admin, turn on bots or festival content, update, or fix a problem | [docs/HOSTING.md](docs/HOSTING.md) |
| manage accounts, characters and settings from a web page | [docs/HOSTING.md#the-admin-page](docs/HOSTING.md#the-admin-page) |
| run the server from source, change code, run the tests | [docs/development/setup.md](docs/development/setup.md) and [CONTRIBUTING.md](CONTRIBUTING.md) |
| understand how it works | [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) and the table below |

## Documentation

| Doc | Covers |
|---|---|
| [docs/HOSTING.md](docs/HOSTING.md) | accounts, settings, phones and LAN, updating, troubleshooting |
| [docs/development/setup.md](docs/development/setup.md) | setup, data import, Docker, tests |
| [docs/BEHAVIOUR.md](docs/BEHAVIOUR.md) | what the server does, as a client sees it |
| [docs/PROVENANCE.md](docs/PROVENANCE.md) | where each value comes from |
| [docs/GAMEPLAY_READINGS.md](docs/GAMEPLAY_READINGS.md) | what the game shows, with dated readings and references |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | tick, delivery, scripts, data |
| [docs/architecture/](docs/architecture) | wire codes, walkable ground |
| [AGENTS.md](AGENTS.md) | project contract: sources, naming, boundaries, done |
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
