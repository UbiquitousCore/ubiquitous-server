# Ubiquitous.Persistence

Implementations of the store interfaces: in-memory (tests and development),
file-backed JSON (default) and Postgres for accounts, characters, inventory
and quest logs when `UBIQUITOUS_POSTGRES_CONNECTION_STRING` is set. `PostgresSchema`
applies versioned migrations once each. Guild, mail, auction, friend and
report state is always file-backed.

Characters are saved on disconnect and every five minutes (`PlaySession.SaveProgressAsync`, and one transaction through `IProgressStore` with Postgres). A null
`CharacterRecord.Currency` means never saved, so a new character starts with
`GameContent.StartingCurrency`.

Tests: `tests/integration/PersistenceStoreTests.cs`, and
`PostgresStoresTests.cs`, which skips unless
`UBIQUITOUS_TEST_POSTGRES_CONNECTION_STRING` is set.
