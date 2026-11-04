# Hosting guide

Everything after the [quick start](../README.md#quick-start): accounts,
settings, playing from other devices, updating, and what to do when something
is wrong. Commands run in the folder that holds `docker-compose.yml`.

## Accounts

Accounts are made and changed from this machine, with the server running.
Each command is the whole line to type; swap `alice` for the account's name
(3-32 letters, digits or `_ . @ -`).

```bash
# Make an account; you are asked for its password twice.
docker compose exec server dotnet server.dll account create alice
# Set a new password for an account.
docker compose exec server dotnet server.dll account password alice
# Make an account an admin (GM)...
docker compose exec server dotnet server.dll account gm alice on
# ...or take it away again.
docker compose exec server dotnet server.dll account gm alice off
# List every account, with GM beside the admins.
docker compose exec server dotnet server.dll account list
```

Admin status applies at the player's next sign-in and covers every character
on the account. An admin gets an admin panel in the game, with:

- teleport to any zone, a faster walking speed, and fly (space up, shift down);
- give themselves items or gold, set their level (1-60), learn every spell;
- invincibility until they leave the world;
- draw distance, the debug HUD, the collision wireframe and fog off.

In the world, an admin also passes the server's movement checks, has a kill
spell and a full heal, and can type `/stress <count>` in chat to spawn a crowd
at the nearest training dummy for load testing. Give it only to people you
trust with the whole server.

## Changing settings

Settings go in the `.env` beside the compose file, which the first `ingest`
wrote, one `NAME=value` per line, and take effect with `docker compose up -d`. For
example, to turn the seasonal festival items on and run ten bots:

```
UBIQUITOUS_FESTIVAL_CONTENT_ENABLED=true
UBIQUITOUS_MODULE_PLAYERBOTS_ENABLED=true
UBIQUITOUS_MODULE_PLAYERBOTS_RANDOM_BOT_COUNT=10
```

| Setting | Default | Does |
|---|---|---|
| `UBIQUITOUS_FESTIVAL_CONTENT_ENABLED` | `false` | winter solstice and anniversary scenery, creatures and items |
| `UBIQUITOUS_XP_RATE` | `1` | multiplies all experience gained (`1.5` is 1.5x) |
| `UBIQUITOUS_EXHAUSTION_LIMIT` | `100` | Legend exhaustion a player can build up before the daily reset |
| `UBIQUITOUS_PLAYER_DAMAGE_RATE` | `1` | multiplies the damage players deal |
| `UBIQUITOUS_CREATURE_DAMAGE_RATE` | `1` | multiplies the damage creatures deal |
| `UBIQUITOUS_CREATURE_HEALTH_RATE` | `1` | multiplies every creature's health |
| `UBIQUITOUS_MODULE_PLAYERBOTS_ENABLED` | `false` | start bots |
| `UBIQUITOUS_MODULE_PLAYERBOTS_RANDOM_BOT_COUNT` | `5` | how many bots play on their own (0-500) |
| `UBIQUITOUS_MAX_CONNECTIONS` | `500` | players connected at once |
| `UBIQUITOUS_DISABLED_RACES` | none | race ids that cannot be picked, comma-separated |
| `UBIQUITOUS_LOG_LEVEL` | `Information` | `Trace` to `Critical` |
| `UBIQUITOUS_SITE` | `localhost` | the address other devices open (below) |
| `UBIQUITOUS_HTTP_PORT`, `UBIQUITOUS_HTTPS_PORT` | `80`, `443` | the proxy's ports (then open `http://localhost:<port>`) |

Every other server setting works the same way, by its `UBIQUITOUS_*` name;
[env.example](env.example) lists them.

## Play from a phone or another computer

Browsers keep the unpacked game data only for a page on https (or on
localhost), so for other devices the stack serves everything through one
https address with its own certificate. In `.env`, name the address
other devices will use (this machine's LAN address here):

```
UBIQUITOUS_SITE=192.168.1.20
```

then run `docker compose up -d` again.

On each device, open `https://192.168.1.20/root.crt` once, accept the
warning, and install the certificate as trusted:

- **iPhone / iPad:** Settings → General → VPN & Device Management → install
  the downloaded profile, then Settings → General → About → Certificate Trust
  Settings → turn on full trust for it.
- **Android:** the link only downloads the file. Install it from Settings →
  Security → Encryption & credentials → Install a certificate → **CA
  certificate** (menu names vary by brand; search Settings for "CA
  certificate").

Then open `https://192.168.1.20`.

If the page still says "Not secure":

- **Restart the browser.** If you opened the page and went past the warning
  before installing the certificate, the browser keeps treating it as
  insecure until it restarts. On Android, force-stop Chrome (Settings → Apps →
  Chrome → Force stop) and open the address again; on iOS, close the tab.
- **Check where it was installed.** On Android it must be a CA certificate:
  it should be listed as "Caddy Local Authority" under Settings → Encryption &
  credentials → Trusted credentials → User. "VPN & app user certificate" and
  "Wi-Fi certificate" do not work.
- **Firefox on Android** ignores certificates you install unless you turn on
  Settings → About Firefox (tap the logo five times) → back to Settings →
  Secret Settings → "Use third party CA certificates". Chrome and Samsung
  Internet use them as they are.
- **The address changed.** The router assigns this machine's LAN address and
  can change it; update `UBIQUITOUS_SITE` and run `docker compose up -d`.
  Reserving a fixed address for the machine in the router stops this.
- **Nothing loads at all.** Check that the phone is on the same network, and
  that this machine's firewall lets Docker (or OrbStack) accept incoming
  connections.
- **The certificate changed.** It lives in the `caddy-data` volume; after
  `docker compose down -v`, every device has to install the new one.

To play from anywhere, give it a real domain instead
(`UBIQUITOUS_SITE=play.example.com`, with ports 80 and 443 forwarded to this
machine): the proxy then gets a public certificate on its own and no device
needs anything installed. [Tailscale](https://tailscale.com) is the way to
reach it from your own devices without opening ports.

## Updating

```bash
# Download the latest compose file over the old one (.env is not touched).
curl -fsSLO https://raw.githubusercontent.com/UbiquitousCore/ubiquitous-server/main/docker-compose.yml
# Fetch the newest version of every image the stack uses.
docker compose pull
# Restart every container on the new images and compose file.
docker compose up -d --force-recreate
```

The first line refreshes the compose file itself (`curl.exe` on Windows; in
a clone, `git pull` instead); your `.env` is kept. `--force-recreate` is
needed because Docker does not notice a change to the proxy's settings,
which live inside the compose file.

Saves are kept: accounts, characters, inventory and quests are in the
Postgres volume; mail, guilds, friends and the auction house in the server's
state volume. Only `docker compose down -v` removes them. Run
`docker compose run --rm ingest` again only when a change touches the
importer, then `docker compose restart server`.

## Settings file (`server.json`)

Game settings live in `server.json` (git-ignored; copy it from
`server.example.json`). The server reads it from the folder it is started in:

```json
{
  "festivalContentEnabled": true,
  "xpRate": 1.5,
  "modules": { "playerbots": { "enabled": true, "randomBotCount": 5 } }
}
```

In a clone, the Docker stack reads it too when `.env` holds
`COMPOSE_FILE=docker-compose.yml:docker-compose.server-json.yml` (on Windows,
separate them with `;`). Settings apply in order: defaults, `server.json`,
`.env`, `UBIQUITOUS_*` environment variables, command-line flags.
