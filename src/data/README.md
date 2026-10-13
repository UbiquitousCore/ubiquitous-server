# Ubiquitous.Data

The normalized data contract game code reads: the `*Definition` records in
`Definitions.cs`, plus `DataManifest` and `DataCatalog`, which load and
cross-validate a `data/generated` package. Raw client formats are parsed only
in `tools/asset-extractor`. See `docs/ARCHITECTURE.md`. Tests:
`tests/data`.
