# Modules

Optional features built into the server. Each folder here holds one module:
a project (`modules/<name>/<name>.csproj`) with a class implementing
`Ubiquitous.Modules.IServerModule`. The server builds every project it finds here and
loads its modules at startup, so adding a module is dropping its folder in and
rebuilding; removing one is deleting the folder.

A module references `src/module-api` (and, if it speaks to the server as a
client does, `src/protocol/web`). It never references the server, persistence
or another module. Through `IModuleHost` it gets:

| Member | What for |
|---|---|
| `Settings` | its own settings (below) |
| `Content` | the loaded game content, read-only |
| `OnWorldTick`, `OnShutdown` | a turn after every world tick, and a chance to tidy up |
| `ReserveLoginAsync`, `Connect` | an account of its own and a socketless client connection that is handled exactly as a WebSocket client is |
| `GroundHeight` | the walkable height at a point, as a client's terrain gives it |
| `IsPlayerNear` | whether a player on a client connection (not a module's) stands near a point |

## Settings

From the JSON config file:

```json
{ "modules": { "playerbots": { "enabled": true, "randomBotCount": 20 } } }
```

or the environment, which wins: `UBIQUITOUS_MODULE_<MODULE>_<SETTING>`, for example
`UBIQUITOUS_MODULE_PLAYERBOTS_RANDOM_BOT_COUNT=20`. Setting names ignore case and
underscores.

## Modules here

- [`playerbots`](playerbots/README.md): characters played by the server.
