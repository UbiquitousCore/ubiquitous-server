# Power readings

Recorded from new characters, starter kit removed. All values read off the
character sheet and power bar. These tables are the reference as recorded;
the server calls the Ranger's bar **vigor** (`PowerKinds.Vigor`), where the
readings below say energy.

## Power type by class

| Class | Bar | Max | Rest value |
| ----- | --- | --- | ---------- |
| Warrior | Morale | 100 at every level | 20 |
| Ranger | Energy (vigor) | 100 at every level | 20 |
| Mage | Mana | scales with level + Wisdom | full |
| Monk | Mana | scales with level + Wisdom | full |

## Max power, level 1, starter kit off

Max Mana = level-curve value + 100 + (Wisdom x 15). The level-curve value at
level 1 is 1 for Mage and Monk. Morale and vigor ignore this and stay at 100.

| Race | Class | Wisdom | Max power |
| ---- | ----- | ------ | --------- |
| Human | Warrior | 9 | 100 |
| Human | Mage | 25 | 476 |
| Human | Ranger | 10 | 100 |
| Human | Monk | 20 | 401 |
| Elf | Mage | 27 | 506 |
| Elf | Monk | 22 | 431 |
| Orc | Mage | 24 | 461 |
| Orc | Monk | 19 | 386 |
| Undead | Mage | 28 | 521 |
| Undead | Monk | 23 | 446 |
| Mendel | Mage | 24 | 461 |
| Mendel | Monk | 15 | 326 |

## Passive regen ticks

| Bar | Tick every | In combat | Out of combat |
| --- | ---------- | --------- | ------------- |
| Mana | 8 s | half tick | full tick |
| Vigor | 1 s | +10 | +10 |
| Morale | 1 s | nothing | 2 toward 20 |
| Health | 8 s | nothing | full tick, class table below |

Mana tick: Mage = (5 + sqrt(Wisdom)) x Wisdom x 0.07 + 10, rounded down.
Monk = (8 + sqrt(Wisdom)) x Wisdom x 0.08 + 14, rounded down. In combat,
halved and rounded down. Warriors and Rangers tick 0.

| Reading, level 1, no gear | Tick |
| ------------------------- | ---- |
| Human Mage, 25 Wis, out of combat | (5 + 5) x 25 x 0.07 + 10 = 27.5 -> 27 per 8 s |
| Human Mage, 25 Wis, in combat | 27 / 2 = 13 per 8 s |
| Human Monk, 20 Wis, out of combat | (8 + 4.47) x 20 x 0.08 + 14 = 33.9 -> 33 per 8 s |
| Human Ranger, any state | +10 per 1 s, to 100 |
| Warrior or Ranger mana tick | 0 |

Morale drift, out of combat only: +2/s if below 20, -2/s if above 20, lands
exactly on 20.

| Reading | Ticks observed |
| ------- | -------------- |
| Warrior at 73 morale, fight ends | 71, 69, 67 ... 21, 20, stops |
| Warrior at 6 morale, fight ends | 8, 10, 12 ... 20, stops |
| Warrior at 20 morale, resting | no change |
| Warrior at 50 morale, still in combat | no change |

Health tick, out of combat only, every 8 s: Warrior = Stam x 2.0 + 9. Mage =
Stam x 1.2 + 6. Ranger = Stam x 1.8 + 2. Monk = Stam x 1.5 + 5. Rounded down.

## Morale gained in combat (Warrior)

On taking a hit: (damage taken / level) x 1.2, minimum 1. Uses the raw damage
number.

| Reading, level 10 Warrior | Gain |
| ------------------------- | ---- |
| Hit for 50 | 50 / 10 x 1.2 = 6.0 -> +6 |
| Hit for 45 | 45 / 10 x 1.2 = 5.4 -> +5 |
| Hit for 12 at level 60 | 12 / 60 x 1.2 = 0.24 -> minimum +1 |

On landing a hit: (damage dealt / level) + weapon bonus, minimum 5. Weapon
bonus = 3.5 x swing time in seconds (main hand), half that for off hand,
halved again if not a crit. Fractions cut.

| Reading, level 10 Warrior, 2.0 s weapon | Gain |
| --------------------------------------- | ---- |
| Main-hand crit for 100 | 100 / 10 + 3.5 x 2.0 = 17 -> +17 |
| Main-hand non-crit for 100 | 10 + 7 x 0.5 = 13.5 -> +13 |
| Off-hand non-crit for 20 | 20 / 10 + (3.5 / 2 x 2.0) x 0.5 = 3.75 -> minimum +5 |

On avoid: dodge, parry, or block = +4 each, no level scaling.

## Energy (vigor) lost in combat (Ranger)

On taking a hit: lose 0.667 x damage / level, minimum 1 per hit, never below
20. Fractions cut toward zero. Dealing damage and avoiding damage change
nothing.

| Reading, level 10 Ranger | Loss |
| ------------------------ | ---- |
| Hit for 60 | 0.667 x 60 / 10 = 4.0 -> -4 |
| Hit for 45 | 0.667 x 45 / 10 = 3.0 -> -3 |
| Hit for 15 at level 60 | 0.667 x 15 / 60 = 0.17 -> minimum -1 |
| Any hit at 20 energy | no change, floor holds |

## Spending and limits

| Reading | Result |
| ------- | ------ |
| Cast costing 10 at 35 power | 25, cast goes through |
| Cast costing 10 at 5 power | fails, "not enough" message, bar unchanged |
| Any gain past max | stops at max (100, or Mana max) |
| Any loss past 0 | stops at 0 |
| Tick or gain timer after spending | keeps running, no delay |

## Worked sequence, level 10 Warrior

| Step | Bar |
| ---- | --- |
| Leave town | 20 |
| Take 45 damage | 20 + 5 = 25 |
| Crit 80 with 2.4 s axe | 25 + 16 = 41 |
| Block next swing | 41 + 4 = 45 |
| Cast costing 10 | 45 - 10 = 35 |
| Fight ends, 1 s ticks | 33, 31, 29 ... 20, stops |
