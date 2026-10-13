# Placed patrol routes

One JSON file per map. A route's `id` is the patrol route id in the spawn
table's patrol column, and spawns naming it walk it; every other patrol walks a
route generated around its spawn.

```json
{
  "mapId": 13,
  "routes": [
    {
      "id": 4541,
      "stops": [
        {"x": 0, "y": 0, "z": 0, "pauseSeconds": 6},
        {"x": 10, "y": 0, "z": 0, "pauseSeconds": 0}
      ]
    }
  ]
}
```

Record routes with the patrol editor in `ubiquitous-client` (admin panel, "Patrol
editor"): pick a walker to go to it, stand where it stops, in a video of the
live game or on a live server, and press "Stop here". Save sends the file to
the server (`patrol.save`, admins only), which writes it here and walks its
routes at once. A file can also be uploaded from disk and saved the same
way.
