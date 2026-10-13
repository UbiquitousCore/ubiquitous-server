# Wire codes

What the codes and keys on the WebSocket wire mean. The names themselves are
pinned by `tests/integration/fixtures/wire-vocabulary.json`, which `ubiquitous-client`
commits byte for byte; message types are in `WebMessageTypes*.cs`, protocol
error codes in `WireCodes.ProtocolError`.

## Outcomes

`outcome` on `attack.swingResult` / `attack.shotResult` and `result` on
`spell.outcome`: `hit` (a damage-over-time tick included), `miss`, `dodge`,
`parry`, `block`, `critical`, `evade` (the target is walking home and took
nothing), and on `spell.outcome` only `heal` and `periodicHeal`.

All three reach the whole map. A blow between players is reported by the
victim's session, with the health the victim was left on; the attacker's
session sends nothing for it.

## Absorbed

`attack.swingResult`, `attack.shotResult` and `spell.outcome` carry
`absorbed` when a shield on the target took some of the blow: how much it
took, before `damage` reached the target's health. A blow the shield took
whole is `damage` 0 with `absorbed` set; a blow no shield touched sends no
key.

## Hands

`hand` on `attack.swingResult`: `main` (and every creature blow) or `off`.

## Dungeon modes

`mode` on `dungeon.chooseMode` / `dungeon.modeChosen` is `normal` or
`legend`; `count` on `dungeon.modeChosen` is how many copies the change ended.

## Deaths

`unit.died` carries `networkId`, the body, and `pos`, where it fell (`x`, `y`,
`z`, the same shape as every other position). `pos` is left out only when
the server holds no position for the body.

## Standing animations

`entity.spawned` carries `animationId` on a creature whose placement stands
doing something: sitting, sleeping, working, talking. It is a
row of the operator's `c_animationdata` table, and the client plays
that clip in place of its idle. A placement doing nothing sends no key.

## Never fights back

`entity.spawned` carries `neverFightsBack: true` on a creature that takes
blows and never answers them: one whose `creature_proto` row carries the
training-dummy bit (column 44, bit 7), or one content binds to the
training-dummy script although its row lacks the bit. Any other body sends
no key. The client keeps such a body in its neutral colour however often it
is hit; every other neutral creature turns hostile at the player's first
landed blow.

## Auras already worn

`aura.applied` and `aura.ended` report a change, so a screen that first sees
a body after an effect went on it is told what the body wears in the
message that shows it: `auras` on `entity.spawned` (a creature's spawn and
another player's alike) and on `world.entered` (the player's own body). A
body wearing nothing sends no key. The list rides on the spawn rather than
in messages after it, so a body is never drawn without what it wears, and
nothing else on the map is told again what it has already seen.

Each entry has the keys of `aura.applied` less `unitId`, which is the body
it is sent with: `spellId`, `casterId`, `harmful` (1 for a debuff, 0 for a
buff), `maxDuration` and `remaining`.
`remaining` is the time left as the message is sent; `remaining` and
`maxDuration` both 0 is an effect that lasts until `aura.ended` takes it
off. One entry per spell: where two players have put the same spell on a
body, the one with more time left is sent. A screen treats the list as the
whole of what the body wears.

A dead body wears nothing, and a player hidden by stealth is not sent at
all, so neither carries a list. Crossing to another map takes every effect
off the player's own body, so `world.entered` carries none after a crossing.

## Spell refusals

`reason` on `spell.failed`: `unknownSpell`, `noEffectData` (no effect this
server carries out), `onCooldown`, `notEnoughPower`, `invalidTarget`
(missing, out of range, dead or invisible), `incapacitated` (stunned or
asleep), `silenced`, `missingEquipment`, `alreadyCasting` (the cast under way
carries on).

## Item results

`code` on `item.refused`, `inventory.equipResult`, `npc.buyResult` and
`npc.sellResult`: `ok`, `inventoryFull`, `destinationOccupied`,
`itemNotInSlot`, `unknownItem`, `wrongSlot`, `vendorDoesNotSell`, `cantSell`
(sell price zero or unknown), `notAVendor`, `notEnoughMoney`, `cantEquip`
(level, class or race).

## Loot state

`state` on `entity.lootable`: `lootable` or `emptied`.

## Quest marks and quest states

`status` on `quest.statuses` (the NPC's mark for this player) and on quest
log, detail and update messages: `none`, `offered`, `underway`, `complete`.

## Quest sharing

A player sends `quest.share` with `questId`, for a quest they hold. Every
other party member near enough gets `quest.shareOffered` (`sharerId`,
`sharerName`, `questId`, `titleStringId`, `detailsStringId`, `minLevel`) and
answers with `quest.shareAnswer` (`questId`, `accept`). The sharer gets one
`quest.shareResult` per member (`memberId`, `memberName`, `questId`,
`result`): at once for a member who is not offered it, and when an offered
member answers.

| Code | Meaning |
|---|---|
| `alreadyHas` | The member holds the quest, or has handed it in. |
| `notEligible` | Level, class, an unfinished earlier quest or a full log bars them. |
| `outOfReach` | On another map, beyond sharing range, or not online. |
| `accepted` | They took it. |
| `declined` | They turned it down. |

## Team kick votes

Only the leader removes a member outright, with `party.kick`. Any other
member sends `party.voteKick` with `networkId`, the member they want gone.
Every member but the two of them gets `party.voteKickAsked` (`initiatorId`,
`initiatorName`, `targetId`, `targetName`) and answers with
`party.voteKickAnswer` (`agree`). Each no reaches the asker and the other
members asked as `party.voteKickRefused` (`memberId`, `memberName`,
`initiatorId`, `initiatorName`); a member who has not answered after 30
seconds is counted as a no and reported the same way. The vote passes once a
strict majority of everyone but the target agrees, the asker counted as
agreeing; it fails once that can no longer happen. The asker and everyone
asked then get `party.voteKickEnded` (`targetId`, `targetName`, `passed`). A
passing vote removes the member as a leader's kick does: `party.kicked` and
an empty `party.roster` to them, the new `party.roster` to everyone else.
A member leaving or being kicked while a vote runs calls it off, as a
`party.voteKickEnded` that did not pass. One vote runs at a time in a team.

## Team rosters

Every `party.roster` row carries `networkId`, `name`, `isLeader` and
`online`. A member who has left the world keeps their row with `online`
false, and the rest of the team is sent a fresh `party.roster` when they
leave and again when they return. Entering the world as a character who
still holds a place sends that character their `party.roster` straight after
the rest of the entry messages; a character in no team is sent none. A
member offline for five minutes is removed, and the team is sent the roster
without them.

## NPC conversations

`npc.gossip` answers `npc.talk` and `npc.previousPage` with `networkId` and
`entry`: the NPC's body and its creature entry. It names no menu. What the
NPC says, and the menus under its greeting, are in the user's own tables,
which the client reads by that entry and walks by itself.

`npc.pickOption` sends `networkId`, `menuId` and `choice`: the page the
player is on and the choice's script slot, 1 to 6. The server refuses it with
`invalid_gossip` unless the page is the NPC's greeting or a page one of its
choices leads to, and the NPC is within reach. Otherwise it answers with
`npc.choiceResult`, carrying the same `networkId`, `menuId` and `choice` and
a `result`:

| Code | Meaning |
|---|---|
| `teleported` | The choice sold a teleport. The price is charged (`player.updated`) and the enter-the-world handshake follows. |
| `notEnoughMoney` | The choice sells a teleport the player cannot afford. Nothing was charged. |
| `nothingHappens` | The choice runs something this server does not carry out. |

Where a teleport goes and what it costs are the server's own data, read from
the same tables; nothing in the request says either.

## Learning and mounts

Using an item whose spell teaches others answers with `spell.learned`
(`spellIds`, the spells now known) and then `spell.book`, and spends the item.
One the player cannot use yet, or whose spells they already know, answers
with `protocol.error` (`cannot_use`) and keeps the item.

Casting a mount spell seats the caster once the cast finishes, shown by the
`mount` unit value, and shows the ride as an `aura.applied` on the rider
(`harmful` 0, `maxDuration` 0: it lasts until taken off) that `aura.ended`
takes away when they get down. Casting it again, or sending `mount.dismount`
(no payload), gets them down at once.

## Administrator teleport

`admin.teleport` carries `mapId`, `x` and `z`, and optionally `y` (the
ground there otherwise). An administrator is moved there as by any other
journey, answered with `world.entered`; anyone else, or a map the server
does not know, gets `protocol.error` (`forbidden`, `invalid_teleport`).

## Talents

`talent.learn` carries `talentId` and buys one more rank of that talent;
`talent.reset` takes every rank back. Both answer with `talent.state`, which
also arrives on entering the world and whenever a level earns a point:
`points` is what is left to spend, and `ranks` lists `{ talentId, rank }` for
every talent with a rank bought. A learn that changed something is followed
by `spell.book` (an ability a talent teaches joins it) and `player.updated`
(`maxHealth` and `maxPower`, which a passive bonus can move).

A refused learn answers with `talent.refused`, carrying `talentId` and one of
these as `reason`:

| Code | Meaning |
|---|---|
| `unknownTalent` | No tree holds that talent. |
| `wrongClass` | The talent is on another class's tree. |
| `maxRank` | Every rank is already bought. |
| `noPoints` | No point is left to spend. |
| `tierLocked` | Too few points are spent in the talent's tree to reach its tier. |
| `prerequisite` | A talent it needs is not at its full rank. |

## Unit values

The keys of the `changed` map on `player.updated` and `entity.updated`. A key
is sent only when its value changes, so a client keeps the last value it saw
for each.

| Key | Value |
|---|---|
| `level` | Character or creature level. |
| `health` | Current health. |
| `maxHealth` | Maximum health. |
| `power` | Current power. |
| `maxPower` | Maximum power. |
| `xp` | Experience earned at the current level. |
| `xpToNextLevel` | Experience the next level needs. |
| `gold` | Money, in copper. |
| `strength`, `agility`, `stamina`, `wisdom` | The player's own Strength, Agility, Stamina and Wisdom with everything worn and active: the character's own value for its level plus gear, buffs and talents. Sent only to the player they belong to, at entry and whenever one changes. |
| `baseStrength`, `baseAgility`, `baseStamina`, `baseWisdom` | The same four with nothing added: the character's own value for its level. The character sheet shows a stat in green while it stands above its base. |
| `attackPower` | The player's own attack power, as a melee or ranged blow uses it. Sent only to the player it belongs to, at entry and whenever it changes, as are the rest of the sheet below. |
| `hitChance`, `critChance` | The chance a melee or ranged blow lands, and that it is critical, in hundredths of a percent (9500 is 95%). |
| `spellPower`, `healingPower` | What gear and auras add to spell damage and to healing. |
| `spellHitChance`, `spellCritChance` | The chance a damaging spell is neither resisted nor missed, and that it is critical, in hundredths of a percent. |
| `armor`, `armorMitigation` | The player's armour, and the share of a physical blow it takes away, in hundredths of a percent. |
| `dodgeChance`, `parryChance`, `blockChance` | The chance a blow against the player is dodged, parried or blocked, in hundredths of a percent. |
| `slowed` | 1 while slowed, 0 once not. |
| `stunned` | 1 while stunned or asleep, 0 once not. |
| `chilled` | 1 while chilled, 0 once not. |
| `spedUp` | 1 while sped up, 0 once not. |
| `stealthed` | 1 while stealthed, 0 once not. |
| `silenced` | 1 while silenced, 0 once not. |
| `mount` | The `mountprototype` row the body rides, 0 on foot. Sent to everyone who can see the body, and again to whoever starts seeing a rider. `moveSpeed` leaves the mount out: the client runs a rider faster itself. |
| `moveSpeed` | How fast the player may move, as a percent of normal speed: 100 unhindered, lower while slowed, higher while sped up, 0 while stunned or asleep. The server refuses a longer step, so the player's own client moves at it. Sent only once it changes; until then it is 100. |

## Numbered codes

The results of logging in and of creating or deleting a character are still
numbers, carried as `code` on `account.loginResult`, `characters.created` and
`characters.deleted`. The numbering is this project's own (`LoginCode` and
`CharacterCreateCode`), and `reason` carries the text.
