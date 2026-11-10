# Asset extractor

The only place raw client formats are read. It turns the operator's own
client files into a normalized package under `data/generated/` and bakes
walkable ground. Input paths come from arguments and are never committed.
Run it with `dotnet run --project tools/asset-extractor -- <command>`.

| Command | Does |
|---|---|
| `--ingest <build-or-dir> [--data <dir>]` | the whole import: every step below, installed into `<dir>/generated` keeping saves |
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

Columns have no names in the files, only a type string per table. Each
table the import reads has one `ColumnLayout` (`*Columns.cs`, listed in
`TableLayouts.cs`): its columns by name at their place in the 1.1.4 shape,
and one `LayoutRevision` per other known shape saying which reference
columns it lacks and where it adds its own. A mapper takes the typed view
(`layout.Resolve(document).As<ItemTableColumns>()`); a column the build
does not have reads as absent, and the import notes it.

| Build | Imports | Left out |
|---|---|---|
| 1.0.0, 1.0.1 | yes | teleport stops, travel routes, festivals, leash and notice ranges |
| 1.0.3 | yes | festivals, leash and notice ranges |
| 1.1.0, 1.1.1 | yes | notice range |
| 1.1.4 | yes | nothing: the reference |
| 2.2.0, 2.5.0 | yes | creature loot (dungeon rewards are imported, not yet awarded) |
| 4.2.21 | yes | creature loot; 68 items with an out-of-range subclass |

The notes are written into `manifest.json` (`ImportNotes`) and the package
carries `fingerprint.json` beside it.

### Adding a build

1. `--fingerprint <tables.bin> --out fingerprints/<build>.json`.
2. `--import` it. Every table whose shape no layout knows is listed.
3. For each, line its rows up with a known build's by key, find which
   columns moved, and add a `LayoutRevision`. `ColumnLayoutTests` checks the
   edits account for the format, `KnownBuildsTests` that every captured
   build is fully known.
4. Record what moved in `docs/PROVENANCE.md`.

A format no revision names still imports where it can: columns after a
known shape are ignored, and otherwise each column is kept only if its
type still fits, with a note for every one dropped.
