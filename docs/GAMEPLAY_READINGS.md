# Gameplay readings

A record of what the game shows, used to confirm the values in
`src/game/PlayerStatCurves.cs`, `src/game/LevelingService.cs`,
`src/game/DeathPenaltyService.cs`, `src/game/StartingLoadouts.cs`, the world
timers and the Prison boss scripts (`src/scripts/Prison/`).

Each table lists what the server uses today (**Server**). Readings go in the
empty columns, with where each was read:

- **Source**: `live` for a reading taken in the live game; `video` for one
  read off a video of it.
- **Date**: when the reading was taken.

Fill in only what was actually seen. A blank is better than a guess, and a
reading that disagrees with **Server** is the most useful row on the page.
Once a section has its readings, `docs/PROVENANCE.md` can cite this page for
those values.

The later sections hold other reference material: what
[oac-head](https://oac-head.com/) lists, the owner's description of
unbuilt systems, and facts from the operator's own client data.

## How the numbers are built

These shapes say which readings are enough to settle each table:

- **Base health and power** follow `A·L² + B·L + C` for each class within
  three level bands: 1–13, 14–35 and 36–60. Three readings in a band settle
  that band. The fourth row in each band is a check.
- **Attack power** is linear in level, Strength and Agility for each class.
  Four readings with different gear settle it; a fifth is a check.
- **Strength, Agility, Stamina and Wisdom** each rise by a fixed amount per
  level for each class within the same three bands, rounded down. Reading
  them at every level settles them.
- **XP to the next level** follows a separate curve in each of the same three
  bands.

## Stats by level

Read Strength, Agility, Stamina and Wisdom off the character sheet at every
level, with nothing worn and no buffs. Each class gains the same amount at
each level whatever its race, so a Human of each class covers every pairing:
another race shows its own level 1 values plus the same gains. An Orc Warrior
at level 60, for example, shows 123 Strength, 71 Agility, 107 Stamina and
34 Wisdom.

**Server** adds each class's gain at that level to the race's level 1 values
(`PlayerStatCurves.AttributesAt`, with level 1 from `StartingLoadouts`). It
matches every row below.

### Warrior (class 1)

| Level | Strength | Agility | Stamina | Wisdom | Source | Date |
| ----- | -------- | ------- | ------- | ------ | ------ | ---- |
| 1     | 21       | 12      | 18      | 9      | live   |      |
| 2     | 22       | 12      | 19      | 9      | live   |      |
| 3     | 23       | 13      | 20      | 9      | live   |      |
| 4     | 24       | 14      | 21      | 9      | live   |      |
| 5     | 25       | 14      | 22      | 10     | live   |      |
| 6     | 26       | 15      | 23      | 10     | live   |      |
| 7     | 28       | 16      | 24      | 10     | live   |      |
| 8     | 29       | 16      | 25      | 10     | live   |      |
| 9     | 30       | 17      | 26      | 11     | live   |      |
| 10    | 31       | 18      | 27      | 11     | live   |      |
| 11    | 32       | 18      | 28      | 11     | live   |      |
| 12    | 33       | 19      | 29      | 11     | live   |      |
| 13    | 35       | 20      | 30      | 12     | live   |      |
| 14    | 36       | 20      | 31      | 12     | live   |      |
| 15    | 38       | 21      | 32      | 12     | live   |      |
| 16    | 39       | 22      | 34      | 13     | live   |      |
| 17    | 40       | 23      | 35      | 13     | live   |      |
| 18    | 42       | 24      | 36      | 13     | live   |      |
| 19    | 43       | 25      | 38      | 14     | live   |      |
| 20    | 45       | 26      | 39      | 14     | live   |      |
| 21    | 46       | 27      | 40      | 15     | live   |      |
| 22    | 48       | 28      | 42      | 15     | live   |      |
| 23    | 49       | 29      | 43      | 15     | live   |      |
| 24    | 51       | 30      | 44      | 16     | live   |      |
| 25    | 52       | 30      | 46      | 16     | live   |      |
| 26    | 54       | 31      | 47      | 17     | live   |      |
| 27    | 55       | 32      | 48      | 17     | live   |      |
| 28    | 57       | 33      | 50      | 17     | live   |      |
| 29    | 58       | 34      | 51      | 18     | live   |      |
| 30    | 60       | 35      | 52      | 18     | live   |      |
| 31    | 61       | 36      | 54      | 19     | live   |      |
| 32    | 63       | 37      | 55      | 19     | live   |      |
| 33    | 64       | 38      | 57      | 19     | live   |      |
| 34    | 66       | 39      | 58      | 20     | live   |      |
| 35    | 67       | 40      | 59      | 20     | live   |      |
| 36    | 69       | 41      | 62      | 21     | live   |      |
| 37    | 71       | 42      | 64      | 21     | live   |      |
| 38    | 74       | 43      | 66      | 22     | live   |      |
| 39    | 76       | 45      | 68      | 22     | live   |      |
| 40    | 78       | 46      | 70      | 23     | live   |      |
| 41    | 80       | 47      | 72      | 24     | live   |      |
| 42    | 82       | 49      | 73      | 24     | live   |      |
| 43    | 84       | 50      | 75      | 25     | live   |      |
| 44    | 86       | 51      | 77      | 25     | live   |      |
| 45    | 89       | 52      | 79      | 26     | live   |      |
| 46    | 91       | 54      | 81      | 26     | live   |      |
| 47    | 93       | 55      | 83      | 27     | live   |      |
| 48    | 95       | 56      | 85      | 28     | live   |      |
| 49    | 97       | 58      | 86      | 28     | live   |      |
| 50    | 99       | 59      | 88      | 29     | live   |      |
| 51    | 102      | 60      | 90      | 29     | live   |      |
| 52    | 104      | 61      | 92      | 30     | live   |      |
| 53    | 106      | 63      | 94      | 31     | live   |      |
| 54    | 108      | 64      | 96      | 31     | live   |      |
| 55    | 110      | 65      | 98      | 32     | live   |      |
| 56    | 112      | 67      | 99      | 32     | live   |      |
| 57    | 114      | 68      | 101     | 33     | live   |      |
| 58    | 117      | 69      | 103     | 33     | live   |      |
| 59    | 119      | 71      | 105     | 34     | live   |      |
| 60    | 121      | 72      | 107     | 35     | live   |      |

### Mage (class 2)

| Level | Strength | Agility | Stamina | Wisdom | Source | Date |
| ----- | -------- | ------- | ------- | ------ | ------ | ---- |
| 1     | 10       | 10      | 15      | 25     | live   |      |
| 2     | 10       | 10      | 15      | 26     | live   |      |
| 3     | 10       | 10      | 15      | 27     | live   |      |
| 4     | 10       | 10      | 16      | 28     | live   |      |
| 5     | 10       | 10      | 16      | 29     | live   |      |
| 6     | 10       | 10      | 16      | 31     | live   |      |
| 7     | 10       | 11      | 17      | 32     | live   |      |
| 8     | 10       | 11      | 17      | 33     | live   |      |
| 9     | 10       | 11      | 17      | 34     | live   |      |
| 10    | 10       | 11      | 18      | 35     | live   |      |
| 11    | 10       | 11      | 18      | 37     | live   |      |
| 12    | 10       | 11      | 18      | 38     | live   |      |
| 13    | 10       | 12      | 19      | 39     | live   |      |
| 14    | 11       | 12      | 19      | 40     | live   |      |
| 15    | 11       | 12      | 20      | 42     | live   |      |
| 16    | 11       | 12      | 20      | 44     | live   |      |
| 17    | 11       | 13      | 21      | 45     | live   |      |
| 18    | 11       | 13      | 21      | 47     | live   |      |
| 19    | 11       | 13      | 21      | 48     | live   |      |
| 20    | 11       | 13      | 22      | 50     | live   |      |
| 21    | 12       | 14      | 22      | 51     | live   |      |
| 22    | 12       | 14      | 23      | 53     | live   |      |
| 23    | 12       | 14      | 23      | 55     | live   |      |
| 24    | 12       | 14      | 24      | 56     | live   |      |
| 25    | 12       | 15      | 24      | 58     | live   |      |
| 26    | 12       | 15      | 25      | 59     | live   |      |
| 27    | 12       | 15      | 25      | 61     | live   |      |
| 28    | 12       | 15      | 25      | 62     | live   |      |
| 29    | 13       | 16      | 26      | 64     | live   |      |
| 30    | 13       | 16      | 26      | 66     | live   |      |
| 31    | 13       | 16      | 27      | 67     | live   |      |
| 32    | 13       | 16      | 27      | 69     | live   |      |
| 33    | 13       | 17      | 28      | 70     | live   |      |
| 34    | 13       | 17      | 28      | 72     | live   |      |
| 35    | 13       | 17      | 29      | 73     | live   |      |
| 36    | 14       | 18      | 30      | 77     | live   |      |
| 37    | 14       | 18      | 30      | 79     | live   |      |
| 38    | 14       | 19      | 31      | 81     | live   |      |
| 39    | 14       | 19      | 31      | 84     | live   |      |
| 40    | 15       | 20      | 32      | 86     | live   |      |
| 41    | 15       | 20      | 33      | 88     | live   |      |
| 42    | 15       | 20      | 33      | 90     | live   |      |
| 43    | 15       | 21      | 34      | 92     | live   |      |
| 44    | 16       | 21      | 34      | 94     | live   |      |
| 45    | 16       | 22      | 35      | 96     | live   |      |
| 46    | 16       | 22      | 36      | 98     | live   |      |
| 47    | 16       | 23      | 36      | 101    | live   |      |
| 48    | 17       | 23      | 37      | 103    | live   |      |
| 49    | 17       | 23      | 37      | 105    | live   |      |
| 50    | 17       | 24      | 38      | 107    | live   |      |
| 51    | 17       | 24      | 39      | 109    | live   |      |
| 52    | 18       | 25      | 39      | 111    | live   |      |
| 53    | 18       | 25      | 40      | 113    | live   |      |
| 54    | 18       | 26      | 40      | 115    | live   |      |
| 55    | 18       | 26      | 41      | 118    | live   |      |
| 56    | 19       | 26      | 42      | 120    | live   |      |
| 57    | 19       | 27      | 42      | 122    | live   |      |
| 58    | 19       | 27      | 43      | 124    | live   |      |
| 59    | 19       | 28      | 44      | 126    | live   |      |
| 60    | 20       | 28      | 44      | 128    | live   |      |

### Ranger (class 3)

| Level | Strength | Agility | Stamina | Wisdom | Source | Date |
| ----- | -------- | ------- | ------- | ------ | ------ | ---- |
| 1     | 13       | 22      | 18      | 10     | live   |      |
| 2     | 13       | 23      | 18      | 10     | live   |      |
| 3     | 13       | 24      | 19      | 10     | live   |      |
| 4     | 13       | 25      | 20      | 11     | live   |      |
| 5     | 14       | 26      | 20      | 11     | live   |      |
| 6     | 14       | 27      | 21      | 11     | live   |      |
| 7     | 14       | 28      | 22      | 12     | live   |      |
| 8     | 15       | 29      | 22      | 12     | live   |      |
| 9     | 15       | 31      | 23      | 12     | live   |      |
| 10    | 15       | 32      | 24      | 13     | live   |      |
| 11    | 16       | 33      | 24      | 13     | live   |      |
| 12    | 16       | 34      | 25      | 14     | live   |      |
| 13    | 16       | 35      | 26      | 14     | live   |      |
| 14    | 17       | 37      | 27      | 15     | live   |      |
| 15    | 18       | 38      | 28      | 15     | live   |      |
| 16    | 19       | 40      | 29      | 16     | live   |      |
| 17    | 19       | 41      | 30      | 16     | live   |      |
| 18    | 20       | 43      | 31      | 17     | live   |      |
| 19    | 21       | 44      | 32      | 17     | live   |      |
| 20    | 21       | 46      | 33      | 17     | live   |      |
| 21    | 22       | 47      | 34      | 18     | live   |      |
| 22    | 23       | 49      | 35      | 18     | live   |      |
| 23    | 23       | 50      | 36      | 19     | live   |      |
| 24    | 24       | 52      | 37      | 19     | live   |      |
| 25    | 25       | 53      | 38      | 20     | live   |      |
| 26    | 25       | 55      | 39      | 20     | live   |      |
| 27    | 26       | 56      | 40      | 21     | live   |      |
| 28    | 27       | 58      | 41      | 21     | live   |      |
| 29    | 28       | 60      | 42      | 22     | live   |      |
| 30    | 28       | 61      | 43      | 22     | live   |      |
| 31    | 29       | 63      | 44      | 22     | live   |      |
| 32    | 30       | 64      | 45      | 23     | live   |      |
| 33    | 30       | 66      | 47      | 23     | live   |      |
| 34    | 31       | 67      | 48      | 24     | live   |      |
| 35    | 32       | 69      | 49      | 24     | live   |      |
| 36    | 33       | 71      | 50      | 25     | live   |      |
| 37    | 34       | 73      | 52      | 26     | live   |      |
| 38    | 35       | 75      | 54      | 27     | live   |      |
| 39    | 37       | 78      | 55      | 27     | live   |      |
| 40    | 38       | 80      | 57      | 28     | live   |      |
| 41    | 39       | 82      | 58      | 29     | live   |      |
| 42    | 41       | 84      | 60      | 30     | live   |      |
| 43    | 42       | 86      | 62      | 31     | live   |      |
| 44    | 43       | 89      | 63      | 31     | live   |      |
| 45    | 44       | 91      | 65      | 32     | live   |      |
| 46    | 46       | 93      | 66      | 33     | live   |      |
| 47    | 47       | 95      | 68      | 34     | live   |      |
| 48    | 48       | 98      | 70      | 34     | live   |      |
| 49    | 50       | 100     | 71      | 35     | live   |      |
| 50    | 51       | 102     | 73      | 36     | live   |      |
| 51    | 52       | 104     | 75      | 37     | live   |      |
| 52    | 53       | 107     | 76      | 38     | live   |      |
| 53    | 55       | 109     | 78      | 38     | live   |      |
| 54    | 56       | 111     | 79      | 39     | live   |      |
| 55    | 57       | 113     | 81      | 40     | live   |      |
| 56    | 59       | 115     | 83      | 41     | live   |      |
| 57    | 60       | 118     | 84      | 42     | live   |      |
| 58    | 61       | 120     | 86      | 42     | live   |      |
| 59    | 62       | 122     | 88      | 43     | live   |      |
| 60    | 64       | 124     | 89      | 44     | live   |      |

### Monk (class 4)

| Level | Strength | Agility | Stamina | Wisdom | Source | Date |
| ----- | -------- | ------- | ------- | ------ | ------ | ---- |
| 1     | 14       | 11      | 20      | 20     | live   |      |
| 2     | 14       | 11      | 20      | 20     | live   |      |
| 3     | 15       | 11      | 21      | 21     | live   |      |
| 4     | 15       | 12      | 21      | 22     | live   |      |
| 5     | 16       | 12      | 22      | 23     | live   |      |
| 6     | 16       | 12      | 22      | 24     | live   |      |
| 7     | 17       | 13      | 23      | 25     | live   |      |
| 8     | 17       | 13      | 24      | 26     | live   |      |
| 9     | 18       | 13      | 24      | 27     | live   |      |
| 10    | 18       | 14      | 25      | 28     | live   |      |
| 11    | 19       | 14      | 25      | 29     | live   |      |
| 12    | 19       | 14      | 26      | 30     | live   |      |
| 13    | 20       | 15      | 26      | 31     | live   |      |
| 14    | 21       | 16      | 27      | 32     | live   |      |
| 15    | 22       | 17      | 28      | 33     | live   |      |
| 16    | 23       | 17      | 29      | 35     | live   |      |
| 17    | 24       | 18      | 30      | 36     | live   |      |
| 18    | 25       | 19      | 31      | 37     | live   |      |
| 19    | 26       | 20      | 32      | 39     | live   |      |
| 20    | 27       | 21      | 33      | 40     | live   |      |
| 21    | 28       | 22      | 34      | 41     | live   |      |
| 22    | 28       | 23      | 35      | 43     | live   |      |
| 23    | 29       | 24      | 36      | 44     | live   |      |
| 24    | 30       | 25      | 36      | 45     | live   |      |
| 25    | 31       | 25      | 37      | 47     | live   |      |
| 26    | 32       | 26      | 38      | 48     | live   |      |
| 27    | 33       | 27      | 39      | 49     | live   |      |
| 28    | 34       | 28      | 40      | 50     | live   |      |
| 29    | 35       | 29      | 41      | 52     | live   |      |
| 30    | 36       | 30      | 42      | 53     | live   |      |
| 31    | 37       | 31      | 43      | 54     | live   |      |
| 32    | 38       | 32      | 44      | 56     | live   |      |
| 33    | 39       | 32      | 45      | 57     | live   |      |
| 34    | 40       | 33      | 46      | 58     | live   |      |
| 35    | 41       | 34      | 46      | 60     | live   |      |
| 36    | 42       | 36      | 48      | 62     | live   |      |
| 37    | 44       | 37      | 50      | 64     | live   |      |
| 38    | 46       | 39      | 51      | 66     | live   |      |
| 39    | 48       | 40      | 53      | 68     | live   |      |
| 40    | 49       | 42      | 54      | 70     | live   |      |
| 41    | 51       | 43      | 56      | 72     | live   |      |
| 42    | 53       | 45      | 57      | 73     | live   |      |
| 43    | 55       | 47      | 59      | 75     | live   |      |
| 44    | 57       | 48      | 60      | 77     | live   |      |
| 45    | 58       | 50      | 62      | 79     | live   |      |
| 46    | 60       | 51      | 63      | 81     | live   |      |
| 47    | 62       | 53      | 65      | 83     | live   |      |
| 48    | 64       | 54      | 66      | 85     | live   |      |
| 49    | 65       | 56      | 68      | 87     | live   |      |
| 50    | 67       | 57      | 69      | 89     | live   |      |
| 51    | 69       | 59      | 71      | 91     | live   |      |
| 52    | 71       | 60      | 72      | 93     | live   |      |
| 53    | 72       | 62      | 74      | 95     | live   |      |
| 54    | 74       | 63      | 76      | 97     | live   |      |
| 55    | 76       | 65      | 77      | 99     | live   |      |
| 56    | 78       | 67      | 79      | 101    | live   |      |
| 57    | 79       | 68      | 80      | 103    | live   |      |
| 58    | 81       | 70      | 82      | 105    | live   |      |
| 59    | 83       | 71      | 83      | 107    | live   |      |
| 60    | 85       | 73      | 85      | 109    | live   |      |

## Base health

Read with nothing worn that adds Stamina or health, and no buffs. The
character sheet gives Max HP and Stamina.
**Base HP = Max HP − 10 × Stamina** (`CombatService.HealthPerStamina`).

**Server** is the class curve plus a flat 10 health every character has
(`PlayerStatCurves.StartingHealth`), which the readings show at every level
for every class. The Stamina shown is the one in "Stats by level", so the
server's Max HP matches the **Max HP shown** column as well.

### Warrior (class 1)

| Level | Server base HP | Max HP shown | Stamina shown | Base HP | Source | Date |
| ----- | -------------- | ------------ | ------------- | ------- | ------ | ---- |
| 2     | 30             | 220          | 19            | 30      | live   |      |
| 5     | 87             | 307          | 22            | 87      | live   |      |
| 9     | 163            | 423          | 26            | 163     | live   |      |
| 13    | 239            | 539          | 30            | 239     | live   |      |
| 14    | 259            | 569          | 31            | 259     | live   |      |
| 20    | 421            | 811          | 39            | 421     | live   |      |
| 28    | 749            | 1249         | 50            | 749     | live   |      |
| 35    | 1141           | 1731         | 59            | 1141    | live   |      |
| 36    | 1183           | 1803         | 62            | 1183    | live   |      |
| 44    | 1663           | 2433         | 77            | 1663    | live   |      |
| 52    | 2399           | 3319         | 92            | 2399    | live   |      |
| 60    | 3391           | 4461         | 107           | 3391    | live   |      |

### Mage (class 2)

| Level | Server base HP | Max HP shown | Stamina shown | Base HP | Source | Date |
| ----- | -------------- | ------------ | ------------- | ------- | ------ | ---- |
| 2     | 26             | 176          | 15            | 26      | live   |      |
| 5     | 71             | 231          | 16            | 71      | live   |      |
| 9     | 131            | 301          | 17            | 131     | live   |      |
| 13    | 191            | 381          | 19            | 191     | live   |      |
| 14    | 197            | 387          | 19            | 197     | live   |      |
| 20    | 275            | 495          | 22            | 275     | live   |      |
| 28    | 491            | 741          | 25            | 491     | live   |      |
| 35    | 785            | 1075         | 29            | 785     | live   |      |
| 36    | 813            | 1113         | 30            | 813     | live   |      |
| 44    | 1109           | 1449         | 34            | 1109    | live   |      |
| 52    | 1533           | 1923         | 39            | 1533    | live   |      |
| 60    | 2085           | 2525         | 44            | 2085    | live   |      |

### Ranger (class 3)

| Level | Server base HP | Max HP shown | Stamina shown | Base HP | Source | Date |
| ----- | -------------- | ------------ | ------------- | ------- | ------ | ---- |
| 2     | 28             | 208          | 18            | 28      | live   |      |
| 5     | 79             | 279          | 20            | 79      | live   |      |
| 9     | 147            | 377          | 23            | 147     | live   |      |
| 13    | 215            | 475          | 26            | 215     | live   |      |
| 14    | 233            | 503          | 27            | 233     | live   |      |
| 20    | 383            | 713          | 33            | 383     | live   |      |
| 28    | 695            | 1105         | 41            | 695     | live   |      |
| 35    | 1073           | 1563         | 49            | 1073    | live   |      |
| 36    | 1113           | 1613         | 50            | 1113    | live   |      |
| 44    | 1505           | 2135         | 63            | 1505    | live   |      |
| 52    | 2025           | 2785         | 76            | 2025    | live   |      |
| 60    | 2673           | 3563         | 89            | 2673    | live   |      |

### Monk (class 4)

| Level | Server base HP | Max HP shown | Stamina shown | Base HP | Source | Date |
| ----- | -------------- | ------------ | ------------- | ------- | ------ | ---- |
| 2     | 29             | 229          | 20            | 29      | live   |      |
| 5     | 83             | 303          | 22            | 83      | live   |      |
| 9     | 155            | 395          | 24            | 155     | live   |      |
| 13    | 227            | 487          | 26            | 227     | live   |      |
| 14    | 243            | 513          | 27            | 243     | live   |      |
| 20    | 381            | 711          | 33            | 381     | live   |      |
| 28    | 677            | 1077         | 40            | 677     | live   |      |
| 35    | 1041           | 1501         | 46            | 1041    | live   |      |
| 36    | 1081           | 1561         | 48            | 1081    | live   |      |
| 44    | 1473           | 2073         | 60            | 1473    | live   |      |
| 52    | 1993           | 2713         | 72            | 1993    | live   |      |
| 60    | 2641           | 3491         | 85            | 2641    | live   |      |

## Base power (Mage and Monk)

Warrior and Ranger have no pool that grows with level. Read with nothing worn
that adds Wisdom or mana, and no buffs.
**Base power = Max mana − 15 × Wisdom** (`SpellService.ManaPerWisdom`).

**Server** is the class curve plus a flat 100 mana every Mage and Monk has
(`PlayerStatCurves.StartingMana`), which the readings show at every level.
The Wisdom shown is the one in "Stats by level". Every point of it is worth
15 mana, including the points gained by levelling, so the server gives the
same 15 for Wisdom from gear and buffs.

Warrior (morale) and Ranger (vigor) show a flat 100 at every level, with or
without Wisdom worn (`SpellService.PlayerMaxPower`).

### Mage (class 2)

| Level | Server base mana | Max mana shown | Wisdom shown | Base mana | Source | Date |
| ----- | ---------------- | -------------- | ------------ | --------- | ------ | ---- |
| 2     | 123              | 513            | 26           | 123       | live   |      |
| 5     | 201              | 636            | 29           | 201       | live   |      |
| 9     | 333              | 843            | 34           | 333       | live   |      |
| 13    | 497              | 1082           | 39           | 497       | live   |      |
| 14    | 534              | 1134           | 40           | 534       | live   |      |
| 20    | 798              | 1548           | 50           | 798       | live   |      |
| 28    | 1262             | 2192           | 62           | 1262      | live   |      |
| 35    | 1773             | 2868           | 73           | 1773      | live   |      |
| 36    | 1834             | 2989           | 77           | 1834      | live   |      |
| 44    | 2394             | 3804           | 94           | 2394      | live   |      |
| 52    | 3082             | 4747           | 111          | 3082      | live   |      |
| 60    | 3898             | 5818           | 128          | 3898      | live   |      |

### Monk (class 4)

| Level | Server base mana | Max mana shown | Wisdom shown | Base mana | Source | Date |
| ----- | ---------------- | -------------- | ------------ | --------- | ------ | ---- |
| 2     | 120              | 420            | 20           | 120       | live   |      |
| 5     | 189              | 534            | 23           | 189       | live   |      |
| 9     | 309              | 714            | 27           | 309       | live   |      |
| 13    | 461              | 926            | 31           | 461       | live   |      |
| 14    | 493              | 973            | 32           | 493       | live   |      |
| 20    | 727              | 1327           | 40           | 727       | live   |      |
| 28    | 1151             | 1901           | 50           | 1151      | live   |      |
| 35    | 1627             | 2527           | 60           | 1627      | live   |      |
| 36    | 1683             | 2613           | 62           | 1683      | live   |      |
| 44    | 2203             | 3358           | 77           | 2203      | live   |      |
| 52    | 2851             | 4246           | 93           | 2851      | live   |      |
| 60    | 3627             | 5262           | 109          | 3627      | live   |      |

To confirm Warrior and Ranger have no level-based pool, read their maximum at
two different levels:

| Class   | Level | Max power shown | Source | Date |
| ------- | ----- | --------------- | ------ | ---- |
| Warrior | 10    | 100             | live   |      |
| Warrior | 40    | 100             | live   |      |
| Ranger  | 10    | 100             | live   |      |
| Ranger  | 40    | 100             | live   |      |

Wisdom worn does not raise either pool:

| Class   | Level | Wisdom from gear | Max power shown | Source | Date |
| ------- | ----- | ---------------- | --------------- | ------ | ---- |
| Warrior | 40    | 20               | 100             | live   |      |
| Ranger  | 40    | 20               | 100             | live   |      |

## Health regained out of combat

Out of combat, health comes back every 8 seconds
(`CombatService.RegenInterval`). Each tick gives back:

**Regained = ⌊Stamina × factor + constant⌋ + health regeneration rating**
(`CombatService.RegenAmount`), where Stamina is the whole figure on the
character sheet, gear included. Power has its own clock and is not covered
here.

| Class   | Factor | Constant |
| ------- | ------ | -------- |
| Warrior | 2.0    | 9        |
| Mage    | 1.2    | 6        |
| Ranger  | 1.8    | 2        |
| Monk    | 1.5    | 5        |

Read with nothing worn that adds Stamina or regeneration, and no buffs:
take some damage, step out of the fight, and note how much one tick gives
back. **Server** uses the Stamina from "Stats by level".

### Warrior (class 1)

| Level | Stamina | Server regained | Regained shown | Source | Date |
| ----- | ------- | --------------- | -------------- | ------ | ---- |
| 1     | 18      | 45              | 45             | live       | 2026-10-06 |
| 10    | 27      | 63              | 63             | live       | 2026-10-06 |
| 20    | 39      | 87              | 87             | live       | 2026-10-06 |
| 30    | 52      | 113             | 113            | live       | 2026-10-06 |
| 40    | 70      | 149             | 149            | live       | 2026-10-06 |
| 50    | 88      | 185             | 185            | live       | 2026-10-06 |
| 60    | 107     | 223             | 223            | live       | 2026-10-06 |

### Mage (class 2)

| Level | Stamina | Server regained | Regained shown | Source | Date |
| ----- | ------- | --------------- | -------------- | ------ | ---- |
| 1     | 15      | 24              | 24             | live       | 2026-10-06 |
| 10    | 18      | 27              | 27             | live       | 2026-10-06 |
| 20    | 22      | 32              | 32             | live       | 2026-10-06 |
| 30    | 26      | 37              | 37             | live       | 2026-10-06 |
| 40    | 32      | 44              | 44             | live       | 2026-10-06 |
| 50    | 38      | 51              | 51             | live       | 2026-10-06 |
| 60    | 44      | 58              | 58             | live       | 2026-10-06 |

### Ranger (class 3)

| Level | Stamina | Server regained | Regained shown | Source | Date |
| ----- | ------- | --------------- | -------------- | ------ | ---- |
| 1     | 18      | 34              | 34             | live       | 2026-10-06 |
| 10    | 24      | 45              | 45             | live       | 2026-10-06 |
| 20    | 33      | 61              | 61             | live       | 2026-10-06 |
| 30    | 43      | 79              | 79             | live       | 2026-10-06 |
| 40    | 57      | 104             | 104            | live       | 2026-10-06 |
| 50    | 73      | 133             | 133            | live       | 2026-10-06 |
| 60    | 89      | 162             | 162            | live       | 2026-10-06 |

### Monk (class 4)

| Level | Stamina | Server regained | Regained shown | Source | Date |
| ----- | ------- | --------------- | -------------- | ------ | ---- |
| 1     | 20      | 35              | 35             | live       | 2026-10-06 |
| 10    | 25      | 42              | 42             | live       | 2026-10-06 |
| 20    | 33      | 54              | 54             | live       | 2026-10-06 |
| 30    | 42      | 68              | 68             | live       | 2026-10-06 |
| 40    | 54      | 86              | 86             | live       | 2026-10-06 |
| 50    | 69      | 108             | 108            | live       | 2026-10-06 |
| 60    | 85      | 132             | 132            | live       | 2026-10-06 |

## Attack power

Read Level, Strength, Agility and Attack Power off the character sheet. The
same character in different gear gives different Strength and Agility at one
level, which is what the rows need. **Server** is what the code gives for the
row's _shown_ Strength and Agility, so it can only be filled once those are
read. Formulas the server uses:

| Class   | Server attack power                 |
| ------- | ----------------------------------- |
| Warrior | 3 × Level + 2 × Strength − 20       |
| Mage    | 3 × Level + Strength − 6            |
| Ranger  | 2 × Level + Strength + Agility − 20 |
| Monk    | 2 × Level + 2 × Strength − 20       |

All are floored at 0.

The game's character sheet shows all three. This server does not send them
to the client yet, so `ubiquitous-client` cannot show them. The level changes from row
to row, and Strength and Agility grow with level, so the rows vary all three
inputs without gear. Strength and Agility in the formulas are the full values
shown: the character's own plus gear and buffs.

All four classes match **Server** in every row. For Mage and Monk, four rows
fix the four terms and the fifth checks them.

### Warrior

| Level | Strength shown | Agility shown | Attack power shown | Server for these stats | Source | Date |
| ----- | -------------- | ------------- | ------------------ | ---------------------- | ------ | ---- |
| 1     | 21             | 12            | 25                 | 25                     | live   |      |
| 10    | 31             | 18            | 72                 | 72                     | live   |      |
| 20    | 45             | 26            | 130                | 130                    | live   |      |
| 40    | 78             | 46            | 256                | 256                    | live   |      |
| 60    | 121            | 72            | 402                | 402                    | live   |      |

### Mage

| Level | Strength shown | Agility shown | Attack power shown | Server for these stats | Source | Date |
| ----- | -------------- | ------------- | ------------------ | ---------------------- | ------ | ---- |
| 1     | 10             | 10            | 7                  | 7                      | live   |      |
| 10    | 10             | 11            | 34                 | 34                     | live   |      |
| 20    | 11             | 13            | 65                 | 65                     | live   |      |
| 40    | 15             | 20            | 129                | 129                    | live   |      |
| 60    | 20             | 28            | 194                | 194                    | live   |      |

### Ranger

| Level | Strength shown | Agility shown | Attack power shown | Server for these stats | Source | Date |
| ----- | -------------- | ------------- | ------------------ | ---------------------- | ------ | ---- |
| 1     | 13             | 22            | 17                 | 17                     | live   |      |
| 10    | 15             | 32            | 47                 | 47                     | live   |      |
| 20    | 21             | 46            | 87                 | 87                     | live   |      |
| 40    | 38             | 80            | 178                | 178                    | live   |      |
| 60    | 64             | 124           | 288                | 288                    | live   |      |

### Monk

| Level | Strength shown | Agility shown | Attack power shown | Server for these stats | Source | Date |
| ----- | -------------- | ------------- | ------------------ | ---------------------- | ------ | ---- |
| 1     | 14             | 11            | 10                 | 10                     | live   |      |
| 10    | 18             | 14            | 36                 | 36                     | live   |      |
| 20    | 27             | 21            | 74                 | 74                     | live   |      |
| 40    | 49             | 42            | 158                | 158                    | live   |      |
| 60    | 85             | 73            | 270                | 270                    | live   |      |

## XP to the next level

The total on the XP bar at each level. Any level helps. Three per band (1–13,
14–35, 36–59) settle the curve, so the rows marked **★** come first.

| Level | Server XP to next | XP bar total shown | Source     | Date       |
| ----- | ----------------- | ------------------ | ---------- | ---------- |
| 1     | 250               | 250                | live       | 2026-10-03 |
| 2 ★   | 600               | 600                | live       | 2026-10-03 |
| 3     | 1,050             | 1,050              | live       | 2026-10-03 |
| 4     | 1,600             | 1,600              | live       | 2026-10-03 |
| 5     | 2,250             | 2,250              | live       | 2026-10-03 |
| 6     | 3,000             | 3,000              | live       | 2026-10-03 |
| 7 ★   | 3,850             | 3,850              | live       | 2026-10-03 |
| 8     | 4,800             | 4,800              | live       | 2026-10-03 |
| 9     | 5,850             | 5,850              | live       | 2026-10-03 |
| 10    | 7,000             | 7,000              | live       | 2026-10-03 |
| 11    | 8,150             | 8,150              | live       | 2026-10-03 |
| 12 ★  | 9,400             | 9,400              | live       | 2026-10-03 |
| 13    | 10,750            | 10,750             | live       | 2026-10-03 |
| 14    | 12,100            | 12,100             | live       | 2026-10-03 |
| 15 ★  | 13,500            | 13,500             | live       | 2026-10-03 |
| 16    | 14,900            | 14,900             | live       | 2026-10-03 |
| 17    | 16,350            | 16,350             | live       | 2026-10-03 |
| 18    | 17,800            | 17,800             | live       | 2026-10-03 |
| 19    | 19,300            | 19,300             | live       | 2026-10-03 |
| 20    | 20,800            | 20,800             | live       | 2026-10-03 |
| 21    | 22,250            | 22,250             | live       | 2026-10-03 |
| 22    | 23,700            | 23,700             | live       | 2026-10-03 |
| 23    | 25,150            | 25,150             | live       | 2026-10-03 |
| 24    | 26,600            | 26,600             | live       | 2026-10-03 |
| 25 ★  | 27,950            | 27,950             | live       | 2026-10-03 |
| 26    | 29,300            | 29,300             | live       | 2026-10-03 |
| 27    | 30,650            | 30,650             | live       | 2026-10-03 |
| 28    | 31,850            | 31,850             | live       | 2026-10-03 |
| 29    | 33,050            | 33,050             | live       | 2026-10-03 |
| 30    | 34,200            | 34,200             | live       | 2026-10-03 |
| 31    | 41,300            | 41,300             | live       | 2026-10-03 |
| 32    | 48,700            | 48,700             | live       | 2026-10-03 |
| 33    | 56,350            | 56,350             | live       | 2026-10-03 |
| 34 ★  | 64,250            | 64,250             | live       | 2026-10-03 |
| 35    | 72,450            | 72,450             | live       | 2026-10-03 |
| 36    | 80,850            | 80,850             | live       | 2026-10-03 |
| 37 ★  | 89,550            | 89,550             | live       | 2026-10-03 |
| 38    | 98,500            | 98,500             | live       | 2026-10-03 |
| 39    | 107,700           | 107,700            | live       | 2026-10-03 |
| 40    | 117,200           | 117,200            | live       | 2026-10-03 |
| 41    | 126,900           | 126,900            | live       | 2026-10-03 |
| 42    | 136,900           | 136,900            | live       | 2026-10-03 |
| 43    | 147,150           | 147,150            | live       | 2026-10-03 |
| 44    | 157,650           | 157,650            | live       | 2026-10-03 |
| 45    | 168,450           | 168,450            | live       | 2026-10-03 |
| 46    | 179,450           | 179,450            | live       | 2026-10-03 |
| 47    | 190,750           | 190,750            | live       | 2026-10-03 |
| 48 ★  | 202,300           | 202,300            | live       | 2026-10-03 |
| 49    | 214,100           | 214,100            | live       | 2026-10-03 |
| 50    | 226,200           | 226,200            | live       | 2026-10-03 |
| 51    | 238,500           | 238,500            | live       | 2026-10-03 |
| 52    | 251,100           | 251,100            | live       | 2026-10-03 |
| 53    | 263,950           | 263,950            | live       | 2026-10-03 |
| 54    | 277,050           | 277,050            | live       | 2026-10-03 |
| 55    | 290,450           | 290,450            | live       | 2026-10-03 |
| 56    | 304,050           | 304,050            | live       | 2026-10-03 |
| 57    | 317,950           | 317,950            | live       | 2026-10-03 |
| 58 ★  | 332,100           | 332,100            | live       | 2026-10-03 |
| 59    | 346,500           | 346,500            | live       | 2026-10-03 |

## XP for a kill

One kill of a creature at a known level. The creature's level is on its
portrait. **Server** is what the code gives (`LevelingService.KillXp`).

| Player level | Creature level | Server XP | XP shown | Source     | Date       |
| ------------ | -------------- | --------- | -------- | ---------- | ---------- |
| 10           | 10             | 100       | 100      | live       | 2026-10-03 |
| 10           | 12             | 110       | 110      | live       | 2026-10-03 |
| 10           | 15             | 125       | 125      | live       | 2026-10-03 |
| 10           | 25             | 150       | 150      | live       | 2026-10-03 |
| 10           | 9              | 90        | 90       | live       | 2026-10-03 |
| 10           | 8              | 80        | 80       | live       | 2026-10-03 |
| 10           | 7              | 40        | 40       | live       | 2026-10-03 |
| 10           | 5              | 0         | 0        | live       | 2026-10-03 |
| 30           | 30             | 200       | 200      | live       | 2026-10-03 |
| 30           | 27             | 100       | 100      | live       | 2026-10-03 |
| 30           | 25             | 33        | 33       | live       | 2026-10-03 |
| 30           | 24             | 0         | 0        | live       | 2026-10-03 |
| 50           | 50             | 300       | 300      | live       | 2026-10-03 |
| 50           | 40             | 0         | 0        | live       | 2026-10-03 |

## XP for a quest

The quest's own reward XP (shown in its reward panel at an even level) and
what is actually granted at another level. **Server %** is the share the code
grants for that gap (`LevelingService.QuestXp`). A repeatable quest always
grants 100%, and so does a daily quest. The rows use a level 30 player and a
quest listing 1,000 XP, and round the grant down. They match **Server** in
every band.

| Quest level − player level | Server % | Quest                | Reward XP listed | XP granted | Source | Date |
| -------------------------- | -------- | -------------------- | ---------------- | ---------- | ------ | ---- |
| -10                        | 10       | any, player level 30 | 1,000            | 100        | live   |      |
| -9                         | 10       | any, player level 30 | 1,000            | 100        | live   |      |
| -6                         | 50       | any, player level 30 | 1,000            | 500        | live   |      |
| -5                         | 50       | any, player level 30 | 1,000            | 500        | live   |      |
| -3                         | 80       | any, player level 30 | 1,000            | 800        | live   |      |
| -2                         | 80       | any, player level 30 | 1,000            | 800        | live   |      |
| +0                         | 100      | any, player level 30 | 1,000            | 1,000      | live   |      |
| +2                         | 100      | any, player level 30 | 1,000            | 1,000      | live   |      |
| +3                         | 110      | any, player level 30 | 1,000            | 1,100      | live   |      |
| +5                         | 110      | any, player level 30 | 1,000            | 1,100      | live   |      |
| +6                         | 120      | any, player level 30 | 1,000            | 1,200      | live   |      |

## Gold lost on death

Die to a creature and note the gold carried before and after. The
level 10/11 pair and each band edge matter most. Carry more than the loss, or
the loss is capped at what is carried. The rows carry 1g (10000 copper) and
match **Server** in every row. A duel ends with the loser
left at 10 HP rather than dead, so no gold moves.

| Level | Killed by      | Server loss       | Gold before | Gold after | Loss | Source | Date |
| ----- | -------------- | ----------------- | ----------- | ---------- | ---- | ------ | ---- |
| 5     | creature       | none              | 10000       | 10000      | 0    | live   |      |
| 10    | creature       | none              | 10000       | 10000      | 0    | live   |      |
| 11    | creature       | 5s (500 copper)   | 10000       | 9500       | 500  | live   |      |
| 20    | creature       | 5s (500 copper)   | 10000       | 9500       | 500  | live   |      |
| 21    | creature       | 15s (1500 copper) | 10000       | 8500       | 1500 | live   |      |
| 30    | creature       | 15s (1500 copper) | 10000       | 8500       | 1500 | live   |      |
| 31    | creature       | 30s (3000 copper) | 10000       | 7000       | 3000 | live   |      |
| 40    | creature       | 30s (3000 copper) | 10000       | 7000       | 3000 | live   |      |
| 41    | creature       | 50s (5000 copper) | 10000       | 5000       | 5000 | live   |      |
| 50    | creature       | 50s (5000 copper) | 10000       | 5000       | 5000 | live   |      |
| 51    | creature       | 80s (8000 copper) | 10000       | 2000       | 8000 | live   |      |
| 60    | creature       | 80s (8000 copper) | 10000       | 2000       | 8000 | live   |      |
| 30    | another player | none              | 10000       | 10000      | 0    | live   |      |
| 30    | a duel         | none              | 10000       | 10000      | 0    | live   |      |

## A new character: level 1 Human Warrior

Source: `live`, 2026-10-06, for this and every section below except the
Prison bosses.

| Reading | Value |
| ------- | ----- |
| Race | Human |
| Class | Warrior |
| Level | 1 |
| Strength | 21 |
| Agility | 12 |
| Stamina | 18 |
| Wisdom | 9 |
| HP | 191 / 191 |
| Morale | 100 |
| Attack Power | 25 |
| Exp | 0 / 250 |
| HP regained per tick | 45 |
| Seconds between ticks | 8 |
| Start zone | High Forest |

Stats are with the starting kit taken off.

| Equipment slot | Item |
| -------------- | ---- |
| Chest     | Rookie Tunic |
| Legs      | Rookie Pants |
| Feet      | Rookie Boots |
| Main hand | Dull Blade |
| Off hand  | Cracked Buckler |

| Bag | Count |
| --- | ----- |
| Minor Healing Potion | 5 |
| Dinner Bread | 20 |
| Minor Mana Potion | 5 |

| Spellbook |
| --------- |
| Swift Strike |
| Pounding Glow |
| Courage |
| Auto Attack |
| Auto Shoot |
| Switch Target |
| Gathering |
| Gathering |
| Gathering |
| Riding Permit for Humans |

| Action bar | Spell |
| ---------- | ----- |
| 1 | Auto Attack |
| 2 | Swift Strike |

## Level 1, every race and class

With the starting kit taken off.

| Race | Class | Strength | Agility | Stamina | Wisdom | Max HP | Max power |
| ---- | ----- | -------- | ------- | ------- | ------ | ------ | --------- |
| Human | Warrior | 21 | 12 | 18 | 9 | 191 | 100 |
| Human | Mage | 10 | 10 | 15 | 25 | 161 | 476 |
| Human | Ranger | 13 | 22 | 18 | 10 | 191 | 100 |
| Human | Monk | 14 | 11 | 20 | 20 | 211 | 401 |
| Elf | Warrior | 18 | 15 | 16 | 11 | 171 | 100 |
| Elf | Mage | 7 | 13 | 13 | 27 | 141 | 506 |
| Elf | Ranger | 10 | 25 | 16 | 12 | 171 | 100 |
| Elf | Monk | 11 | 14 | 18 | 22 | 191 | 431 |
| Orc | Warrior | 23 | 11 | 18 | 8 | 191 | 100 |
| Orc | Mage | 12 | 9 | 15 | 24 | 161 | 461 |
| Orc | Ranger | 15 | 21 | 18 | 9 | 191 | 100 |
| Orc | Monk | 16 | 10 | 20 | 19 | 211 | 386 |
| Undead | Warrior | 19 | 12 | 17 | 12 | 181 | 100 |
| Undead | Mage | 8 | 10 | 14 | 28 | 151 | 521 |
| Undead | Ranger | 11 | 22 | 17 | 13 | 181 | 100 |
| Undead | Monk | 12 | 11 | 19 | 23 | 201 | 446 |
| Mendel | Warrior | 19 | 16 | 21 | 9 | 221 | 100 |
| Mendel | Mage | 9 | 16 | 16 | 24 | 171 | 461 |
| Mendel | Ranger | 9 | 26 | 16 | 14 | 171 | 100 |
| Mendel | Monk | 15 | 17 | 18 | 15 | 191 | 326 |

Max power is Morale for Warriors, Energy for Rangers and Mana for Mages and
Monks.

## Starting kit

The same for every race.

| Class | Chest | Legs | Feet | Main hand | Off hand | Ranged |
| ----- | ----- | ---- | ---- | --------- | -------- | ------ |
| Warrior | Rookie Tunic | Rookie Pants | Rookie Boots | Dull Blade | Cracked Buckler | — |
| Mage | Rookie Robe | Rookie Skirt | Rookie Slippers | Apprentice Staff | — | — |
| Ranger | Venturer's Jerkin | Venturer's Breeches | Venturer's Boots | Jagged Knife | Jagged Knife | Training Bow |
| Monk | Friar's Vest | Friar's Trousers | Friar's Boots | Long Staff | — | — |

| Bag (every class) | Count |
| ----------------- | ----- |
| Minor Healing Potion | 5 |
| Dinner Bread | 20 |
| Minor Mana Potion | 5 |

## Spellbook

| Class | Spellbook |
| ----- | --------- |
| Warrior | Swift Strike, Pounding Glow, Courage, Auto Attack, Auto Shoot, Switch Target, Gathering ×3 |
| Mage | Fireball, Frost Bolt, Energy Field, Auto Attack, Auto Shoot, Switch Target, Gathering ×3 |
| Ranger | Rapid Shot, Shadow Strike, Sneak, Auto Attack, Auto Shoot, Switch Target, Gathering ×3, split shot primeval, snipe primeval |
| Monk | Arrow of Faith, Rapid Healing, Blessing of Vitality, Auto Attack, Auto Shoot, Switch Target, Gathering ×3 |

| Race | Racial spell |
| ---- | ------------ |
| Human | Riding Permit for Humans |
| Elf | Riding Permit for Elves |
| Orc | Riding Permit for Orcs |
| Undead | Riding Permit for Undead |
| Mendel | — |

## Action bar

| Class | Button 1 | Button 2 |
| ----- | -------- | -------- |
| Warrior | Auto Attack | Swift Strike |
| Mage | Auto Attack | Fireball |
| Ranger | Auto Attack | Rapid Shot |
| Monk | Auto Attack | Arrow of Faith |

## Start positions

Read from the debug HUD, then checked against the rough position in the
original game.

| Race | Start zone | Map | X | Y | Z |
| ---- | ---------- | --- | - | - | - |
| Human | High Forest | 1 | 2 | 7 | 55 |
| Elf | High Forest | 1 | 2 | 7 | 55 |
| Orc | West Land | 7 | -2640 | 33 | 100 |
| Undead | West Land | 7 | -2640 | 33 | 100 |
| Mendel | Under World | 6 | -860 | -6.5 | 20 |

To confirm each start in the original game or a video of it, note what a new
character sees on first login.

| Race | Zone name shown | NPCs in view | Facing (towards what) | Source | Date |
| ---- | --------------- | ------------ | --------------------- | ------ | ---- |
| Human | | | | | |
| Elf | | | | | |
| Orc | | | | | |
| Undead | | | | | |
| Mendel | | | | | |

## World timers

| Timer | Value |
| ----- | ----- |
| Corpse with loot left | 150 s |
| Corpse already looted | 30 s |
| Corpse inside a dungeon | Stays until the dungeon copy ends |
| Dungeon reset after everyone leaves | 15 min |
| Health regen tick | 8 s |
| Stuck-rescue cooldown | 15 min |
| View range | 60 units |

The stuck-rescue cooldown is read from the menu, which shows "this function
can be used in" with a live countdown from 15m. The view range is a rough
check in play. The value is this project's own.

## Prison bosses (Knahswahs Jail)

Source: `video`, 2026-10-06.

The level is on the boss's portrait. Health shows only as a bar, so it is not
read. Legend is chosen by the team leader on the team window's Dungeon page.

| Boss | Mode | Server level | Level shown | Server health | Source | Date |
| ---- | ---- | ------------ | ----------- | ------------- | ------ | ---- |
| Executioner of Knahswahs | Normal | 20 | 15 | 10,000 | video | 2026-10-06 |
| Ariel | Normal | 20 | 15 | 12,000 | video | 2026-10-06 |
| Cosmo | Normal | 21 | 15 | 15,000 | video | 2026-10-06 |
| Executioner of Knahswahs | Legend | 60 | | 100,000 | | |
| Ariel | Legend | 61 | | 120,000 | | |
| Cosmo | Legend | 61 | | 150,000 | | |

The normal-mode rows disagree with **Server**. The server keeps the client
data's levels; the videos are probably from a later version.

[oac-head](https://oac-head.com/) agrees with the videos for Normal and with
the client data for Legend:

| Boss | oac-head Normal | oac-head Legend |
| ---- | --------------- | --------------- |
| Executioner of Knahswahs | level 15, 1,500 HP | level 60, 100,000 HP |
| Ariel | level 15, 1,500 HP | level 61, 120,000 HP |
| Cosmo | level 15, 1,600 HP | level 61, 150,000 HP |

Boss health is read off the target bar, so it is a rough share. Times are from
the pull, and every time here is a rough estimate still to be checked against
video. The boss's spell shows as an icon next to it, which can be tapped
for the spell's name and the time left on the cast.

### Executioner of Knahswahs

| Time into fight | Boss health | What happens | Spell |
| --------------- | ----------- | ------------ | ----- |
| every ~3 s in melee | any | Heavy melee hit on the tank | Reaping |
| ~9 s | any | Boss covers itself | Osseous Blade Armor |
| ~26 s | any | Boss spins, hitting everyone near it | Whirlwind |
| every ~17 s after | any | Armour and spin take turns | Osseous Blade Armor, Whirlwind |
| while covered (~11 s) | any | Heavy melee hits stop | — |

Spell list: Osseous Blade Armor, Reaping, Whirlwind.

| When | Yell |
| ---- | ---- |
| Casting Whirlwind | Cyclone Slash! |

### Ariel

| Time into fight | Boss health | What happens | Spell |
| --------------- | ----------- | ------------ | ----- |
| from the pull | any | Steady damage to the whole party | Putrefying |
| ~6 s, then every ~15 s | any | A random party member is stunned for 3 s | Decayed Seed |
| — | about a tenth | Starts a 20 s cast | Detonation |
| when the cast finishes | — | The whole party dies if she is still alive | Detonation |

Spell list: Decayed Seed, Detonation, Putrefying.

Ariel doesn't yell.

### Cosmo

| Time into fight | Boss health | What happens | Spell |
| --------------- | ----------- | ------------ | ----- |
| from the pull, ~5 s between each | full to about 70% | Curses the target, in turn | Thunder Curse, Cold Curse, Flame Curse |
| — | — | Shields himself | Shadow Shield |
| — | about 70% | Changes into a demon and fights in melee | Demon Metamorphosis |
| ~5 s after the change, then every ~10 s | about 70% to 25% | Shadow damage around him | Shadow Erode |
| every ~15 s | about 70% to 25% | An Undead Puppet climbs out of the floor | — |
| when a puppet is beside him | about 70% to 25% | Eats the puppet and his health bar jumps up | Devour Puppet |
| — | about 25% | Changes back and curses again | — |

Spell list: Cold Curse, Demon Metamorphosis, Devour Puppet, Flame Curse,
Shadow Erode, Shadow Shield, Thunder Curse.

| When | Yell |
| ---- | ---- |
| Changing into a demon | The demons stir within me! |
| Summoning an Undead Puppet | The dead fight for ME! |
| Eating a puppet | Give me your power! |

The spell lists are from oac-head.com
([Executioner](https://oac-head.com/creatures/4/spells/),
[Ariel](https://oac-head.com/creatures/5/spells/),
[Cosmo](https://oac-head.com/creatures/6/spells/)).

### Boss spells in the game data

Every spell the bosses use is a row in the game's own spell table, read from
the user's IPA. A spell with an icon there shows beside the boss when cast; a
spell with none never shows.

Cosmo's heal after eating a puppet has an icon, and it carries the same name
as the eat itself, so a player sees "Devour Puppet" both times. Its
description reads "Restores your health by 2,000 HP", but the spell's own
effect heals 4,000 in normal mode and 40,000 in Legend, and the heal is
whatever the effect says. How much of the bar comes back is the reading
that tells which one the live game used.

| Boss | When | Spell | Icon in the IPA | Seen in play | Name shown | Source | Date |
| ---- | ---- | ----- | --------------- | ------------ | ---------- | ------ | ---- |
| Cosmo | just after he eats a puppet, as his health bar jumps | Devour Puppet (the heal) | yes | | | | |

Two spells end another spell and have no icon, so a player never sees them
cast. What a player sees is the other spell ending. Each one's effect in the
IPA is to remove the other spell's aura, so they are its inverse.

| Boss | Spell (IPA name) | Icon in the IPA | Removes | What a player sees | When | Seen in play | Source | Date |
| ---- | ---------------- | --------------- | ------- | ------------------ | ---- | ------------ | ------ | ---- |
| Ariel | Remove Putrefying | none | Putrefying | The steady damage to the party stops, and the Putrefying icon goes | about a tenth of her health, as the long cast starts | | | |
| Cosmo | Remove Demon Metamorphosis | none | Demon Metamorphosis | He changes back from the demon shape | about 25% | | | |

### Spells not yet cast

Each boss's spells are one group in the game's spell table, so these are
known to be the boss's own. When each is cast is not in the data. Write down
when it shows: the time from the pull, or the share of the boss's health
bar, or what it follows.

| Boss | Spell | Shows in play as | When (rough) | Source | Date |
| ---- | ----- | ---------------- | ------------ | ------ | ---- |
| Executioner of Knahswahs | Osseous Blade Armor (second) | the same icon and name as the first | | | |
| Ariel | Putrefying | its own icon; steady damage to the whole party | | | |
| Ariel | Detonation (second, instant) | the Detonation icon again, after the long cast | | | |
| Cosmo | Shadow Shield | its own icon on Cosmo | | | |
| Cosmo | Remove All | no icon; Shadow Shield and the demon shape end together | | | |

## Legend mode

Source: the owner's description of the live game, not yet confirmed by a
reading. None of it is built.

- Legend dungeons are for level 60 and up.
- A 100-point exhaustion bar fills with each boss kill. A full bar bars Legend
  dungeons until the daily reset at 03:00 GMT, and using a portal inside with
  a full bar puts the player out.
- An Energy Tablet item clears the bar.
- The chosen mode shows on the team window's Dungeon page for every member.

## The prison in the client data

Read from the operator's own tables, build 1.1.4.

| Fact | Value |
| ---- | ----- |
| Spawns on map 14 | 95 |
| Respawn timer of a prison creature | ten hours |
| Authored spawns standing more than two units off the terrain | about a third |
| Highest spawn above the terrain beneath it | about twenty units |
| Distance between the inside dungeon-gate pair | seven units |

The ten-hour timer is why nothing respawns within one run, and the heights are
why a dungeon keeps authored heights rather than snapping to terrain.

## Greenmont patrols

Greenmont gets its own readings because its patrols are among the most
visible in the game: guards and townsfolk walking the capital's streets
that every player passes. The creatures that wander a patch of ground
elsewhere are far less noticed, and walk generated loops for now.

How a route is read: find the walker in a video of the live game, or on a
live server, and note where it stops and for how long. Then open the patrol
editor in `ubiquitous-client` (admin panel), stand a character on each stop in turn,
facing the way the walker faces, and press "Add stop here"; type each pause
from a stopwatch. Save the file into `content/patrol-routes/map-13.json`.
The file holds the stops; this table says where and when each route was
read.

The routes and walkers are the ones the user's own spawn table marks as
patrolling on map 13.

| Route | Walker | Stops recorded | Loops or turns back | Source | Date |
| ----- | ------ | -------------- | ------------------- | ------ | ---- |
| 4541 | Orc Guard | | | | |
| 4549 | Orc Guard | | | | |
| 4593 | Undead Guard | | | | |
| 4613 | Human Guard | | | | |
| 4623 | Elf Guard | | | | |
| 4631 | Elf Guard | | | | |
| 4639 | Guard | | | | |
| 4653 | Guard | | | | |
| 4663 | Undead Guard | | | | |
| 4670 | Gevia | | | | |
| 4679 | Gross Foresttrace | | | | |
| 4686 | Roulv Giantstun | | | | |
| 4749 | Guard | | | | |
| 4762 | Guard | | | | |
| 4918 | Madline | | | | |
| 4952 | Pochah Stonehorn | | | | |
| 4980 | Elerrie | | | | |
| 4998 | Fabrena | | | | |
| 5039 | Harmien Daran | | | | |
| 5051 | Solomon Harmk | | | | |
| 5091 | Babara | | | | |
| 5099 | Bellesie | | | | |
| 5331 | Vivian Narrow | | | | |
| 5347 | Guard | | | | |
| 5361 | Guard | | | | |
| 5377 | Orc Guard | | | | |
| 5389 | Falimi | | | | |
| 5787 | Harriney | | | | |
| 6257 | Association Member | | | | |
