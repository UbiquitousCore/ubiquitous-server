# Contributing to Ubiquitous Server

[AGENTS.md](AGENTS.md) is the project contract: sources, naming, dependency
boundaries, comments, workflow and definition of done. Setup, data import,
Docker and test conventions are in
[docs/development/setup.md](docs/development/setup.md).

## Repository layout

```text
docs/     behaviour, provenance, readings, architecture, development
src/      one project per subsystem
tests/    one test project per subsystem, plus behavior/ and integration/
tools/    asset-extractor (client data import and walkable-ground bake)
content/  project-authored JSON content (placed patrol routes)
scripts/  setup, test and hygiene helpers
data/     generated/ and cache/, both git-ignored
```

One behaviour per pull request; `dotnet build` and `dotnet test` must pass,
and `scripts/check-no-assets.py` runs in CI.
