# Asset extractor

The only place raw client formats are read. It turns the operator's own
client files into a normalized package under `data/generated/` and bakes
walkable ground. Input paths come from arguments and are never committed.
The `justfile` wraps every command.

| Command | Does |
|---|---|
| `--inspect <tables.bin>` | list every table's rows, columns and type string |
| `--extract <tables.bin> --out <dir>` | unpack the archive to plain files |
| `--extract-world <build> --out <dir>` | copy the per-map world archives beside the tables |
| `--import <tables.bin> --out <dir>` | build a package straight from the archive |
| `--import-from-dir <dir> --out <dir> [--world-archive <bin>]...` | build a package from unpacked tables |
| `--bake <package> [--tables-dir] [--models] [--world-dir] [--map]` | bake walkable ground |
| `--nav-probe <package> --map <id> --at x,y,z [--radius]` | explain the bake at one point |
| `--check <package>` | validate a package |
| `--synthetic --out <dir>` | write a tiny synthetic package |

See `docs/architecture/walkable-ground.md` for the bake.
