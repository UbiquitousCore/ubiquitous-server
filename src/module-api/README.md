# Ubiquitous.Modules

The contract between the server and optional modules. A module is a project
in `modules/<name>/` with an `IServerModule`; the server builds and loads
every one it finds there. See `modules/README.md`.
