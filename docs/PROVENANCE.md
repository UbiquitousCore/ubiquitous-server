# Provenance of format and wire facts

Every value this server uses is a game fact a player can observe, comes from
the operator's own client files or from what the `ubiquitous-client` client needs, or
is this project's own design choice.
This page lists the file-format and wire facts the server depends on, where
each one is read, and the test that pins it. Checked against the operator's
own copy of client build 1.1.4.

## File formats (read from the operator's own client files)

| Fact | Where the server reads it | Pinned by |
|---|---|---|
| Table files start with a 44-byte header: ASCII magic `FTABLE`, then little-endian 32-bit fields at offsets 12-43 giving row count, column count, and the offsets of the row-id index, the column type string, the records and the string pool, plus record size and pool length. | Each `*.tbl` in the operator's `tables.bin` (`tools/asset-extractor/FTable.cs`) | `tests/tools/TableLayoutTests.cs` |
| A table's column type string gives one character per column; each character has a fixed width (`k`/`u` u32, `i` i32, `f` f32, `c` i8, `h` i16, `s` string reference, other u32). | The type string stored in each table's own header | `tests/tools/FTableRowTests.cs` |
| A string cell is an offset plus a length into the table's string pool. | The table's own string pool | `tests/tools/FTableRowTests.cs`, `tests/tools/StringTableMapperTests.cs` |
| Per-table column type strings used in tests (for example the 167-column quest table and the 113-column item table). Each is the type string stored in that table's header in the operator's `tables.bin`. | The operator's `tables.bin` | `tests/tools/*ColumnsTests.cs`, `tests/tools/PackageImporterTests.cs` |
| Terrain tiles (`world/<map>/terrain/<x>_<z>.trn` inside a map's world archive): ASCII magic `ATIL`, a 24-byte header, 64 chunk headers of 12 bytes, then 65 × 65 little-endian int16 heights in hundredths of a unit at offset 792. A tile whose every sample is -1000 is empty. | The operator's own map archives (`tools/asset-extractor/TerrainTileReader.cs`) | `tests/tools/TerrainTileReaderTests.cs` |
| A terrain tile covers 64 world units; a map's bounds are the full extent of the tiles present. | Tile names in the operator's own map archives (`tools/asset-extractor/WorldBoundsReader.cs`) | `tests/tools/WorldBoundsReaderTests.cs` |
| Scenery tile files (per tile in a map's world archive): ASCII magic `AITM`, a version, the tile's x and z, a placement count and a resource count, then one fixed-size record per placement (resource index, position, rotation quaternion, per-axis scale; 56 bytes at version 3, 64 at version 4), then the resource name table. | The operator's own map archives (`tools/asset-extractor/WorldItemReader.cs`) | `tests/tools/WorldItemReaderTests.cs` |
| Collision mesh files (`.phy`): a shape count, then per shape a 36-byte header; kind 7 is a triangle mesh (position, half-size, vertex and triangle counts, vertices, triangles), any other kind is a box (yaw, centre, half-size). | The operator's own extract (`tools/asset-extractor/CollisionMeshReader.cs`) | `tests/tools/CollisionMeshReaderTests.cs` |
| Conversation pages, an own reading of the user's files: `chat_menu` is keyed by column 0; column 1 names a `String_Menu` string, columns 2-7 are six choice scripts and 8-13 the pages under them. The string's markup opens a tag with byte 0x12 and closes it with 0x13. `link_menu=K` is a choice; K from 1 to 6 opens sub-page column K. `teleport=K,topzone=Z,x,y,z,o,moneytype,moneycount` is a teleport sold by the choice carrying `script=K`, arriving in zone Z at (x, y, z) facing o; moneytype 0 is the same currency number vendor tabs and storage upgrades price in, counted here as copper, and a tag with no price is free. `creature_proto` column 56 names the page a creature greets with. | `tools/asset-extractor/GossipMenuTableMapper.cs`, `tools/asset-extractor/PackageImporter.cs` | `tests/tools/PackageImporterTests.cs`, `tests/game/GossipServiceTests.cs`, `tests/integration/GossipTeleportTests.cs` |
| Chat-menu script 562 sends the player, free, to `teleport_spot` 1. Script 562 is the choice script of the Orc Teleporter's page and of Akhan's page, both of which offer the Town of Silence; `teleport_spot` 1 is the row the user's tables label "HF-Silent town". The files do not say what a script does, so treating 562 as this teleport is this project's design choice. | `src/game/GossipService.cs` (`GossipScripts`) | `tests/game/GossipServiceTests.cs`, `tests/integration/GossipTeleportTests.cs` |
| Table and pack archives (`tables.bin` and the map `.bin` files) are ZIP archives whose local headers carry the four bytes `GBMP` where ZIP puts `PK\x03\x04`. | The operator's own archives (`tools/asset-extractor/TableArchive.cs`, `PackedArchive.cs`) | `tests/tools/TableArchiveReaderTests.cs`, `tests/tools/PackedArchiveTests.cs` |
| Worn equipment slots 0-15; bag and bank slots start at 16. Slots 0-12 are this project's own order, head to toe: head, neck, shoulders, hands, chest, shirt, back, legs, feet, then two rings and two trinkets. The weapons are 13 (main hand), 14 (off hand) and 15 (ranged) because the operator's spell tables name them by those numbers (`spell_new` column 12, the slot a spell needs, and the weapon slot of a weapon-hit effect in `spell_effect`). | This server's and the web client's shared numbering on the wire (`src/data/Definitions.cs`, `EquipmentSlots`) | `tests/data/EquipmentSlotsTests.cs` |
| An item's stat kind maps to a character attribute (for example kind 11 Strength, 13 Stamina, 14 Wisdom). | The stat columns of the operator's `items` table (`src/data/Definitions.cs`, `ItemStatKinds`) | `tests/tools/ItemColumnsTests.cs` |
| Spell effect kinds and details (for example detail 23 a main-hand weapon hit, 28 a ranged weapon hit, 11 a chill). | The operator's `spell_effect` table (`tools/asset-extractor/SpellEffectColumns.cs`) | `tests/tools/SpellEffectColumnsTests.cs` |
| A direct-damage spell that also slows carries the slow (aura detail 7) behind its damage, an own reading of the user's files: in the 1.1.4 `spell_new` table 87 such spells hold it in effect slot column 34 and one in column 35. | `tools/asset-extractor/PackageImporter.cs` | `tests/tools/PackageImporterTests.cs` |
| Scenery placed as `<model>_s` collides as `<model>.phy`, an own reading of the user's files: in the 1.1.4 extract 38 models are placed only under an `_s` name whose collision ships under the base name beside it (town stone walls, a tower, a stable, a church), and one model is placed as `orcbuilding__arena01` while its collision is `orcbuilding_arena01.phy`. | `tools/asset-extractor/CollisionModelLibrary.cs` | `tests/tools/CollisionModelLibraryTests.cs` |
| A collision block whose flags word (the first field after its type) has bit 0 or 1 set is a volume a body walks into, such as the box filling an inn that marks a player as inside, and is left out of the walkable ground: the reading and choice `ubiquitous-client` documents in `client/docs/formats/phy.md`. Baked solid, such a box walled the floor of the building Edgar (creature 745, map 1) stands in off from the street. | `tools/asset-extractor/CollisionMeshReader.cs` | `tests/tools/CollisionMeshReaderTests.cs` |
| Collision models (`.phy`) are looked up in a build's `model_phy.bin`, then `tables.bin`, then `res.bin`, ignoring case: the order `ubiquitous-client` reads them in (`PHY_PACKS`). The 1.1.4 build ships them in `tables.bin` only; 2.2.0 adds `model_phy.bin`, whose models fill those 1.1.4 lacks (dark elf houses, Mendel walls and trees). Letting a later build fill only what the map's own build lacks is this project's design choice. | `tools/asset-extractor/CollisionPacks.cs` | `tests/tools/CollisionPacksTests.cs` |
| A playable body's looks, read as `ubiquitous-client` reads them: `creaturedisplayinfo` column 1 names the `creaturemodeldata` row, whose column 2 is the body's model; with the file name's one-letter suffix dropped (`human_m_h` draws as `human_m`), each `<stem>_body_NN`, `<stem>_body_NN_face_NN` and `<stem>_hair_a_NN` image in `res.bin` or `res_t.bin` is a skin, face or hair colour, and each `hair_<n>` mesh in the model a hair style. | `tools/asset-extractor/PlayerLookReader.cs` | `tests/tools/PlayerLookReaderTests.cs`, `tests/playerbots/PlayerbotWorldTests.cs` |
| A terrain tile's 12-byte chunk headers carry water: bit 16 of the first 32-bit field says the chunk holds water, and a signed 16-bit value at byte 4 is its surface in hundredths of a unit; a surface below the chunk's lowest ground sample is a placeholder that is not drawn. Read from the operator's own tiles, as `ubiquitous-client` reads them. A body swims once the bed is 1.25 below the surface, at half running speed, as in `ubiquitous-client`. | `tools/asset-extractor/TerrainTileReader.cs` (`ReadWater`), `src/data/NavigationPackage.cs` (`NavigationAreas`) | `tests/tools/TerrainTileReaderTests.cs`, `tests/integration/WalkableGroundTests.cs`, `tests/playerbots/BotWalkingTests.cs` |
| Static object type 8 is a mailbox and type 9 a storage chest. | The operator's static-object tables (`src/data/Definitions.cs`) | `tests/tools/StaticObjectTableMapperTests.cs` |
| A party holds four: the team frames show three other members and a summon stone's window three names. | `PartyService.MaxPartySize` | `tests/game/PartyServiceTests.cs` |
| Static object type 105 is a summon stone, an own reading of the user's files: its four entries (210, 216, 217, 283) place the `callstone` models, five times, beside dungeon entrances; `ubiquitous-client` gives the type the `ui_head_summon` head icon. A stone calls another member of the summoner's party, who is asked and, on a yes, moved to where the summoner stands: the 1.1.4 interface's own summon window lists names and its strings ask "%s summons you to %s." and answer "Summon Refused". Arriving beside the summoner rather than at a fixed point is this project's design choice. A stone asks the summoned player to be at least the level of the dungeon whose way out is nearest it (within 150 units, own choice), the normal copy's where a legend copy shares the way out: the owner's rule, so a stone cannot carry players below a dungeon's level into it. The 1.1.4 strings name that refusal on both sides (1495, 1496). | `src/server/Handlers/SummonHandler.cs`, `src/game/SummonRules.cs`, `StaticObjectTypes.SummonStone` | `tests/integration/SummonStoneTests.cs`, `tests/game/SummonRulesTests.cs` |
| `map_info` column 3 holds 4 on every dungeon row (the seven dungeon copies in 1.1.4) and column 4 the level a dungeon asks for (the prison 10, Swamp Eye 20 and its legend copy 60, Eristar Temple 30, Sailen's lower city 40 and palace 45, Eidolon's Horizon 50); columns 5-8 are where a player is put out, beside that dungeon's summon stone. An own reading of the user's files. | `tools/asset-extractor/MapInfoColumns.cs`, `MapDefinition.MinLevel`, `MapDefinition.IsDungeon` | `tests/tools/MapInfoColumnsTests.cs` |
| Mining locations and ore nodes, an own reading of the user's files: `staticobject_spawns` entry 52 (model `mine_cross`, type 1) has 1,192 placements on six maps and no gather spell; type 1 entries 132, 167, 168, 198, 199 and 200 (models `ore_main01` to `ore_main06`) have no placements, name the gather spell in `staticobject_proto` column 12 (23, "Gather mine") and carry figures in columns 16 (120 or 180) and 17 (300, once 400) whose meaning is not settled and which are not used. Spell 23's cast time (`spell_new` column 16) is 3,500 ms, read into `spells.json` as `castTimeMs`. The six `Bronze Mineral`, `Marcasite`, `Primal Iron`, `Light Silver`, `Matted Gold Mineral` and `Magical Blood Mineral` items (5192-5195, 5197, 5198) are the ores in rising price order; pairing them with the six nodes in entry order is this project's reading, since no table links a node to an item. | `src/game/MiningRules.cs` (`MiningRules`, `MiningOres`) | `tests/game/MiningPoolTests.cs` |
| What the columns of the loot, `teleport_spot`, `t_bus_route` and vendor tables hold. | The operator's own tables (`tools/asset-extractor/*Columns.cs`, `PackageImporter.cs`) | `tests/tools/PackageImporterTests.cs` |
| `creature_spawns` column 14 names the `c_animationdata` row a placement stands playing, 0 for none, an own reading of the user's files: on 942 of the 1.1.4 table's 12,513 rows it is set, and the names it points at are standing poses and loops (`sit_wait`, `emote_work`, `sleep`, `emote_talk`, `kneel_hold`). Sent as `animationId` on `entity.spawned`. | `tools/asset-extractor/CreatureSpawnColumns.cs`, `PlaySession.Visibility.cs` | `tests/tools/CreatureSpawnColumnsTests.cs`, `tests/integration/VisibilityRegistrationTests.cs` |
| Bit 0x0004 of the quest flags column (14) marks a repeatable quest: it is set on 15 quests, 14 of whose own titles in the operator's `String_Quest` text say "(repeatable)". | `tools/asset-extractor/QuestTableMapper.cs`, `QuestDefinition.IsRepeatable` | `tests/tools/QuestTableMapperTests.cs` |
| The prison's inner door: static-object spawn row 2818 on map 14. The barrier and the last boss's appearance point are measured from it. | `GameContent.PrisonInnerDoor` | `tests/integration/GameContentTests.cs` |
| Talent trees: `spell_talent` column 0 the id, 1 the tier, 2 the column, 3 the tree, 4 the points spent in the tree that reaching it needs, 5 and 6 prerequisite talents, 8 the rank count, 9-13 one spell per rank, 14 the icon, 15 the `String_Spell` id of its name (the description is the next id). `character_class` columns 3-5 name the trees a class owns. Read against the operator's tables: every rank count matches its non-zero rank spells, every prerequisite on a class tree names a talent on the same tree, and the tier gate is five points a tier. Rows on trees no class owns (an older copy of each class tree, and the riding tree) are not imported. | `tools/asset-extractor/TalentColumns.cs`, `TalentTableMapper.cs` | `tests/tools/TalentColumnsTests.cs`, `tests/tools/PackageImporterTests.cs` |
| Emote ids and names. | The operator's animation table (`src/game/EmoteService.cs`) | `tests/game/EmoteServiceTests.cs` |
| `spell_effect` type 1 detail 3 is a shield that absorbs damage: `baseValue` the damage it takes in all, the effect parameter the percentage of each blow it takes. Own reading of the user's files: the row's own label names the absorb amount and is cut short before the parameter, which rises with rank from 40 to 60 on one line of the mage's shield and reads 100 on the other. | `tools/asset-extractor/SpellEffectColumns.cs`, `PackageImporter.cs`, `src/game/AbsorbEffectService.cs` | `tests/tools/PackageImporterTests.cs`, `tests/game/AbsorbEffectServiceTests.cs` |
| `spell_effect` column 4 names who an effect lands on, an id the `spell_affect_type` table names (1 the caster, 2 one enemy, 6 one friend, and so on). Own reading of the user's files: every eating, drinking, spell-learning and mount row carries 1, damage rows mostly 2, and the mage's shield rows 1, which is why a shield lands on its caster. | `tools/asset-extractor/SpellEffectColumns.cs`, `SpellEffectClassifier.cs`, `src/data/Definitions.cs` (`SpellEffectTargets`), `src/server/PlaySession.Spells.cs` | `tests/tools/SpellEffectClassifierTests.cs`, `tests/integration/SpellExtraEffectTests.cs`, `tests/integration/DuelSpellTests.cs` |
| `spell_new` columns 33-37 are a spell's five effect slots, each a `spell_effect` id or 0. Own reading of the user's files: every nonzero value in those five columns names a `spell_effect` row, and the mage's opening spell, whose text promises direct damage, damage over time and a burning condition, names exactly those three effects in columns 33, 34 and 35. | `tools/asset-extractor/PackageImporter.cs`, `SpellEffectClassifier.cs` | `tests/tools/PackageImporterTests.cs` |
| `spell_effect` columns 16 and 17 are nonzero on an effect that applies only while its target (16) or caster (17) is in some state. Own reading of the user's files: the ranged attack's two stun triggers carry caster values 8 and 9 where the spell text says they apply in sneak mode, and a strike's extra damage carries target value 13 where its text says a bruised target takes more. Which state each other number names is not settled, so such an effect is not applied. | `tools/asset-extractor/SpellEffectClassifier.cs` | `tests/tools/SpellEffectClassifierTests.cs` |
| `spell_effect` type 0 detail 6 gives power back: `baseValue` the amount, spread the most added on top, the effect parameter the kind (1 mana, 2 morale, 3 vigor). Read from the user's files: the row's own label. `spell_new` column 40 is the kind a spell spends: own reading of the user's files, every spell on the warrior's lines carries 2, the mage's and monk's 1, the ranger's 3. | `tools/asset-extractor/SpellEffectClassifier.cs`, `src/data/Definitions.cs` (`PowerKinds`), `src/game/SpellService.cs` | `tests/tools/SpellEffectClassifierTests.cs`, `tests/game/SpellServiceTests.cs`, `tests/integration/SpellExtraEffectTests.cs` |
| Power regen, morale gains and vigor losses (rates, floors, rest value, the in-combat halving of mana). Read in play from new characters; tables in `docs/POWER_READINGS.md`. The weapon swing time uses the server's one swing interval (our own choice), and a hit's damage is the amount after armour. | `src/game/SpellService.cs`, `src/server/PlaySession.Power.cs` | `tests/game/SpellServiceTests.cs`, `tests/integration/PowerInCombatTests.cs` |
| An aura effect row of subtype 17 (label `Avatar<miscValue:displayinfo>`) turns its wearer into the `creaturedisplayinfo` row in its argument column; cookies, wands and spirit transforms use it. Read from the user's files: the row's own label. Re-run ingest to put `auraAvatarDisplayId` into spells. | `tools/asset-extractor/SpellEffectClassifier.cs`, `src/game/AvatarEffectService.cs`, `src/server/PlaySession.Auras.cs` | `tests/tools/SpellEffectClassifierTests.cs`, `tests/integration/AvatarAuraTests.cs` |
| `spell_effect` type 0 detail 14 moves the caster to the spell's target, its parameter 1 where it is shown and 0 where it is not. Read from the user's files: the row's own label names it a teleport with that parameter; it leads Charge and Dash Attack (shown) and Shadow Blink (not shown), and follows the damage on Lightning Fall. Where the caster lands is not in the files. | `tools/asset-extractor/SpellEffectColumns.cs`, `SpellEffectClassifier.cs`, `src/server/PlaySession.Spells.cs` | `tests/tools/SpellEffectClassifierTests.cs`, `tests/integration/LeapToTargetTests.cs` |
| `items` column 25 is how many of an item one stack holds, and column 15 its item level. Own reading of the user's 1.1.4 files: column 25 is 1 or 0 on every weapon and armour row and 5, 10, 20, 99 or 100 on consumables, reagents and quest items, and column 15 is at or above column 16 (required level) in 2,330 of 2,353 rows that set both. 0 is read as one to a stack. | `tools/asset-extractor/ItemColumns.cs`, `ItemTableMapper.cs`, `src/game/InventoryService.cs` (`Sort`) | `tests/tools/ItemColumnsTests.cs`, `tests/game/InventorySortTests.cs` |
| `spell_new` column 6 is a spell's rank in its chain, 0 for a spell without ranks; a chain is the spells that share a name. Own reading of the user's 1.1.4 files: Fireball 5000, 5006, 5007, 5008 and 5009 hold 1 to 5, and Auto Attack (108) holds 0. The default spell bar, and any bar slot when a higher rank is learned, uses the highest rank the character knows. | `tools/asset-extractor/SpellNewColumns.cs`, `src/game/SpellRanks.cs` | `tests/tools/SpellNewColumnsTests.cs`, `tests/game/SpellRanksTests.cs` |
| `creature_proto` column 55 lists a vendor's sell lists (`creature_machant_sell_program`), one per shop tab, each followed by a comma (`114,115,116,117,`, `52,`); a few rows hold one bare number. Own reading of the user's 1.1.4 files: read this way, vendors with no stock fall from 26 to 4. | `tools/asset-extractor/SellListIds.cs`, `PackageImporter.cs` | `tests/tools/SellListIdsTests.cs`, `tests/tools/PackageImporterTests.cs` |
| `items` column 19 is a spell the user must already know. Own reading of the user's files: class skill books ask for rank I of their line, craft handbooks for the craft, riding-skill items for the race's riding permit and Bonds for that race's riding skill. Only using an item checks it. | `tools/asset-extractor/ItemColumns.cs`, `ItemTableMapper.cs`, `src/game/SpellLessons.cs` | `tests/tools/ItemColumnsTests.cs`, `tests/integration/MountTests.cs` |
| `spell_effect` type 0 detail 10 teaches the spell its parameter names; type 1 detail 50 seats the caster on the `mountprototype` row its parameter names, `baseValue` the percent chance to stay seated when struck; type 1 detail 52 is the mount's speed-up percent. Read from the user's files: each row's own label. Every `Bond` item's use spell teaches one mount spell, and each mount spell carries a 50 and a 52 row. | `tools/asset-extractor/SpellEffectColumns.cs`, `SpellEffectClassifier.cs`, `src/server/PlaySession.Mounts.cs` | `tests/tools/SpellEffectClassifierTests.cs`, `tests/integration/MountTests.cs` |
| The player who loses a duel begs, observed in play by the owner. The emote played is `beg`, chosen by its name. | `src/server/PlaySession.Duel.cs` | `tests/integration/DuelSpellTests.cs` |

Tables have no column names in these files. Code and docs refer to a column
by its index and what it holds.

## Ids and wire values (required by the web client)

| Fact | Where it is used | Pinned by |
|---|---|---|
| The named codes and value keys on the wire (outcomes, refusals, item results, loot state, conversation choice results, quest marks, unit values, hands, quest-share results). This project's own names, listed in `docs/architecture/wire-codes.md`. | `src/protocol/web/WireCodes.cs`, `src/data/ItemErrors.cs` | `tests/integration/SpellCastTests.cs`, `tests/integration/BagFullTests.cs`, `tests/integration/AuraStateFieldTests.cs`, `tests/integration/LevelingTests.cs` |
| Ranged auto-attack reach 25 units: the range of Auto Shoot (spell 58) in the operator's `spell_new` table, column 21. The projectile's spell visual 81: the row of the operator's `c_spellvisual` table named "Bow Arrow". Projectile speed 30: Rapid Shot's speed from `spell_new` (column 39), used for all shots. | `src/game/CombatService.cs` (`MaxRangedAttackDistance`, `RangedAttackSpellVisualId`) | `tests/game/CombatServiceTests.cs`, `tests/integration/RangedAttackTests.cs` |
| A spell's projectile speed is `spell_new` column 39, the same column as Rapid Shot's. Landing a spell after distance over that speed, measured at release, is this project's design choice, matching how ranged shots land. | `src/game/SpellFlightService.cs` | `tests/game/SpellFlightServiceTests.cs`, `tests/integration/SpellFlightTests.cs` |
| Attribute ids 0-43 (strength, stamina, armor, and so on). | `src/data/Definitions.cs` (`AttributeIds`); also the attribute column of the operator's `spell_effect` table | `tests/game/AttributeModifierServiceTests.cs`, `tests/tools/SpellEffectColumnsTests.cs` |
| `creature_ai` holds one row per faction type (rows 1-3: hostile, townsfolk, neutral), each with three values, one per faction type in the same order; 0 reads as hostile, 1 friendly, 2 neutral. Own reading of the user's files: with that reading every faction is friendly to itself, the neutral row is neutral to everything, the townsfolk row is hostile to the hostile faction, and the hostile row is hostile to the neutral faction and neutral to the townsfolk one. Only the hostile-to-neutral pairing is acted on. | `src/game/FactionStance.cs` (`AttacksCreatureOnSight`), `src/game/CreatureHunting.cs` | `tests/game/CreatureHuntingTests.cs`, `tests/integration/CreatureHuntTests.cs` |
| `creature_proto` column 1 is what kind of animal or being a creature is; 9 is a small animal. Own reading of the user's files: every row carrying 9 is a rabbit, frog, snake, squirrel, a small dragon, or one of a handful of unnamed marker rows. | `tools/asset-extractor/CreatureProtoColumns.cs`, `src/data/Definitions.cs` (`CreatureFamilies`) | `tests/tools/CreatureProtoColumnsTests.cs` |
| Faction types 0-2 (hostile, townsfolk, neutral); every player is townsfolk. | `src/data/Definitions.cs`; the faction column of the operator's `creature_proto` table | `tests/game/FactionStanceTests.cs`, `tests/integration/CreatureAggroTests.cs` |
| Class ids 1-4 and the class mask `1 << id`. | `src/data/Definitions.cs` (`PlayerClassIds`); the class-mask columns of the operator's item and quest tables | `tests/data/ItemClassMaskTests.cs`, `tests/integration/CharacterCreationTests.cs` |
| Inventory types 0-11, 13-15, 18 and 19 (where an item is worn), the values the column holds. | `src/data/EquipSlotRules.cs` (`InventoryTypes`); the inventory-type column of the operator's `items` table | `tests/game/EquipSlotRulesTests.cs`, `tests/game/InventoryEquipTests.cs` |
| NPC flag bits (gossip, vendor, teleporter, auctioneer, ...). Whether an NPC starts quests comes from `creature_quest_starter`, not a flag bit. | `src/data/Definitions.cs` (`NpcFlags`); the NPC-flag column of the operator's `creature_proto` table | `tests/tools/NpcOverlaysTests.cs`, `tests/game/GossipServiceTests.cs` |

Only one extra-flags bit is used (bit 7, the training dummy). Other bits, such
as bit 15, are set on some creatures in the operator's data and are not used.

## Values that match what a player sees in play

Values a player can see in play, stated in this project's own code. Stats per
level, base health and mana, XP to the next level, quest XP by level gap, and
each race and class's level-1 attributes, kit, spellbook and action bar were
read in the live game between 2026-10-03 and 2026-10-06; the readings are in
[GAMEPLAY_READINGS.md](GAMEPLAY_READINGS.md), and `PlayerStatCurvesTests`,
`LevelingServiceTests` and `StartingLoadoutsTests` pin them.

- Experience: the XP bar total per level, kill XP by level gap, quest XP by
  level gap (`src/game/LevelingService.cs`).
- Base health, power and attack power per class and level, and the
  Strength, Agility, Stamina and Wisdom each class gains per level
  (`src/game/PlayerStatCurves.cs`).
- What a new character starts with per race and class: worn kit, bag items,
  spellbook, filled action-bar buttons and level-1 attributes, named by client
  ids (`src/game/StartingLoadouts.cs`).
- Gold lost on death by level (`src/game/DeathPenaltyService.cs`).
- Fall damage (`src/game/FallDamage.cs`), as the game behaves in play: a fall
  pulls at 18 units a second squared; landings up to 18 units a second are
  harmless, damage grows evenly to all of maximum health at 30, only a fall
  that began with a jump counts, and landing in water is harmless (recorded
  in `docs/guides/gameplay-field-guide.md`). Charging the ordinary death
  penalty for a fatal fall is this project's choice.
- Boss abilities, their order and the health at which a fight changes phase
  (`src/scripts/Prison/`). How often each ability comes round is this
  project's own choice.
- Prison boss levels and health are the operator's `creature_proto` rows:
  Normal level 20, 20 and 21 with 10,000, 12,000 and 15,000 health; Legend
  level 60, 61 and 61 with 100,000, 120,000 and 150,000.
- One dungeon mode for all of a team's dungeons, and changing it resets every
  dungeon: the game's own team window has a single Normal/Legend choice on its
  Dungeon page and warns that changing the mode resets all progress in all
  dungeons (the operator's interface strings).
- Each prison creature's Legend counterpart (`GameContent.PrisonLegend`) is
  the `creature_proto` row of the same name at level 58-61; each boss spell's
  Legend counterpart is the spell row of the same name whose numbers are the
  larger (for example Reaping: 300 damage normal, 2,500 Legend).
- A talent's prerequisite must be at its full rank before the talent can be
  bought (`src/game/TalentBook.cs`). The data names the prerequisite, not
  how many of its ranks.
- Creature ranks Rare, Elite and Boss (`src/data/Definitions.cs`,
  `CreatureRank`): the target portrait wears a different frame for each
  than for an ordinary creature.
- A player standing at a travel stop can buy a fare whatever height the
  stop is recorded at: the distance to a stop is measured across the ground
  only (`src/game/InteractionRange.cs`, `WithinHorizontal`;
  `src/game/TravelService.cs`).
- The 15-minute reset of an emptied dungeon copy
  (`src/game/InstanceService.cs`).
- Movement speeds: 6 units a second forward, walking at 0.4 of forward
  (`PlayerMovementLimits.ForwardSpeed`, `MovementRules.WalkSpeedRatio`).
- Interaction range 10 units (`InteractionRange.Max`).
- Out-of-combat regeneration every 8 seconds, and how much health each tick
  gives back: the character's whole stamina times a factor per class plus a
  constant per class (`CombatService.RegenByClass`), read in the live game
  (see [GAMEPLAY_READINGS.md](GAMEPLAY_READINGS.md)).
- Corpses stay 150 seconds with loot and 30 seconds empty.
- A creature that gives up a fight, because it was outrun or everyone
  fighting it died, is back at full health at once
  (`src/server/PlaySession.Evade.cs`). Clearing its auras and its threat at
  the same moment is this project's own choice.
- A creature walking home takes 0 from everything and cannot be pulled into
  a fight until it is back at its spawn point (observed in play by the
  owner; `src/server/PlaySession.Evade.cs`, `CombatService.Evading`).
  Refusing an attack on it, rather than letting the swing start and evade,
  is own choice.
- The 15-minute cooldown on the stuck-rescue request.
- Quest log of 12, starting bag of 12 slots and bank of 24.
- Guild names are limited to 16 characters; guilds are limited to 240
  members. Founding needs level 10 (`src/game/GuildService.cs`).
- 10 health per point of Stamina (`CombatService.HealthPerStamina`) and 15
  mana per point of Wisdom for mana classes (`SpellService.ManaPerWisdom`),
  as the character sheet shows.
- Losing a duel leaves the loser on 10 health (`PlaySession.Duel.cs`).
- Speech is heard out to 20 units (`ChatService.SayRange`).
- A group of creatures keeps the arrangement it spawns in and turns with its
  leader as it walks (`src/game/CreatureFollowService.cs`). The turn
  threshold of about ten degrees is this project's own choice.
- Attack power per class, level, Strength and Agility
  (`PlayerStatCurves.AttackPower`).
- Any vendor buys a bag stack of anything that has a price, whether or not it
  stocks that item, and pays the item's sell price for each one in the stack
  (`VendorService.Sell`).

## Design choices that follow CypherCore

CypherCore (https://github.com/CypherCore/CypherCore, GPL-3.0) is a World of
Warcraft server emulator. Where this game shows no rule, the server may
follow CypherCore's behaviour as a design choice. No CypherCore source code is
copied into this repository.

| Rule | What follows CypherCore | CypherCore commit checked |
|---|---|---|
| Creature notice range (`src/game/EncounterRules.cs`) | The shape: start from a base range, add a fixed step per level the creature holds over the player (subtract per level below), count a creature above the level cap as at the cap, and clamp between a floor and a ceiling. That a higher-ranked creature and a ranged fighter each notice from further off is observed in play by the owner. The step (0.5 per level), the limits (4 to 18) and the size of each bonus are this project's own. | `96596913746bd7861a9f68158a8135b2b62d3450` |

## This project's own design choices

These rules are not visible in play and do not follow CypherCore; they are
this project's own:

- Mining (`src/game/MiningRules.cs`, `MiningPool.cs`): the active share,
  three-minute rest, per-map ores and weights, one ore per node and the
  Mendel's halved cast are provisional and unverified in play.
- View range 60 units (`WorldSessionState.DefaultViewRange`): how far away a
  player is told about creatures and objects. A player cannot measure it;
  a rough check in play puts it about there, and the round figure is own
  choice.
- Mounts (`src/server/PlaySession.Mounts.cs`, `src/game/SpellLessons.cs`,
  `MountRules.cs`): no NPC in 1.1.4 sells the riding-skill items a Bond asks
  for (no creature uses the sale lists that carry them, 110, nor 112 for the
  Aryurumoka horse, nor 78, 114 and 116 for the permits; only creature 3113
  in map 13 sells Bonds), so a riding skill counts as known by anyone who meets the requirements
  of the item that teaches it (the race's permit and level 20). A rider moves
  at 1.6 times their speed, the ratio `ubiquitous-client` runs a rider at; casting
  anything else, attacking, dying or changing map gets them down, and the
  mount spell or `mount.dismount` does so at once. The ride shows among the
  rider's buffs as an aura that lasts until taken off. Mounts are not saved
  across a relog.
- A refused sign-in says "This username does not exist, or the password is
  incorrect." — the wording the game shows (observed in play), the same for an
  unknown user and a wrong password.
- A new character starts with no money (observed in play). Only the synthetic
  development content, used with no data package, hands out a large purse.
- Development logins enter the world with at least 99,000 gold
  (`DevelopmentFixtures.Purse`), enough to buy every mount while testing.
- Only the team leader, or a player on their own, may change the dungeon
  mode, and the change is refused while anyone is inside one of the leader's
  dungeons, as `dungeon.restart` is. The chosen mode lasts until the server
  stops. A Legend creature counts as the creature it replaces for barriers,
  summons and finishing a run (`DungeonCopy.AuthoredEntry`).
- A team member who leaves the world keeps their place, and the lead, for
  five minutes (`PartyService.OfflineGrace`); after that they are removed
  as though they had left. Own choice: long enough for a relog or a dropped
  connection.
- Talent points (`src/game/TalentBook.cs`): one point per level from level
  10, so 51 at the level cap. The operator's data holds the trees and their
  tier gates but not the rate.
- A talent spell with a power cost, a cooldown or a cast time is an ability
  that joins the spellbook; any other talent spell is passive
  (`src/game/TalentEffects.cs`).
- A level 60 character knows every rank of its class's spells, and every
  higher rank of a talent ability it holds (`PlaytestSpellbook.AtMaxLevel`,
  `TalentAbilityRanksAtMaxLevel`). Read from the operator's `spell_new`:
  column 1 is the talent tree a spell belongs to, column 6 its rank and
  column 27 the first rank of its chain; a chain whose first rank is a
  `spell_talent` rank spell is a talent ability. That 60 grants every rank
  is this project's own rule.
- The level a class spell is learned at is the required level of the lesson
  item that teaches it (the item's on-use spell has the spell as a learn
  effect), the lowest among the items the class may use; for example Fireball
  II, III and IV are taught at levels 6, 12 and 18. A spell no item teaches
  comes with the class (level 1), and a talent ability's higher rank no item
  teaches counts as level 60 (`SpellLearning`). The GM commands
  `admin.setLevel` and `admin.learnSpells` use this.
- Talents can be reset at any time, for free (`talent.reset`). The game has a
  reset (the operator's `String_Spell` names a "Reset Talents" spell); its
  cost and where it is offered are not in the data.
- Creature hunting (`src/game/CreatureHunting.cs`, `src/server/PlaySession.CreatureHunts.cs`):
  only small animals are hunted, a hunter notices one at its player notice
  range for the animal's level, gives up at its hold range, and one session
  runs at most 4 hunts at once. A wolf going after rabbits is observed in
  play by the owner.
- Faction stance (`src/game/FactionStance.cs`): the table of who may attack
  whom follows what a player sees (town NPCs of your own faction cannot be
  attacked; neutral creatures fight only once hit; hostile creatures attack);
  its structure is this project's own.
- The prison's last boss appears `GameContent.CosmoDepthPastDoor` (120) units
  along the hall past the inner door.
- The Executioner's armour-and-sweep rhythm opens at 9 seconds and repeats
  every 17; a cover holds 11 seconds (`src/scripts/Prison/ExecutionerScript.cs`,
  `BossTiming.CoverHolds`).
- From the dungeon field guide (`docs/guides/dungeon-field-guide.md`,
  compiled from online guides, videos and observed play): the prison
  bosses' yells; Ariel's Putrefying from the pull; Cosmo's Shadow Shield
  once he changes back at 25%; the "faraway gate" notice once Ariel and the
  Executioner are both down, and Cosmo arriving
  `GameContent.CosmoArrivesAfter` (10 s) later; a puppet left standing
  `PuppetStandsFor` (10 s) before Cosmo eats it. The spell ids are the
  operator's `spell` rows of the same names (Putrefying 2651/2766, Shadow
  Shield 2638/2779, Normal/Legend).
- Also from the field guide: an Undead Puppet crumbles
  `CosmoScript.PuppetCrumblesAfter` (12 s) after it rises; Decayed Seed and
  Flame Curse fall on any player in the fight; which prison inmate uses which
  move, Bloodlust at 80/60/40/20%, Siphon Life only below half health with a
  Vicious Orc Prisoner beside it, and a Bone Golem breaking into two Inferior
  Bone Golems (`PrisonInmates`). How often each inmate move repeats is this
  project's own choice. The Legend counterparts of the inmate spells are the
  operator's `spell` rows of the same names in the Legend block.
- `spell_affect_type` names effect targets 4 "AreaEnemy", 14 "Area Enemy Of
  Target" and 16 "Position Area Enemy" (`SpellEffectTargets`), read from the
  operator's table. The table gives no radius: an area round a creature
  reaches 15 units and one round its target 6, this project's own choice
  (`PlaySession.CreatureSpells.cs`).
- The prison follows the field guide where it gives a number and the earlier
  video readings disagree: Cosmo takes the demon shape at 75%, and raises a
  puppet every 20 s with the turn between for eating it; Ariel's Decayed Seed
  repeats every 10 s (its 6 s opening is the video reading, since the guide
  says only that it comes early). Poison Teeth stacks to 5
  (`GameContent.SpellStackLimits`), also the guide's.
- Andare's three idle lines are the operator's `String_Script` rows beside
  the one the guide quotes; how often he speaks (`CreatureScript.ShoutEvery`,
  45 s) is this project's own choice. The Executioner's second "Osseous Blade
  Armor" row (2781, a slow heal) exists only in the Legend block, so only the
  Legend Executioner casts it, after each armour. Cosmo's "Remove All" row is
  read as what ends his demon shape and shield when his fight resets without
  his death; that reading is this project's own.
- Relic's Key (map 15, `swamp_eye`): the way the ritual runs (talk to
  Tharrath, follow him, three blocks of waves on a timer, a boss once a
  block is cleared, failure if he dies), the bosses' moves and thresholds
  and which creature uses which move are the dungeon field guide's. So are
  the creatures in every wave and when each comes (`RelicsKeyRitual.Blocks`):
  the first wave at once, then 35 s and 30 s; the second block 25, 30 and
  30 s after Hathraad dies; five waves 20, 30, 40, 45 and 50 s after Kermode
  dies; each boss 20 s after its block is dead, its warning 5 s after it
  appears; the block's opening line 5 s into the first block and 10 s after
  each boss; the farewell 10 s after "It's done!". Every
  line Tharrath, Hathraad and Alr-Rahjil say is an operator `String_Script`
  row; which line is a boss's pull or death line ("Heretics must die!" /
  "No...." for Hathraad, "You dare disturb me! You will die!" / "I will
  devour your rotten soul when next we meet in hell!" for Alr-Rahjil) is
  read from the rows' order around the lines the guide quotes, this
  project's own reading. Spell and creature ids are the operator's rows of
  those names; the Legend copy is map 15 with the swamp's Legend rows
  (`GameContent.RelicsKeyLegend`), not map 16. Tharrath's chat-menu script
  591 starts the escort (`GossipScripts.StartsRitual`), and he becomes the
  sturdier Tharrath row (2660, 2662 in Legend) once he starts the ritual:
  both readings are this project's own.
- Own choices in Relic's Key, to be checked in play: where Tharrath starts,
  where the ritual is held and where waves appear
  (`GameContent.RelicsKeyRitualSite`, each on a spot a creature in the
  swamp's spawn table stands on); the dungeon gates, laid round the gate
  objects 205 and 206 the way the prison's are round theirs; the pause
  before a retry (15 s); every move's repeat time where the guide says only
  "every so often". Kermode's
  shouts every 8 s, Tempest every 10 s, Alr-Rahjil's rotation every 5 s and
  the reach of Kermode's shouts (30), Tempest (30), Wraith Blast (20) and
  Pounding Glow (8) are the guide's (`GameContent.SpellReach`). Break Armor
  stacks to 5, as the guide says.
- A creature spell with a cast time in its `spell` row lands only when the
  cast ends, and a self aura with no duration pulses every
  `AuraService.TickInterval` until the creature ends it or dies. Of the
  sessions fighting a creature, only the one whose player it has chosen acts
  for it. These are this project's own design.
- A training dummy stops counting itself as fighting 8 seconds after nobody
  is in reach (`TrainingDummyScript.CombatExitDelay`).
- Melee reach 3 units.
- Wearing and using an item share one requirement: the character's level,
  class and race must all be ones the item's row allows, with one refusal
  for any failure (`PlaySession.MeetsItemRequirements`).
- Travel fares (`src/game/TravelService.cs`): the stops and legs are the
  operator's `teleport_spot` and `t_bus_route` rows; only bus stops are sold,
  legs run both ways, and a fare is the cheapest total of legs, searched when
  a quote is asked. A pair no legs join has no fare, which is not the same as
  a free one.
- Which loot groups a creature carries (`tools/asset-extractor/PackageImporter.cs`):
  the operator's `loot`, `loot_feature` and `loot_feature_items` rows. Only
  `loot` rows whose column 2 is 1 (a creature) are read; a group attaches only
  when its level range admits the creature; money always rolls from the
  creature's own range, never the group's.
- Weapon-hit spell effects (`PlaySession.SpellEffects.cs`): a main-hand,
  off-hand or ranged weapon hit rolls the ordinary swing and then applies the
  effect's percent and flat bonus; an off-hand hit with no off-hand weapon
  does nothing.
- Where a released spirit lands (`src/game/GraveyardLookup.cs`): the nearest
  graveyard on the same map, then the map's recall point, then where the
  player stands.
- Loot generation (`src/game/LootService.cs`): per-item chance rolls, top-up
  to the group minimum with the nearest misses, trim to the group maximum, at
  most 8 rows per corpse.
- Selling (`VendorService.Sell`): a sell price of zero in the item row is
  read as "no vendor pays for this" and refused with `cantSell`; an item row
  with no sell price column is priced at half its buy price, or half the
  vendor's price for it.
- Patrol routes (`src/game/PatrolRoutes.cs`): generated around each spawn,
  until a route is placed in `content/patrol-routes/` from readings taken
  with the patrol editor.
- Equip ordering when two rules apply at once (`src/game/InventoryService.cs`).
- Map bounds covering the full extent of the highest terrain tile.
- The internal 64-bit entity id layout (a per-type number above a low byte
  holding the object type; `src/common/EntityId.cs`). It is not sent on the
  wire: clients see ids such as `Creature:1`.
- Wander (`src/game/CreatureWanderService.cs`): destinations spread evenly
  over a circle of the spawn's move range; a pause rolled between 2 and 6
  seconds between legs, and one of 0 to 6 seconds before the first, so that
  creatures do not move in step.
- Server cadence (`src/world/SimulationTiming.cs`): a 400 ms tick; a session
  at rest runs its combat pump every other tick.
- Attack speed: +p% divides the swing interval by (1 + p/100).
- A blow settles in this order: nothing on a dead or evading body; both sides
  enter combat; a shield takes its share; health drops; death. So a fully
  absorbed blow still starts a fight, and a killing blow never leaves anyone in
  combat with a corpse.
- Saved characters keep the field names `bagCapacity` and `bankCapacity`;
  renaming them needs a save and schema migration.
- Threat switching (`CombatService.ThreatSwitchMargin`): a creature turns on
  a new attacker once that attacker's hostility is 30% above its victim's.
- Item use (`PlaySession.Inventory.cs`): using an item spends one of it
  unless the item can be worn.
- Pack assist and giving up (`PlaySession.CreaturePack.cs`,
  `CreatureScript.GivesUp`, `DungeonInmateScript.GivesUp`): who joins a
  group's fight and when a creature drops a fight.
- Ranged auto-attack (`src/game/CombatService.Ranged.cs`): the stance is
  derived from whether a shot is being readied; the first shot comes one
  weapon interval after the switch, moving holds fire until the archer has
  stood still for 0.75 seconds, and at most 4 shots per attacker fly at once.
- Off-hand swings (`CombatService.SetPlayerOffHandWeapon`): a weapon-class
  item in the off hand lands its own blow 0.4 of a swing interval after
  each main-hand blow, for its own damage roll (`items`), with no off-hand
  penalty.
- Quest sharing (`PlaySession.QuestSharing.cs`, `QuestService.ShareRange`):
  a party member within 40 units on the same map is offered the quest; the
  same gates as accepting it from its giver decide who may take it.
- Players: a step over 18 units (three times forward speed) is refused; a leap
  lands 1.5 units short of its target; a heal or buff with no friendly player
  selected lands on the caster; stealth ends on any attack, enemy cast or
  damage taken; crossing to another map removes every effect; spell damage
  between players ignores armour.
- Party kick votes (`PartyService`): a strict majority of everyone but the
  target, 30 seconds to answer, silence counts as no. The client's strings
  describe no vote.
- Water costs four times land to route across (`NavigationAreas.SwimCost`):
  swimming is half running speed, and players keep to land when the way round
  is not long. The factor is this project's own.
- Modules (`modules/`, `src/module-api`): a module is a folder dropped into
  `modules/` and built into the server, the arrangement AzerothCore
  (https://github.com/azerothcore/azerothcore-wotlk) uses for its modules. The
  contract (`IModuleHost`, socketless connections, module accounts) is this
  project's own; no AzerothCore source is copied.
- Playerbots (`modules/playerbots`): what bots do takes mod-playerbots
  (https://github.com/mod-playerbots/mod-playerbots) as its reference: random
  bots on accounts of their own that level by questing and grinding, join a
  player's group when invited, follow and assist their leader, and answer
  short chat commands. No mod-playerbots source is copied, and its design is
  not followed: these bots are clients of the WebSocket protocol, not server
  objects, and the order of decisions, the command words, every timing and
  distance, the item weights (`BotKnowledge.Worth`), the role each class
  plays (`BotBrain.RoleOf`), how often bots pause, talk or step away
  (`BotBrain.Idle`), how they vary their routes (`Bot.PickDetour`) and how
  often a bot no player is near takes a turn (`BotManager.WatchedWithin`,
  `unwatchedTurnEvery`) are this project's own.
- Random bots spread over levels and the day (`modules/playerbots`): starting a
  random character at a random level, at a place its level fits, follows
  mod-playerbots' random bots. The level range, judging a place by the median
  level of hostile creatures within 150 units, leaving a place outgrown by five
  levels, counting trips within two levels of the best as equally good, and
  stints of 40-240 minutes with 10-90 minutes away are this project's own.
