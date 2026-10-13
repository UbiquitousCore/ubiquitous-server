# TODO

Open work. **(client)** means `ubiquitous-client` needs changes too. Delete an item when it is done.

## Combat
- Clearing or losing a target (death, despawn, distance) does not stop auto-attack; a target change must not cancel a cast. **(client)** needs a clear-target gesture.
- Stun and sleep do not stop a swing already under way.
- Provoked neutral creatures stay neutral; faction cannot change at runtime.
- Neutral creatures never hunt prey.
- Guards do not attack hostile creatures on sight; they only join a fight a player is in.
- Hunt quarry never flees, and hunt damage is private to the session running it.
- Elite rank does not change experience.
- No killer-less deaths (falling, lava, hazards), so their gold penalty never applies.
- Creatures always spawn at their minimum level; the max-level column is not imported.
- Give-up rule has no grace period and measures from the spawn point, not from where the fight began.
- Creature health and threat are per session; players sharing a creature do not share its health bar or threat list, and an evade refill only lands in the session that let it go. Moving them (with auras and corpse timers) onto the shared map is the largest change left; build the state-diff tool (Testing) first so a before/after run can be compared.
- Weapon swing delay is ignored; ranged shots use the melee roll and a flat 2 s interval.
- Strength counts twice (direct damage and attack power); pick one.
- Resistances, spell/ranged power, damage bonuses, run/swim speed, creature-type bonuses and reflects are tracked but inert; only armour matters.
- Percentage attribute modifiers and ranged haste have no effect.
- Dodge and the direct Strength damage bonus count only Agility and Strength from gear and buffs, not the character's own; attack power counts both.
- Current health/power do not follow a max change when a Stamina or Wisdom buff starts or ends.
- No weapon-skill model: no dual-wield, one-handers always main hand, required-skill checks skipped.
- Item info on the wire lacks stats, armour, damage, delay, quality and binding.

## Spells and auras
- No global cooldown.
- Cast refusals send a bare number instead of a client-readable code. **(client)**
- Stealth has no detection.
- Body states (slowed, stunned, chilled…) reach only the caster's session.
- No rule for which spell ranks a level below 60 grants; at 60 a character knows every rank (`PlaytestSpellbook.AtMaxLevel`). Needs trainer or level-gate content.
- Unhandled effect kinds: enchants, teleport/charge, revive, area bursts, cooldown reset, power over time, attribute/power-based damage, reflection, shield-bash damage, dispel, eat/drink, summons, shape change, faction change; fear and charm import as nothing.
- Conditional effects (`spell_effect` columns 16/17) are dropped; needs target/caster state numbering and burning/bleeding/bruise tracking.
- Eating and drinking need sitting and a power tick.
- On-hit procs and weapon coatings never fire.
- Spell modifier auras are stored but never applied.
- Percent attribute auras change no number.
- Every periodic effect ticks 3× at 3 s regardless of duration; every effect shares the spell's one duration.
- Area and chain effects hit only the selected target.
- Heals and buffs use the hostile range column instead of the friendly one.
- `SpellDefinition` has no school or resistance.
- Passive talents only apply flat attributes; skill-line passives are never read.
- Creatures never cast or use ranged attacks.

## Quests
- No daily quests, race restrictions or chain follow-ups.
- Trigger objectives with no creature/item/object/spell never progress (no area triggers).
- Objectives naming a spell and an object only count the object click.
- Guide arrow points at the nearest spawn, not the quest's waypoint; item objectives get no arrow. **(client)** arrow should move to the hand-in once complete.
- Item-offered quests name their giver `item:{entry}`.
- Tutorial flow (lowest priority). **(client)**

## NPCs, vendors and scripting
- Gossip choices that run a script answer `nothingHappens`, except script 562 (to the Town of Silence); gossip cannot grant a buff.
- Guild founding is free and does not consume the guild stone.
- Vendor quantity column is not understood; no buyback.
- Top bag and bank upgrades are never offered (second currency untracked).
- Some NPC service flags (food vendor) do nothing.
- Mounts: verify in play whether a mount's speed-up percent (20 or 25) changes
  its speed, whether mounting is refused in combat, indoors or in dungeons,
  and whether a mount is kept over a relog. The riding-skill stand-in
  (`SpellLessons.KnowsRequiredSpell`) goes once riding skills can be bought.
  Mounts are not drawn on the character sheet or in the mount page's
  riding-skill tab. **(client)**
- Place Greenmont's (map 13) patrol routes with the `ubiquitous-client` patrol editor
  and record them in `docs/GAMEPLAY_READINGS.md` ("Greenmont patrols"), then
  decide which other walkers need placed routes. Stop actions (emotes,
  scenes) are missing.
- Under Docker, saved patrol routes are lost on rebuild unless `content/` is
  mounted. Drawing routes on the minimap. **(client)**
- Walk, chase and patrol speeds are the same for every creature; slows only change chase pace.
- Formation followers do not despawn with, or copy the idle of, their leader.
- NPCs loop one idle animation. **(client)**
- Choose an encounter scripting policy for bosses and escorts.
- List the data a contributor cannot get from their own client.

## World, maps and movement
- Verify start zone names and positions for Orc/Undead ("West Land", map 7)
  and Mendel ("Under World", map 6) in game. Mendel has no racial spell.
- `player.teleported` is never sent.
- Presence has no distance limit.
- The data package carries no zones (the importer reads `zones.tbl` only to resolve teleport destinations), so nothing zone-based (PvP areas, ambience) exists server-side. Add zones back to the package together with their first consumer.
- PvP flag is manual; zones do not set it.
- Weather and festivals never change in play (`AmbientService.SetState` has no caller).
- Corpse timers live in the session instead of the shared map; check durations.
- A creature turning for home looks idle until the next map tick.
- Only one map gate is connected and there is no return gate.
- Spawn heights are not snapped to the ground at load.
- No tile activation policy; every spawn on the map is scanned for every session's view (see Performance).
- Static objects carry no state (doors, interactables).
- Creature chase and wander ignore baked walkable ground: no route-finding, no "cannot reach", no fleeing.
- Walkable-ground bake keeps unreachable surfaces (roofs, cart tops), misses some outdoor floor, and may mirror spawn-table objects.
- Player movement is client-trusted: no server collision, reported height is used, speed is checked per move only, no authored no-go areas, refused map crossings are timer-held, and administrators skip the checks.
- Decide whether leaving map bounds should kill instead of snapping back.
- No jump check, fall damage, or jump/fall/swim state handling.
- Respawn timers for chests and zone objects are unconfirmed.

## Instances and dungeons
- No dungeon flag or player cap.
- Boss form changes do not reach the client.
- Bosses have no phase dialogue. Add the yells: Executioner "Cyclone Slash!"
  with Whirlwind; Cosmo "The demons stir within me!" (demon form), "The dead
  fight for ME!" (puppet), "Give me your power!" (eats a puppet). Ariel is
  silent. Legend uses the same lines. See `docs/GAMEPLAY_READINGS.md`.
- Prison boss spells not yet cast (timing unknown; verify in play): the
  Executioner's second Osseous Blade Armor (2781, heals); Ariel's Putrefying
  (2651, party damage aura) and instant second Detonation (2797); Cosmo's
  Shadow Shield (2638) and Remove All (2648). Legend: Putrefying 2766, Remove
  All 2780; whether Legend uses 2781/2797 is unknown. Record timings under
  "Spells not yet cast" in `docs/GAMEPLAY_READINGS.md`.
- Verify Devour Puppet's heal: the effect heals 4,000 (Legend 40,000), its
  description says 2,000.
- Verify boss timings in `BossTiming` against video; they are rough estimates.
- Ariel's script treats Decayed Seed as a root; it is a
  3 s stun.
- Legend has no level gate or exhaustion bar (see "Legend mode" in
  `docs/GAMEPLAY_READINGS.md`). The chosen mode is lost on restart, and only
  the leader who saves it is told: members, and a leader who relogs, see
  Normal until it is saved again. **(client)**
- Normal prison bosses show level 15 in video and on oac-head but
  `creature_proto` has 20-21 with 10,000-15,000 HP; the server keeps the
  client data (see `docs/GAMEPLAY_READINGS.md`).
- Verify in play that dungeon corpses stay and trash does not respawn until
  reset.
- Client keeps colliding with the opened dungeon barrier; verify its rectangle. **(client)**
- Review instance-copy branches in `PlaySession.cs` for content that belongs in scripts.
- Battlegrounds and arenas: queue and hand-off, teams, win condition and result, arena doors.

## Inventory and items
- Action bar is not saved.
- Mining **(client)**: draw nothing at entry 52 locally and show what `entity.spawned` announces; fade out on `entity.removed`; play the gather animation for spell 23.
- Mining values are provisional (see TODOs in `MiningRules.cs`). Herbs, chests and other gatherables (spells 57 and 1822) are not built; dungeon copies have no ore.
- No durability, enchants or sockets.
- Currency shows as raw copper. **(client)**

## Social
- No guild chat; guild ranks are leader and member only.
- Parties are lost on restart; offline members' rosters update late.
- Mail has no COD, expiry or return; claiming takes every attachment.
- Auction house needs no auctioneer and takes no cut.
- Duels do not move players back when they end; accept range is 15 and should be 20 with its own refusal code.
- Player-to-player periodic effects ignore the caster's spell and healing power.
- No player-created chat channels.
- No reputation, pets or companions.

## Data package
- No package version or upgrade rules.
- Class ids are not validated.
- Terrain is not checked against map bounds; spawns, travel stops and routes are not checked against walkable ground.
- No clean-checkout test that imports synthetic data and boots the server.

## Playerbots
- Some placed models ship no collision in any build's packs (desert tents, the suspension city, Eristar crystals, dungeon roofs and spiders), so bots and creatures walk through them; neither does `ubiquitous-client`.
- The walkable ground still shows each start area as a closed basin (map 1 about 45,500 and map 7 about 29,500 square units). Bots leave by bus or the Orc Teleporter; check in play whether a player can walk out, and if so where, so the bake can be corrected there.
- Bus stop 10 (map 1) and a few dungeon gates stand off the walkable ground; bots never travel to them.
- Bots never train: the server has no trainers or level-gated spell ranks yet (see Spells).
- Bots sell junk but never buy, give items in a trade, bid, mail or join guilds.
- Loot is private to each session, so groups have no loot rules for bots to follow.
- Bots follow a leader through map gates but not onto a bus or teleporter, and never enter dungeons.
- No command for a player or admin to add or remove a named bot (`.bot add`); needs a chat hook in the module API.
- Bots made before looks were imported keep look option 1; a random bot's character is never remade.
- A random bot started above level 1 wears only its starting kit; mod-playerbots gives its random bots gear for their level.
- Bots no player is near play every other world tick (`unwatchedTurnEvery`); measure the cost at a few hundred bots to see whether that is enough, and whether bot turns need running side by side (bounded) instead of one after another.

## Persistence
- Guild, mail, auction, friend and report state have no Postgres store.
- Migration runner has no lock; schema lacks foreign keys and indexes.

## Networking
- Wire gaps with `ubiquitous-client`: `world.ambient`, `chat.channelUpdated` and `emote.performed` have no client handler; the client listens for events the server never sends; vendor payloads carry the routing id outside the contract; `quest.list` has two reply shapes; invite/kick name the target differently; hub fan-out forwards melee but not ranged, spell, aura or teleport events; inbound `sequence` is ignored.
- No protocol version or capability handshake.
- No per-map command queue; no fake-clock multi-session ordering test.
- `SharedMapState` lives in `Ubiquitous.Game`, but AGENTS.md says `Ubiquitous.World` owns map state; settle which project holds it.
- Finish splitting `PlaySession`: model the shared combat, visibility and quest state as objects first (as `PlayerBelongings` does), then move each partial to a handler.

## Performance
Nothing here has been measured; start from the world tick's over-budget warning (`WorldTick`), then add counters where it points.
- Visibility still walks every spawn on the map for every session each tick (sorted once, but O(sessions × spawns)). Index creatures by grid cell on the shared map (`TileGrid`) so a view asks only nearby cells, and let a session whose position and map state have not changed skip the rescan.
- Aggro, chase joiners, guard and pack assist, hunts and creature scripts each still walk the whole visible set every tick; route, follow and wander share one pass (`TickIdleMovement`). Fold the rest into one pass that hoists the per-creature checks, one sub-tick at a time.
- Wire ids are strings everywhere inside the server; key internal maps by `EntityId` and turn to strings only at the wire.
- Sessions tick one after another under one shared-map lock; a slow session holds up the rest. Decide on sharding by map or a per-session time guard once measurements show it matters.
- A creature stays simulated for three minutes after the last player leaves (`TileGrid.SleepGrace`); measure, then shorten it or drop wander and patrol during it.
- A crowded entry sends one `entity.spawned` per visible body; batching them needs a client change. **(client)**

## Security and operations
- Logs carry no session ids, no audit trail of logins and character actions, and no redaction of connection strings or paths.
- `/health` does not track the database after startup.
- CI format job does not fail the build.
- Operator docs lack browser configuration, account setup, backups and log locations.

## Testing
- Dev-only `dev.stressTest` (admin): spawn N player-bodied bots around the training dummy in sight so `ubiquitous-client` can measure a crowd. Bots go out as `entity.spawned` player payloads (race cycling human/elf/orc/undead, looks from the `playerLooks` dataset, the requester's class, level and gear), wander, and engage the dummy; the dummy's script keeps them from aggroing anything else. Open: the dummy can die under 100 bots, ending the swings early; combat still keys off the table flag bit (pinned quirk), so an unflagged dummy may swing back at the bots; bots are sight-scoped, so other sessions never see them.
- No browser-driven run of the first-client scenario (login → enter world → move → reconnect).
- Regression scenarios for map changes, entity churn, malformed commands, persistence failures and multiple sessions.
- Build a state-diff tool comparing two recorded runs (positions, visible sets, event order).

## Client-only
- Another player's `player.revived` should stand their body back up in view; `ubiquitous-client` now ignores it (it used to apply it to the hero).
- Show `protocol.error`; add a release-spirit button and show the death gold loss.
- Hide uncastable spells; UI for weather, festivals, guilds, dungeon entry and whispers.
- Centred loot messages, level-up effect and sound, readable dark secondary buttons.
- Fountain collision, swim jump rate, stuck pickup jump, pickup animation, camera-facing particles.
- Audio: nothing plays. Work out the `sound.bin` format first; start with UI sounds behind the existing toggle.
