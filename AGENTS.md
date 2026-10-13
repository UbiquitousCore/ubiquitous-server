# Ubiquitous Server Agent Contract

This repository is an independent implementation. Every value it uses is a
game fact a player can observe, comes from the operator's own client data
files, or is this project's own design choice. No other server's or client's
source code is copied. CypherCore, an open-source World of Warcraft
emulator, is the structural model for the code layout and the source of some
design choices (recorded in `docs/PROVENANCE.md`).

## Sources and hygiene

- Take values only from what a player can observe in play, from the
  operator's own client files (`tables.bin`, terrain tiles), or from what the
  `ubiquitous-client` client needs on the wire. Where none of those says, follow
  CypherCore's behaviour and record it in `docs/PROVENANCE.md`, or make a
  design choice of your own and say so.
- Describe behaviour in your own words, then pin it with synthetic tests or
  tests that use user-supplied local data.
- Never commit client assets, extracted tables, packet captures containing
  game data, generated datasets, credentials, or absolute local paths.
- Runtime data paths, connection strings, and development credentials come
  from configuration or environment variables. Generated data belongs in
  ignored local directories such as `data/generated/` and `data/cache/`.
- The server sends identity, rules, state, and events. It does not send
  models, textures, animations, or other presentation assets.
- Do not say where a value came from unless it is true, and never describe
  code as a copy of, or a match for, another implementation.

### What names to use

- **This server's own WebSocket message names** — `attack.meleeStart`,
  `player.expGained`, `entity.spawned`. The shared vocabulary between this
  server and `ubiquitous-client`.
- **Client data table names** — `creature_proto`, `spell_effect`,
  `creature_machant_sell_program` (sic): the file names an operator finds in
  their own `tables.bin`. Columns have no names in those files; refer to a
  column by its index and what it holds.
- When a game concept needs a name here — a field, an enum value, an error
  code — give it a **CypherCore-style C# name**: PascalCase, dotted
  (e.g. `AttributeIds.HealingPower`, `ItemErrors.InventoryFull`,
  `InteractionRange.Max`).

### Before committing

Check the staged diff for client data, credentials, absolute paths and
provenance claims, and run the build and tests (see "Definition of done").

## Dependency boundaries

The intended dependency direction, using the physical project names, is:

```text
common -> data -> world -> game -> scripts -> server
common -> data -> persistence -> server
game -> module-api -> modules/* -> server
```

The C# namespaces retain the `Ubiquitous.*` prefix; physical folders, project files,
and solution display names use the shorter names above.

- `Ubiquitous.Common` contains generic primitives and infrastructure contracts.
- `Ubiquitous.World` owns authoritative map, visibility, entity, and movement state.
- `Ubiquitous.Game` owns gameplay/session-facing orchestration and canonical commands
  and events.
- `Ubiquitous.Data` exposes normalized server data; raw client formats belong only in
  the separate data-ingestion boundary.
- `Ubiquitous.Persistence` implements replaceable runtime/data repositories.
- `Ubiquitous.Scripts` holds per-creature and per-dungeon behaviour as classes the
  composition root binds to content. It answers questions the engine asks and
  never acts on its own; nothing below it may reference it. See
  `docs/ARCHITECTURE.md`.
- `Ubiquitous.Protocol.Web` is an adapter, not game logic.
- `Ubiquitous.Modules` (`src/module-api`) is what an optional module in
  `modules/<name>/` may see of the server. A module never references the
  server, persistence or another module; see `modules/README.md`.
- `Ubiquitous.Server` is the composition root, host, lifecycle, and listener owner.

`Ubiquitous.Game` and `Ubiquitous.World` must not reference WebSocket transport,
ASP.NET transport types, database implementations, raw client formats, or
presentation assets. Transport adapters decode into canonical commands and
encode canonical events. `ubiquitous-client` connects directly to the server's
WebSocket endpoint.

## Comments and documentation

Prefer, in order: self-explanatory code, a test, a one-line comment saying
*why*, a short doc section. Never comment what the code does, and never record
implementation history or unverified recollection as specification; an
unverified value gets a one-line `// TODO: Verify ...` beside it. No XML doc
comments.

Where a value comes from belongs in `docs/PROVENANCE.md`. `docs/BEHAVIOUR.md`
holds only client-visible rules that the code and tests cannot make obvious.

## A test must reach the code the server runs

If nothing but a test calls a member, the test is not testing the server. It
passes, it reads like cover, and the behaviour it describes never happens.

Three things tend to hide behind one:

- **A convenience overload.** The test calls a wrapper; the session calls
  what it wraps, which stays untested.
- **A rule that was never wired in.** A predicate is written and tested, and
  the production path computes its own version instead.
- **A reset for the tests.** Static state that outlives one test needs
  putting back, and the reset belongs in the test project, not beside the
  thing it resets.

Find them by intersecting three sets: declared in `src/` or `tools/`, named in
no other file under those, and named in a file under `tests/`. Then, for each:

| What it is | What to do |
|---|---|
| A wrapper over the call production makes | Delete it; the test asks the production call |
| A rule nothing wired in | Wire it, with a test at the call that decides |
| A constant derived from one production evaluates | Delete it; the test evaluates the same function |
| An observation impossible from outside | Keep it, and name it so it says so |

That last row is the only legitimate one, and it has to earn itself: a cache
hit and a map's private belief about a body cannot be seen any other way.
"For diagnostics and for the tests" does not count when nothing diagnostic
reads it.

## When a file feels too large, take the decision out, not the code

A cluster that looks like a class is usually one testable rule wrapped in
plumbing. Move the rule somewhere it can be answered without a session, a
socket or a map, and leave the plumbing where it is.

A message family that only needs the player and the session's replies becomes
a handler in `src/server/Handlers/`: it takes `ISessionPlayer` (character,
unit id, world, hub and a few narrow questions), `SessionOutbox`, and the
services it owns. Past about four collaborators you are inventing an interface
that carries the session around instead; model the shared state first
(`PlayerBelongings` is the example) and hand that over.

## Task workflow

For every task:

1. Inspect the current code, roadmap, client contract, and the observable
   behaviour before changing architecture.
2. Note any open question in `TODO.md`.
3. Write a focused test or fixture using synthetic data where possible.
4. Implement the smallest independent change that makes the test pass.
5. Run focused tests, `dotnet build`, and `dotnet test`.
6. Review the diff for unrelated changes, local paths, client data,
   credentials, and dependency direction.
7. Update `TODO.md`, and `docs/` only where a client-visible rule or a
   provenance fact changed.

Preserve unrelated worktree changes. Do not reset, revert, or rewrite changes
made by another contributor. Keep the tree buildable for the next task.

## Publishing

Never make this repository public. A public release is always prepared as a
fresh repository by the owner, following the release notes in the ignored
`local/RELEASE.md`. If that file is missing, stop and ask for it.

## Definition of done

A task is complete only when its acceptance criteria are met, focused tests and
the full build/test commands pass, provenance is recorded, and the diff
contains no proprietary data or credentials.

Required validation commands:

```powershell
dotnet build
dotnet test
```

Use the smallest relevant test project first, then run both commands above.
