# Asset extractor

The only place raw client formats are read. It turns the operator's own
client files into a normalized package under `data/generated/` and bakes
walkable ground. Input paths come from arguments and are never committed.
The `justfile` wraps every command.

| Command | Does |
|---|---|
| `--inspect <tables.bin>` | list every table's rows, columns and type string |
| `--fingerprint <archive\|dir> [--out <file>] [--golden <file>]` | capture the shape of every table as JSON; with `--golden`, fail on format drift |
| `--extract <tables.bin> --out <dir>` | unpack the archive to plain files |
| `--extract-world <build> --out <dir>` | copy the per-map world archives beside the tables |
| `--import <tables.bin> --out <dir>` | build a package straight from the archive |
| `--import-from-dir <dir> --out <dir> [--world-archive <bin>]...` | build a package from unpacked tables |
| `--bake <package> [--tables-dir] [--models] [--world-dir] [--map]` | bake walkable ground |
| `--nav-probe <package> --map <id> --at x,y,z [--radius]` | explain the bake at one point |
| `--check <package>` | validate a package |
| `--synthetic --out <dir>` | write a tiny synthetic package |

See `docs/architecture/walkable-ground.md` for the bake.

## Column layouts

Each gameplay table's columns are resolved by shape (`*Columns.cs`
`Resolve`, `TableSchemas.cs`): an exact or append-only 1.1.4 format match
takes the pinned columns, anything else is located by shape or fails
with the table, column and both formats named. Spell effect slots are
discovered by value (`TableShape.DiscoverEffectSlots`); power cost and
group mask on an unknown layout are left unknown rather than misread.
The import log names the identified build and the slots used, and the
package carries `fingerprint.json` beside `manifest.json`.

Builds 1.1.0 and 1.1.1 import end to end (goldens in
`fingerprints/`, explicit overrides in `ItemColumns.cs` and
`SpellNewColumns.cs`). Their one gap is vendor stock: their
`creature_proto` column 55 is not the vendor-list string, so vendor
wiring is skipped with a log note until their seller tables are mapped.

Build 2.2.0 also imports end to end (golden in `fingerprints/`, talent
layout in `TalentColumns.cs`, replacement loot in
`DungeonRewardTableMapper.cs` → `dungeonRewards.json`). Its gaps:
creatures drop nothing (dungeon rewards are imported but not yet awarded
by game logic), and the new sticker, daily-bonus and Turkish string
tables are not read.

Builds 1.0.3 and 4.2.21 are fingerprinted only (goldens in
`fingerprints/`, nothing wired). A trial import shows 1.0.3 close:
it reads as an unknown build with small drifts (five tables) and
imports with only rows dropped (43 spawns, 11 zones, 150 quests)
plus skipped vendor stock. 4.2.21 needs real resolver work: every
entry in its `tables.bin` is LZMA-compressed (method 14, now
decodable via `LzmaMethod.cs`), its load-bearing formats extend
2.2.0's by appended columns, and the trial import stops loud on
`items.tbl` row 11663 (no usable item subclass).
