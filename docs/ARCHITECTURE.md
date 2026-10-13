# Architecture

Project graph and boundaries are in `AGENTS.md` and enforced by
`tests/behavior/ArchitectureBoundaryTests.cs`. `src/server/Hosting/` is the
composition root.

## Data

Raw client formats (`.tbl`, GBMP archives, terrain, collision) are read only
by `tools/asset-extractor`, with column layouts pinned to client build 1.1.4.
It writes a package to `data/generated/` (`manifest.json` plus one JSON file
per dataset); game code reads only the `Ubiquitous.Data` records.

- No package: the server runs on built-in development content.
- A dataset the manifest does not list falls back to development content with
  a warning. A listed dataset that is missing or invalid stops the server
  (exit code 1), so a broken package never runs half on development content.
- A package from another build only warns. Columns move between builds
  (`map_info`'s name is column 9 in 1.1.4 and 12 in 2.2.0), so check a table
  against the build it came from.

Project-authored content that no client file carries lives in `content/`.

## The world tick

One loop in `WebSocketGateway`, every 400 ms: every live map ticks first,
including dungeon copies nobody is in, then every session. There is no
per-map thread.

- The map tick runs what must finish with nobody watching: a creature walking
  home after a fight.
- A session's pump runs everything already gated on a player being near
  (`TileGrid`): wander, patrol, escort, noticing, chasing, swinging. Moving
  these to the map tick would simulate for nobody.
- The map records leg starts and ends in a numbered movement log; each session
  forwards what its cursor has not seen. A new session starts at the end of the
  log, because a spawn already carries where the body is headed.

## Sessions and delivery

Everything that changes a session or numbers its outgoing messages runs
inside that session's gate. `PlaySessionHub` never sends to another session
directly: it queues the event in that session's inbox, which is drained inside
the gate, so each client sees strictly increasing `sequence` numbers in queue
order. Who receives an event is decided from its type and the ids it names
(`OutboundEvent`, `EventAudience`), never by searching serialized text.

Each connection has a 1024-message outbound queue and its own writer, so a
client that stops reading cannot stall the tick; when the queue fills the
connection is closed.

## Scripts

`Ubiquitous.Scripts` answers questions the engine asks (`GivesUp`, `Assists`,
`Progress`, `Act`) and never acts: it returns a description, and
`PlaySession.Scripts.cs` carries it out. One script instance is shared by every
creature it is bound to, so a script holds no state; a fight remembers things
through marks the engine hands back each turn. `ScriptRegistry` binds content
to scripts by name, because game cannot reference the scripts project.

## Modules

`modules/<name>/` holds optional features built into the server; see
`modules/README.md`. `server.csproj` references every project there and names
each in a `BuiltWithModuleAttribute`, which `ServerModules.BuiltIn` loads.
Modules see the server only through `src/module-api`: settings, read-only
content, a turn after each world tick (`WorldTick`), and `LocalConnection`, a
client with no socket. A local connection goes through the same session,
gate, delivery order and rate limit as a WebSocket, so a module can do nothing
a remote client could not. Module accounts (`ModuleAccounts`) live only in
memory with a secret drawn at startup and never shadow a real account.

## The client's copy of the world

The client builds its world from the map's own data; the server cannot remove
anything from it by not sending it. Anything the server wants gone must be
said: `world.hiddenObjects` (sent on entry even when empty, so absence is never
ambiguous) and `dungeon.barrierOpened`. A client must reapply both after the
zone load that follows entry.

## Precision

Coordinates are float32 end to end. Do not widen to double, or reorder
arithmetic, where the result crosses the wire or decides something a client
can see.
