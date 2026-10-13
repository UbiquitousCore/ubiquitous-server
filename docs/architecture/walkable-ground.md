# Walkable ground

Terrain holds one height per point, so it cannot represent a floor above a
floor, and says nothing about walls. The asset tool bakes each map's collision
into a Recast/Detour navigation mesh (`DotRecast`) offline; `Ubiquitous.World` loads
it to answer ground height, straight-line walkability and paths for creatures.
Players are not checked against it.

## Bake inputs

Terrain from the package; collision models placed by the spawn tables
(placement → entry → display → model → `.phy`); and the map's own scenery from
its world archive (`just extract-world`), without which a town bakes as the
field it stands on. A house's interior is a separate unplaced model named
`<house>_inside`, placed with the house's transform. Scenery positions are
relative to the tile the file's header declares. Collision models come from the tables
(`just extract`); `just extract-collision <build>` adds those a client build's
`model_phy.bin`, `tables.bin` or `res.bin` ship, keeping any already there, so
a later build fills models the map's own build lacks. A model with no `.phy` of its
own takes the one shipped under its base name: `<model>_s` uses `<model>.phy`,
and a doubled underscore reads as one (`CollisionModelLibrary`).

## Parameters (`NavigationBaker`)

| | Value | Why |
|---|---|---|
| Cell size | 0.5 | Terrain has one height per unit. |
| Cell height | 0.1 | |
| Body radius | 0.4 | The browser client moves the player with the same value. |
| Step height | 0.6 | Also the height a body walks straight over; raising it makes crates and gravestones walk-through. |
| Max slope | 60° | The browser client stops climbing at the same angle. |
| Headroom | 2.0 | |

The bake keeps a detail mesh: without it a polygon is flat between its
corners and cuts through stairs and banks.

## Stairs

Stair collision is a few slabs about a unit apart, taller than the step
height, so each flight bakes as islands. Instead of raising the step height,
the bake hangs an off-mesh link from foot to head of each flight.

- Flights are recognised by model name; shape-based detection also matched
  trees, rocks and boats.
- A link's foot is placed one step below the first tread, or it attaches to the
  tread instead of the floor.

## Water

The bake reads each terrain chunk's water from the map's world archive. Ground
lying more than `NavigationAreas.SwimDepth` (1.25) under a chunk's surface is
painted the swim area: still walkable, but costing `NavigationAreas.SwimCost`
(4) times as much to route across, so creatures and bots go round water unless
the way round is long. The squares of water are stored in the `.nav` file
(format 3) so the server knows the surface; a format 2 file reads as a map with
no water. Detour costs a route between polygon edge midpoints, so where the way
round is only a little longer than a swim, a route may still swim.

## Which floor a body is on

- **Under before over**: the highest floor at or below the body wins; only
  with nothing beneath is it lifted to the floor above. Nearest-first carried
  creatures up a storey.
- **Reach**: the bake pulls ground back from walls, so queries search nearby
  rather than answering "nowhere" for a body beside a wall.
- A partial path is returned rather than refused, and an empty path means the
  body is already as close as the ground allows. With no path at all a chase
  falls back to a straight line at the chaser's own height.
- A map with no bake answers null/false and callers behave as before.

## The file

`<package>/navigation/<mapId>.nav`: magic `UBQWALK`, a format version, map
id, the five bake parameters, the mesh's length, the squares of water (count,
then min x, min z, size and surface for each), then the deflated Detour mesh. Any other format
version is refused, since a misread mesh puts bodies inside walls.

## Corrections

`navigation/<mapId>.overrides.json`, read by the bake (not at load), so
re-baking keeps them:

```json
{
  "mapId": 13,
  "edits": [
    { "kind": "unwalkable", "minX": 10, "maxX": 14, "minZ": 2, "maxZ": 6 },
    { "kind": "walkable", "minX": 20, "maxX": 21, "minZ": 4, "maxZ": 6, "floorY": 12.5 }
  ]
}
```

`unwalkable` paints ground out before regions grow (omit `minY`/`maxY` for
every storey); `walkable` adds a flat floor as collision. Corrections are
boxes because polygons do not exist until the bake has run. A malformed file
fails the bake.

## Inspecting a bake

- `just bake` prints per map how many spawn-table creatures stand on baked
  ground; this checks the whole chain against placements nobody here chose.
- `just nav-probe <map> x,y,z` re-bakes one tile and prints which stage
  removed the ground at a point (`collision`, `slope`, `ledges`, `headroom`,
  `body radius`, `islands`), then how much ground around it is reachable.
  Islands of one or two polygons are prop tops; islands of hundreds are a torn
  floor.
- In `ubiquitous-client`, `ubiquitous.nav()` draws the polygons around an operator
  (`navigation.request`, operators only).

Tests bake their own synthetic collision; none needs client assets.

## Known gaps

- Player movement is not checked against walkable ground.
- Off-mesh links exist only for stairs.
- Some maps lack floor collision at walking height (the prison bakes into 388
  islands); the cause is in the extract, not the bake.
