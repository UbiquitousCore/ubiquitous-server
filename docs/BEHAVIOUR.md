# What the server does

The observable contract with `ubiquitous-client`, system by system. Message names are
this server's own; codes are in `architecture/wire-codes.md`; where each value
comes from is in `PROVENANCE.md`.

## Connection, login and characters

- JSON text frames only; a binary or over-size frame closes the socket. 60
  seconds of silence closes it; `session.ping` gets `session.pong`.
- A missing or null required field gets `protocol.error invalid_payload`; any
  other handler failure gets one `protocol.error internal` with a generic
  reason, never the exception text.
- `account.login {userId, passHash}`. A refused login may be retried on the same
  connection, up to five times; after five refusals, or once signed in, a
  further login gets `invalid_state`. `account.loginResult` carries `isAdmin`,
  true only for a GM account that signed in; the client shows its admin panel
  from it. The client sends
  the typed password as `passHash`; the server stores it as
  `pbkdf2-sha256$<iterations>$<salt>$<hash>` and compares in constant time. A
  stored value without that prefix is accepted once and rehashed. Five failures
  for one user id within a minute lock it for a minute, with the ordinary
  invalid-credentials answer. Accounts are never created over the wire; the operator makes them with the
  `account create` command (see docs/development/setup.md).
- `account.logout` signs the connection out (reply `account.loggedOut`) so it can
  log in again; refused with `not_authenticated` before a login and `invalid_state`
  while a character is in the world.
- A vendor row for a spell lesson carries `lesson: "learned"` when the character knows
  everything it teaches, else `lesson: "cannot"` when it could not be read now (another
  class, too low a level, a missing skill: the rules of using it). Any other row has no
  `lesson`; the client lays "Already learned" or "Can't learn" over the card.
- `characters.create` is refused (`characters.created {ok:false, code}`) for a
  fifth character, a name that is not 2-16 letters or is taken (ignoring case),
  a race/class pair with no loadout, or an out-of-range gender or look.
- A character can be in the world from one connection at a time; choosing one
  already in the world is refused and the other connection is left alone.
- `characters.return` saves and leaves exactly as a disconnect does, then
  keeps the connection signed in for `characters.list` / `characters.choose`.
- `admin.giveItem {entry, count}` and `admin.giveGold {gold}` add to the sender's own
  character, for a GM account only (`forbidden` otherwise). The item must exist
  and the count is 1-1000; gold is 1-100,000 whole gold. A full bag answers
  `item.refused`. Items arrive as `inventory.itemAdded`, gold as
  `player.valuesChanged`. Each is logged with the account name.
- `admin.setLevel {level}` (1 to 60) and `admin.learnSpells` are GM-only too. A new level
  restores health and power, resets experience, and sends `player.valuesChanged` and
  `talent.state`, plus `player.expGained` with `levelUp` when the level went up, so the
  banner, sound and glow play as for an earned level. Learning teaches every rank of
  the character's class and of the talent abilities it holds that the current level
  has reached (the level of the lesson that teaches it), as `spell.learned` and
  `spell.book`; with nothing new it answers `invalid_state`. A level change also sends a
  fresh `spell.book`: ranks above the new level are forgotten, and an action button
  that held one takes the highest rank still known, or empties.
- `admin.setInvincible {on}` is GM-only. While on, nothing damages the character (creatures,
  spells, auras, other players); the server answers `admin.invincible {on}`. It lasts until
  the character leaves the world, and each entry starts without it.
- `dev.enterWorld {characterRef}` skips login and joins the same entry path.

## World entry, maps and movement

- Entry sends `world.loading`, `world.entered`, `world.ambient`, one
  `entity.spawned` per body in range, `world.hiddenObjects`, inventory, quest
  log, `spell.book`, then a `player.updated` snapshot. Every map change (gate,
  graveyard, bus, dungeon) runs the same entry: it ends any fight, removes
  every visible body and clears per-map state (auras, open loot).
- Walk-through gates test the whole line of an accepted move, so a fast move
  cannot step over one.
- `player.move` is refused, answered with `move.accepted` at the server's
  position, when the step exceeds 18 units (scaled by slows and speed-ups),
  X or Z is not finite or is outside the map rectangle, the player is stunned
  or asleep, or it crosses a shut dungeon barrier (`barrier_closed`). Height is
  taken as sent. An accepted move is answered and forwarded as `entity.moved`.
- A fall hurts only when it began with a jump (`jumping`) and ends in a
  ground landing (`landed` or `landedMoving`); a drop that started as
  `falling`, and a jump that ends any other way (`swimming`, a teleport),
  never hurt. The height is the highest accepted Y of the jump less the
  landing's Y; landing speed is √(2·18·height). Above 18 units a second the
  fall costs maximum health × (speed − 18) / 12, sent as `spell.outcome`
  from and to the player with `result: "fall"` and spell 0; 30 or faster
  kills (about a 25-unit fall). A fatal fall is a death to no one:
  `unit.died`, then `player.death` naming the player, with the ordinary gold
  penalty.
- `moveSpeed` tells the player's own client the speed the cap allows.
- Admins skip the step cap and bounds check, except after a map change until
  the first accepted move on the new map or three seconds: a client still
  reporting old-map coordinates must not place the player there on the new map.

## Visibility and replication

- Creatures and static objects within 60 units are spawned and removed per
  session and never broadcast: fanning them out would despawn bodies other
  clients can still see. Players on the same map instance see each other
  map-wide. Movement, combat, deaths, loot state and emotes go to every session
  on the map instance.
- A map's population is built in spawn-id order the first time anyone enters;
  ids come from a per-instance counter, stable within a boot only.
- A creature simulates while its home tile (16 units) is within 2 tiles of a
  player, or for 3 minutes after, or while fighting.
- `entity.updated` sends only changed fields, 500 ms after the first change in
  a batch. Entry snapshots and reward acknowledgements go at once. A corpse
  sends nothing after `unit.died`.
- A body first shown to a screen carries what it already wears (`auras` on
  `entity.spawned` / `world.entered`), but not the unit-value states, so a body
  another player slowed arrives with the icon and without `slowed`.

## Creatures

- **Wander**: a walker with no patrol or leader walks to straight-line
  reachable points within its move range, pausing 2-6 s (0-6 s before the
  first leg, so a group does not move in step). Each leg is two `entity.moved`:
  setting off with its target, and arrival.
- **Patrol**: a spawn whose route is in `content/patrol-routes/` walks it;
  every other patrol walks a loop generated from its spawn id, so every session
  and restart sees the same one. `patrol.save` (admins) writes the file and
  takes effect at once.
- **Escorts** keep their spawn-time offset from their leader, turned with it.
- **Groups**: spawns naming the same leader are one group. Touching one brings
  in every idle member and nobody else. A follower never gives up while its
  leader fights.
- **Noticing**: an idle hostile creature notices a living, unstealthed player
  within its sight range (`creature_proto`, or 10), adjusted by level gap and
  rank and clamped to 4-18 units. The ceiling is what keeps one dungeon group's
  pull from waking the next. The creature opens the fight (`attack.meleeStart`
  names it as attacker); the player swings back only once something strikes
  them, or at once if they attack it themselves.
- **Chase** at 6 u/s to 3 units, following baked walkable ground.
- **Giving up**: open world, beyond its leash (`creature_proto`, or 40) from
  the player or from home. In a dungeon only outrunning counts (beyond leash and
  out of reach for 5 s), or a fight would switch off on entering each room.
- **Evading**: a creature that gives up is at full health at once (sent to the
  whole map), loses its auras and threat, and walks home at 6 u/s on the map
  tick. Until home it takes 0 from everything (`evade`), cannot be attacked or
  pulled, and swings at nobody.
- **Threat**: a creature switches target at 30% more hostility; an attacker
  that stops hurting it drops off after 10 s.
- **Guards** answer a call within 15 units of a player's target.
- **Hunting**: an idle hostile creature hunts small neutral animals it would
  notice as a player of that level, over the ordinary combat messages. One
  quarry per hunter, at most 4 hunts per session's view; a player joining
  either side ends it; the corpse has no loot.
- **Training dummies** (`creature_proto` flag) never notice, swing or chase and
  lose no health; content-bound dummies lose health and can die. Both are sent
  `neverFightsBack`.
- **Corpses**: 150 s with loot, 30 s empty (emptying brings the end forward,
  never later). One deadline per corpse on the map. Respawn after a random time
  between the creature's own minimum and maximum, or 30 s.
- With festival content off, seasonal creatures and props are left out and the
  hidden props listed in `world.hiddenObjects`.

## Combat

- `attack.engage` is refused if the player is dead or stunned, or the target
  is dead, invisible, friendly, or beyond 3 units (25 ranged). Another player
  is a valid target only in a duel or when both are PvP-flagged.
- First swing at once, then every 2 s (faster with attack speed); swings wait
  while out of reach. 5% miss less hit rating; crits double at 0.1% per rating
  up to 30%. Damage is the weapon roll (`items`) or 12 unarmed, plus strength
  and attack power. An off-hand weapon swings 0.4 of a swing after each
  main-hand blow.
- Creatures swing for their own min-max (`creature_proto`). Dodge, parry and
  block are 0.1% per point; armour takes 0.1% per point up to 75%; every hit
  does at least 1.
- With a ranged weapon the fight switches to ranged out of melee reach: a shot
  every 2 s up to 25 units, flying at 30 u/s and landing on arrival. Moving
  holds fire until 0.75 s still; at most 4 shots in flight per attacker.
- A blow settles in order: nothing on a dead or evading body; both enter
  combat; a shield takes its share; health drops; death. So a fully absorbed
  blow still starts a fight, and a killing blow leaves nobody in combat with a
  corpse.
- Out of combat, health regenerates every 8 s; health per tick is
  `⌊stamina × factor + constant⌋` plus regeneration rating.
- Power depends on the class (`docs/POWER_READINGS.md`): mana (Mage, Monk)
  ticks every 8 s, halved in combat; vigor (Ranger) ticks +10 every 1 s; a
  Warrior's morale starts at 20, drifts back to 20 by 2 a second out of
  combat, and gains from hits taken, hits landed and dodges, parries and
  blocks. A Ranger loses vigor to each hit taken, down to 20. Spending power
  does not delay the next tick. The swing time behind a morale gain is the
  server's one swing interval, hasted by attack speed, since weapons carry no
  speed of their own here.

## Spells and auras

- A shape-changing aura (`aura.applied` for a spell that turns its wearer into a
  creature) lasts the spell's duration, ends the shape it replaces, and a second
  cast of a timed one refreshes it. One with no duration toggles off when cast
  again. The client reads the display from its own spell data.
- A creature action marked to hold still stops that creature swinging and
  walking until the cast lands (Ariel's detonation).

- `spell.request` checks in order: stun or sleep, silence, operator-only,
  equipment, a visible living target (unless self-targeted), spell book,
  cooldown, power. Refusal is `spell.failed {reason}`.
- Cast data comes from `spell_new`, effects from `spell_effect`. A cast time
  sends `spell.casting` and resolves on the combat tick; moving does not
  interrupt; `spell.abort` cancels without cost. Everyone on the map sees
  casts and auras.
- A spell runs all five effect slots (`spell_new` 33-37). The first leads;
  later ones land on the caster when their target column names the caster, and
  on the target otherwise, only once the lead lands (a blow or damage only on a
  hit, not on a target it killed). On another player only the lead effect and
  a damage spell's chill or slow apply.
- Effects conditional on a caster or target state (`spell_effect` 16/17) are
  not applied. Area and chain effects hit the selected target only.
- Damage spells: 8% resist, 5% miss, crit by spell crit rating.
- A spell with a speed (other than a weapon shot or a leap) sends
  `spell.released` at once and lands when its projectile arrives: distance at
  release over speed. Damage, auras and the target's reaction wait for it; a
  target dead or gone by then takes nothing.
- A slowed creature chases at its slowed pace, and `entity.moved` carries that
  speed.
- Heals, heals over time, buffs and speed-ups go to the selected player when
  that is a non-opponent player, otherwise to the caster. The target's session
  applies, reports, holds and ends the effect.
- A shield lands on the caster and takes its percentage of each blow after
  armour until spent or expired; reports carry `damage` and `absorbed`.
- A power restore only works for a class that spends that power.
- A spell with no cost, cooldown or cast time is passive and refused.
- A leap (Charge, Shadow Blink...) puts the caster 1.5 units short of the
  target, facing it, via `move.accepted`.
- Stealth hides the player from everyone outside their team (they are told the
  player left) and makes outside actions on them refused; creatures ignore it.
  It ends on starting a fight, an enemy cast, a swing or shot, or taking damage.
- Crossing to another map removes every effect on the player without
  `aura.ended`. Dying ends every aura with `aura.ended`.
- Every character has an operator kill spell and full heal, refused to
  non-admins.

## Death, levels and talents

- A player death sends `unit.died` and `player.death`; attackers walk home.
- Gold lost: none to level 10, then 500 / 1,500 / 3,000 / 5,000 / 8,000
  copper by ten-level band, capped at what is carried, none for a PvP kill.
  Nothing else is lost.
- `player.unstuck` while dead releases to the nearest `map_graveyard`, else the
  `map_info` recall point, else in place, at full health and power. Alive, it
  is an unstuck: once per 15 minutes, not in combat. A player who disconnects
  dead is saved at their graveyard.
- Losing a duel leaves the loser at 10 health, begging; forfeiting plays
  nothing.
- Level cap 60. Kill XP is `5 × level + 50`, +5% per level above (to +50%),
  -10% per level for one or two below, then to nothing across a grey window of
  at least five levels; killer only. Quest XP is scaled 120/110/100/80/50/10% by
  level gap (repeatables pay in full). At the cap nothing is paid and stored
  XP reads 0. Level-up refills health and power.
- One talent point per level from 10. `talent.learn` is refused off-class,
  past max rank, with no points, below the tier gate, or with a prerequisite
  short of full rank. `talent.reset` is free. A passive talent applies only its
  flat attribute bonuses. Saved ranks are re-bought through the same rules on
  entry.

## Quests

- Marks: finished, incomplete, available, none. Available needs class, level,
  prerequisite and not-yet-done. The log holds 12.
- Objectives count kills (killer only), items in the bag, objects used and
  spells cast; each change sends `quest.updated`.
- `quest.complete` needs every objective, a valid reward choice and bag room.
- Guide zones point at the nearest spawn for the first unmet objective, then
  the nearest finisher.
- `quest.share` offers to party members on the map within 40 units who could
  accept it from the giver.

## Inventory, loot, vendors and travel

- Bag 12, bank 24, both upgradable for gold. Items take the first free slot.
  Equipping checks level, class and race; a two-hander moves the off-hand to
  the bag. Using an item spends one unless it can be worn.
- Every service interaction (gossip, vendors, quests, bank, loot, trade,
  teleporters, auctioneer, mailbox) needs 10 units; one check runs before any
  handler. Bus stops are measured on the ground plane only.
- Loot is generated for the killer: money from the creature's range (or 50
  copper); each group item rolls its chance, topped up to the group minimum by
  the nearest misses and trimmed to its maximum; at most 8 rows.
- Vendors sell without stock limits. Any vendor buys any bag stack at the
  item's sell price; a zero sell price is refused (`cantSell`); no buyback.
- Bus fares are the cheapest total of legs, which run both ways.

## Mining

- Locations (static object entry 52) are a shared pool per map; a quarter hold
  ore at once and only those are announced, as the ore node's entry. The ore is
  rolled when a location activates.
- Tapping an ore within 10 units casts spell 23 (halved for Mendel). Moving,
  fighting, being struck, dying, leaving range or another player mining it
  first interrupts (`spell.interrupted`). A second tap while gathering is
  ignored; a tap during another cast is `spell.failed`.
- The cast opens the ordinary loot window with one ore. Only `loot.take`
  mines the node (`entity.removed`); closing leaves it standing. A full bag
  mines nothing.
- Greenmont (map 13) has no ore: its spawn table places mining locations, but
  none ever activates.
- A mined location rests three minutes; taking the ore activates a rested (or
  never-mined) location at random, else the longest idle one.

## Social

- Chat: `say` (20 units), `general` and `trade` (map-wide, joined; `general`
  starts joined), `party`; trimmed to 256 characters. Whispers reach anyone
  online. A player who ignores the sender gets neither channel chat nor
  whispers from them; the whisper still reads sent. Ignoring drops the
  target from friends and leaves parties untouched.
- Parties of up to five; the leader invites (same map) and kicks. Membership
  belongs to the character: a member who leaves the world shows offline and
  keeps their place and lead for five minutes. Parties are not saved.
- A non-leader can start a kick vote: everyone but asker and target has 30 s
  (silence is no); a strict majority of everyone but the target passes it, the
  asker counting yes. One vote at a time; a member leaving calls it off.
- Guilds: level 10 to found, names up to 16 characters, 240 members; the guild
  dissolves when the leader leaves.
- Duels: request lasts 60 s, accepted within 15 units, 5 s countdown. A killing
  blow ends the duel instead. Harmful effects each left on the other end with
  the duel.
- Between players, a blow is applied by the victim's session on its next tick
  and that one report is what both see. Armour reduces weapon blows, not
  spells. Melee and spell range are measured to where the victim's own session
  last put them.
- Trade: both within 10 units; changing an offer after a confirm cancels;
  the swap happens only when both have the goods and the room.
- Mail and the auction house are shared services applied one change at a
  time. Bids refund the outbid player by mail; a buyout or won auction mails
  item and gold. No postage, expiry, deposit or cut.
- Weather is always clear.

## Dungeons (the prison)

- Walking into the prison mouth on map 1 opens a copy of map 14 owned by the
  party leader (or the lone player); a party lands in the same copy and copies
  never see each other. A dungeon map is only ever played in a copy: arriving
  on one any other way (login, release) opens one.
- Ordinary gates never fire inside a copy, only the marked exit. The inside
  gate box is tight because the inside pair stands seven units apart: a wider
  one throws a player back out on their first step.
- Heights inside a copy are the authored ones, never terrain: a dungeon floor
  is mesh, and snapping to terrain drops bodies through it.
- An empty copy is held 15 minutes; re-entering resumes the run. A copy keeps
  ticking while empty. `dungeon.restart` ends the caller's copies, refused
  while anyone is inside.
- The leader chooses Normal or Legend for all their dungeons
  (`dungeon.chooseMode`), refused while anyone is inside or for a non-leader.
  Legend replaces each creature with its Legend counterpart, which counts as
  the original for barriers, summons and completion.
- A barrier refuses entering its rectangle until its milestone, and never
  refuses leaving, so nobody is trapped inside one. The server sends the
  rectangle and any model names in it (`dungeon.barrierOpened`); the client
  needs both, since a map has several copies of a door and the floor in the
  doorway must stay. Milestones latch for the life of the copy.
- The last boss is created once, when both other bosses are dead; that tick is
  not a finish. `dungeon.completed` goes to the copy once all three are dead.
- Kills belong to the copy: every member sees the same creatures and deaths.
  Corpses stay and nothing respawns until the copy ends.
- Boss scripts live in `src/scripts/Prison/`; their ability order and phase
  thresholds follow play, their timings (`BossTiming`) are this project's own.

## Persistence

- Accounts, characters, inventory and quest logs go to Postgres when
  `UBIQUITOUS_POSTGRES_CONNECTION_STRING` is set, otherwise to JSON files. Mail,
  auctions, guilds, friends and reports are always JSON.
- A character is saved on disconnect and every five minutes. With Postgres
  the three writes are one transaction; with files the character record is
  written last, so a failure leaves the older record. A failed periodic save is
  logged and retried next time.
- Parties, duels, trades, PvP flags, dungeon copies and chat channels are not
  saved.
