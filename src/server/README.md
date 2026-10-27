# Ubiquitous.Server

Composition root and host. `Program` reads `ServerOptions`, sets up logging,
and wires the pieces in `Hosting/`:

| Type | Owns |
|---|---|
| `ServerContentLoader` | the data package and `content/` into `ServerContent`, with development fallbacks |
| `SessionStoreFactory` | file or Postgres stores (`SessionStores`) |
| `SharedWorld` | services every session shares (maps, duels, trades, instances, ...) and the world tick |
| `PlaySessionFactory` | one `PlaySession` per connection |
| `DevelopmentFixtures` | spawns, accounts and characters used with no data package |
| `ServerModules`, `ModuleHost`, `ModuleAccounts` | the modules built in from `modules/` (see `modules/README.md`) |

`WebSocketGateway` owns the socket, `/health` and the tick loop (`WorldTick`);
`LocalConnection` is a client with no socket, for modules; `PlaySessionHub`
delivers between sessions and `PlayerPresence` tracks who is where. Tests:
`tests/integration`.

## Finding a message handler

Look up the message type in the dispatch switch in
`PlaySession.HandleTextAsync`. It calls either a handler or a `PlaySession`
partial.

Handlers (`Handlers/`) take `ISessionPlayer` and `SessionOutbox` plus what they
own: `ChatHandler`, `EmoteHandler`, `ReportHandler`, `SocialHandler`,
`GuildHandler`, `PartyHandler`, `NavigationHandler`, `MailHandler`,
`AuctionHandler`, `TradeHandler`. `PlayerBelongings` is the player's inventory,
money and storage, shared by the session and the commerce handlers.

The `PlaySession.*.cs` partials hold the simulation core, which shares the
session's combat, visibility and quest state. `PlaySession.Reach.cs` is the
one distance gate every routed message passes.

A new message family that needs only the player is a new handler plus one line
in the dispatch switch.

## Running

```powershell
dotnet watch --project src/server run   # file saves, rebuilds on edits
docker compose up -d                    # the server plus Postgres in Docker
dotnet run --project src/server -- [--config server.json] [--data-dir data/generated] [--port 8781]
```

`server.example.json` documents the file (bots, festival content, ports);
the server reads `server.json` and `.env` from the folder it starts in, and
`UBIQUITOUS_CONFIG` or `--config` names another file. Settings apply in order:
defaults, JSON config, `.env`, `UBIQUITOUS_*` environment variables,
command-line options. `ServerOptions.Settings` declares each one once with its
config key, environment variable and flags: bind address, port, data directory,
content directory, Postgres connection string, development accounts, festival
content, disabled races, log level (`Trace` to `Critical`). Module settings are
under `modules` in the config file or `UBIQUITOUS_MODULE_*` in the environment.
