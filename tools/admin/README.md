# Admin page

A web page for a host: accounts, GM, characters (name, level, gold, bag,
giving items) and the `.env` settings.
Docker: it starts with the stack, at <http://localhost:8790>. From a
clone, with file saves: `dotnet run --project tools/admin/web`. Only a GM
account signs in.

It is kept apart from the server so it can move to a repository of its own:

| Project | Holds | May reference |
|---|---|---|
| `core` | the rules (who signs in, which settings, what a valid value is) and the ports | nothing |
| `stores` | the ports over this server's persistence, game rules and data package, `.env`, and its `/health`, `/online` and `/restart` | `core`, `src/persistence`, `src/game` |
| `web` | HTTP, the sign-in cookie, `wwwroot/index.html` | `core`, `stores` |

Moving it out means replacing `stores` with one that talks to the server some
other way; `core` and `web` do not change. The server never references any of
the three.

Settings: `UBIQUITOUS_ADMIN_URLS` (default `http://127.0.0.1:8790`),
`UBIQUITOUS_ADMIN_SETTINGS_FILE` (`.env`), `UBIQUITOUS_ADMIN_SERVER_URL`
(`http://127.0.0.1:8781/`), `UBIQUITOUS_ADMIN_TOKEN_FILE`, and the server's own
`UBIQUITOUS_POSTGRES_CONNECTION_STRING`, `UBIQUITOUS_POSTGRES_PASSWORD_FILE`,
`UBIQUITOUS_STATE_DIRECTORY` and `UBIQUITOUS_DATA_DIRECTORY` (for item names). With file saves, the game server keeps
characters in memory: stop it before deleting one here.

Tests: `tests/admin`.
