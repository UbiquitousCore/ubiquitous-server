# Gameplay Field Guide

*How the game behaves in play, written from a player's point of view. It describes build 1.1.4, and calls out 1.0.3 where the two differ. Companion to the [Dungeon Field Guide](dungeon-field-guide.md), which covers the bosses.*

Timings and numbers are what you would see on screen or time with a stopwatch. A "range" in metres is measured from the creature or player named, the same way spell ranges are shown in the spell book.

Notes marked **◆ Legendary** describe how Legendary mode differs; **Tip** and **Warning** call out what to do and what to avoid.

## Contents

1. [Dungeon runs](#dungeon-runs)
2. [The world](#the-world)
3. [Mounts](#mounts)
4. [Parties: experience, money and loot](#parties-experience-money-and-loot)
5. [Interface](#interface)
6. [Chat](#chat)

## Dungeon runs

### Your copy of a dungeon

- Each dungeon is a private copy. On your own you get a copy of your own; in a party, everyone goes into the same copy. Up to 4 players fit.
- Normal and Legendary use **the same entrance**. The **Dungeon** button on the toolbar sets the mode (**Normal** or **Legend**), and the game confirms it: "Current Dungeon mode is Legend". The two modes are separate copies, so switching mode gives you a fresh copy.
- The same menu has **Reset All Dungeons**, which resets your Normal copies ("Are you sure you want to reset all normal dungeons?"). It fails while anyone is still inside: "Reset failed. Players were still in the dungeon."
- Leave and come back while the copy is still open and you return to the same copy, with what you killed still dead.
- **An empty copy closes 15 minutes after the last player leaves.** Come back after that and the dungeon has reset.
- Monsters inside a dungeon don't respawn during a run. Their respawn timers are measured in hours, so in practice a cleared room stays clear until the copy closes.

### Corpses

- A body with loot on it stays for **2½ minutes**. It goes a few seconds after the last item is taken (about 2 seconds for most dungeon monsters).
- A body with nothing on it fades after about **30 seconds**.
- While a loot roll on a body is still running, the body stays until the roll ends.

### ◆ Legendary: Exhaustion

- Legendary copies wear you down. Every 45 seconds you spend inside, your **Exhaustion** rises by 1, and each boss kill adds more on top. In Knahswahs Jail the Executioner and Ariel add 5 each and Cosmo adds 8. In Relic's Key, Hathraad and Kermode add 4 each and Alr-Rahjil adds 9. It caps at 100.
- Exhaustion is shown on your character sheet as "Exhaustion: 12 / 100". It **empties once a day** at the daily reset.

## The world

### Where each race starts

| Race | Map | Starting area |
| --- | --- | --- |
| Human, Elf | High Forest | **Bolderwood**, in Arcadian Forest |
| Orc, Undead | West Land | **Roar Military School**, in Tanned Land |
| Mendel | — | Not playable in 1.1.4 (the race button has no name and the Under World has no zone names yet) |

### Monsters: when they attack and how far they reach

**When a monster comes after you**

- A hostile monster attacks when you **come into its sight** or **when you hit it**. Monsters that aren't hostile only fight back.
- Sight is short: about **8 metres** for an ordinary monster at your level. It grows by 5% for every level the monster has over you and shrinks by 5% for every level it's below you. Ranged monsters see a little further (×1.1), and tougher ranks further still (×1.2 and ×1.3).
- However the numbers fall, a monster always notices you within **3 metres** and never from further than **15 metres**.
- It has to be able to **see** you: walls, rocks and hills block it. **Stealth** keeps you hidden, and a monster that's already fighting you loses you if you vanish.

**Friends join in**

- A monster in a fight pulls in idle friends standing close to it: the same kind of monster, or one of the same race and side. Most monsters call in friends within **4 metres**; some reach further, up to 10 metres.
- Friends are called in one at a time, but each newcomer calls its own neighbours, so a tight pack can chain into one big pull.

**Who it attacks**

- Damage draws a monster's attention: every point of damage you deal counts. A debuff, or an attack that misses, still counts for a little.
- **Healing doesn't draw attention.** A healer who only heals never makes a monster turn on them, although the monster still notices a healer who walks into its sight.
- A monster doesn't switch targets lightly. It changes only when someone else's attention reaches **120%** of its current target's. Taunts force it onto the taunter for a while.

**How far it hits**

- A melee monster walks in until it's well inside its reach, then starts swinging. It keeps swinging until you step **beyond** its reach. Most monsters reach about 2 metres, plus about a metre for you; big ones reach 3–5 metres.
- Ranged monsters shoot from up to **25 metres** and switch to melee when you're close.

**When it gives up**

- A monster gives up and goes home when it's pulled **more than 30 metres from where it was standing**.
- It also gives up if nobody has damaged it, and it hasn't damaged anybody, for **15 seconds**.
- Once a fight has gone on for 10 seconds, it gives up if its target gets further away than it can track: **15 metres** for melee monsters and **30 metres** for ranged ones. A monster following a leader keeps going while the leader is still fighting.
- If its target dies, goes into stealth or gets away, it turns to the next person who has hurt it. If there's nobody left, it goes home.

### Respawns in the open world

- Every monster has its own respawn time, but **the game speeds it up when players outnumber the monsters around them**. A creature that dies with few of its kind still standing nearby, compared with the number of players nearby, comes back sooner. With six or more monsters per player around it, it takes its full time. With fewer, it takes about 83%, 67%, 50% or 33% of that time, and with very few it takes as little as 17%.
- Monsters that appear during an event (dungeon waves, bosses that arrive, summoned adds) never respawn.

### Ore, chests and other objects

Once you've used or gathered an object, it comes back after:

| Object | Comes back after |
| --- | --- |
| Bronze Metal, Marcasite, Primal Iron | 2 minutes |
| Copper Ore | 3 minutes 20 seconds |
| Light Silver, Matte, Magic Blood | 3 minutes |
| Treasure Box | 5 minutes |
| Field chests (the level-banded chests, from Lv 1–3 up to Lv 57–60) | 90 minutes |
| Knahswahs Jail chests | Once per run (they don't come back before the copy resets) |
| Quest objects (cages, sacks, handbooks and the like) | Usually 25–30 seconds, some 3 minutes |
| Anything else | 3 minutes |

### Moving around

- Running speed is **6 metres a second**. Walking is 40% of that, swimming 50%, and a ghost runs 1.5× as fast.
- There's **no fall damage**.
- **If you leave the edge of the map, you are moved to the nearest graveyard.** You arrive alive.
- The game doesn't kick you for moving unusually fast; the server tolerates it.
- You can't jump while a slow has cut your speed by more than 90%.

### Water and lava

**Swimming**

- Deep enough water makes you swim, at half your running speed. You start by floating at the surface, with a ring of ripples round you.
- **Swim forward while looking down to dive.** Underwater you move in the direction you're looking, up or down.
- There's **no breath limit**: you can stay underwater as long as you like.
- You can't ride in water. Starting to swim puts you back on your feet.

**Jumping in water**

- **At the surface, jump works as normal.** You leap out of the water with a splash and land back in it, or on the bank.
- **Underwater, the jump button swims you straight up** towards the surface instead of jumping.
- Jumping or dropping into water lands you in it with a splash, swimming. There's no fall damage, so a high drop into water is safe.

**Monsters and water**

- **Most monsters won't follow you into water.** A land monster chasing you stops at the water's edge and waits there. **While you're out of its reach it can't be hurt**, so you can't pick it off from the water. It carries on the fight once you come back within reach.
- Some monsters swim and will follow you in: crocodiles, crabs, water elementals, several of the Sailen palace guards and casters, and the Vicious Elf, Human and Orc Prisoners in Knahswahs Jail. A few, like the **Drowned Man**, live only in the water and won't follow you out.
- Monsters that swim after you don't spread out around you the way they do on land; they bunch up where they reach you.

**Lava**

- Some areas have lava you can wade or swim in, with a fiery splash.
- **Lava burns for 10% of your maximum health every second**, less your fire resistance.
- If lava kills you, you can't revive on the spot. You have to go back from the graveyard.
- No monster will follow you into lava.

## Mounts

Mounts arrived after 1.0.3. They are in 1.1.4.

### Getting and using a mount

- You learn a mount by using a **Mount Bond** item. The mount then appears on the **Mount** tab of your spell book, grouped by race (Human, Elf, Orc, Undead). The same tab has a **Riding Skill** page.
- To ride, either tap the mount in the spell book and press **Use** on its card, or drag it onto your action bar and tap it there.
- Summoning takes **3 seconds** for the basic mounts and 2–2½ seconds for the better ones.
- While you're riding, a **mount button appears on your action bar**. It shows the mount you're on, and tapping it gets you off. The button isn't there when you're on foot.

### Speed and stability

| Mount | Speed | Stability |
| --- | --- | --- |
| Brown, White or Black Horse; Meek, Violet or Greengrass Deer; Tamed, Cyan or Green Lion; Black, Rotting or Bloody Undead Dog | +20% | 30 |
| Aryurumoka Horse, Deer, Lion or Undead Dog | +25% | 40 |
| Eristar Bear Spirit, Sand Skin Wolf | +30% | 40 |
| Eristar War Bear Spirit | +35% | 60 |
| Crethiyum's Desert Walker | +35% | 50 |
| Battlefront Black Wolf | +35% | 45 |
| Battlefront Champion Snow Wolf | +40% | 50 |
| Eristar Bear Spirit (Morph) | +40% | 45 |

**Stability** is the percentage chance that you stay in the saddle when something hits you.

### Where you can and can't ride

- **You can't mount in combat.**
- **Mounts don't work in dungeons.**
- Most mounts don't work in the arena either. The two **Battlefront** wolves are the exception: they can be ridden in the arena ("You can ride it in the Arena").
- You're put back on your feet when you **walk into a building**, **start swimming**, or do anything that can't be done on horseback. You can stand, run, jump and land while mounted; attacking, casting or sitting dismounts you.

## Parties: experience, money and loot

A party holds up to **4 players**. Almost everything a party shares only reaches members **within 50 metres** of the monster when it dies.

### Who gets the kill

- The first player or party to hit a monster gets a **head start worth 20% of the monster's health**.
- When the monster dies, whoever has done the most damage gets the kill, counting that head start: a party counts as one, a solo player on their own. The winner gets the experience and the loot; a tie is decided at random.
- **Every party member within 50 metres gets credit for "kill X" quests**, not only the one who landed the blow.

### Experience

- Solo, a kill is worth **5 × your level + 50** experience, adjusted for the level gap:
  - Monster 10 or more levels above you: 1.5×.
  - Monster up to 9 levels above you: +5% for each level.
  - Monster 1–2 levels below you: −10% for each level.
  - Monster 3 or more levels below you: tapers off to nothing. The cut-off is 5 levels below you up to level 28, and widens by one level every 5 levels after that (6 levels from level 29, 7 from level 34, and so on).
  - **Elites are worth double.**
- In a party, the level used is the **highest level among the nearby members**, and the kill is worth more in total the more of you are nearby: 1.6× for two, 1.8× for three and 2× for four. That total is **shared in proportion to each member's level**, so a higher-level member takes a bigger share.

### Money

- Money on a body is **split evenly between every party member within 50 metres**, and the player who picks it up also gets any coins left over.
- Each member sees "You pick up [amount]." for their own share.

### What's on a body

- A body that has loot for you **glows**. Only the players who earned the kill see it glow: the winning party's members within 50 metres, or the solo player.
- A body holds at most **9 items**, plus money. If a monster drops more than that, the lowest-quality items are left out first, but quest items are always kept.
- Gear can come with a random prefix, so the same item can drop with different bonuses.
- **Each player only sees what they can take**:
  - Quest drops appear only for players whose quests need them.
  - An item that starts a quest appears only for players who can take that quest and don't already have it.
  - An item that has gone to another party member (by turn, roll or the leader) disappears from everyone else's view.
- Money is shown to everyone and split as described above.

### Loot rules

The leader picks one of three loot rules and a quality threshold:

| Rule | What happens |
| --- | --- |
| **Free loot** | Anyone in the party within range can take anything. |
| **Team allotment** | Items at or above the threshold go to a roll. Items below it go to whoever's **turn** it is; the turn moves to the next member with each kill. |
| **Leader allotment** | Items at or above the threshold wait for the leader to hand them out ("Assign to..."). Items below it go to whoever's turn it is. If the leader doesn't hand an item out within **60 seconds** ("Leader allotment timed out."), it goes to a roll. |

- The leader sets both in the team screen (**Rule:** and **Quality:**, then **Save settings**). The threshold can be **Excellent**, **Rare**, **Epic** or **Artifact**. Under Team or Leader allotment, Poor and Common items always go to whoever's turn it is.
- **A new party starts on Team allotment with the threshold at Excellent.** Changing the leader puts the party back on those defaults.
- On Leader allotment, if the leader isn't within 50 metres when the monster dies, that kill is handled as Team allotment at Excellent instead.
- "Loot in turn" appears among the game's text but isn't offered as a rule.
- Changes are announced: "The loot rule has been changed to …" and "The loot quality has been changed to …".

### Rolling

- Each player chooses to **roll** or **give up**. There's no separate greed option.
- A roll is a number from **1 to 100**, and no two players in the same roll get the same number, so there are no ties. The highest roll wins.
- The roll ends as soon as everyone has chosen, or after **60 seconds**.
- If everyone gives up, the item is free for anyone in the party: "All the team members dropped [item]. Everyone can pick it up now."
- The roll is announced as it happens: "[name] wants to get [item].", "[name] has dropped [item].", "[name] rolled 57 points." and "[name] got [item]!".

### Quest items

- If the player whose turn it is doesn't need a quest item, **anyone in the party who needs it can take it**.
- Some quest items are shared: **every member who needs one gets their own copy** from the same body.

## Interface

### Spell book

- Tabs: **Spells** (five pages), **Talents** and **Mount**.
- **Tapping a spell highlights it and opens its card**, with its description and a **Use** button that casts it. Tap the same spell again to close the card.
- Press and drag a spell to put it on your action bar.
- Mount spells work exactly the same way. There's no separate cast button on the row itself: you use the card's **Use** button or the action bar.

### Quest log

- The log holds up to **12 quests**. Its title shows how full it is: **"QUEST LOG  3 / 12"**.
- That count is quests in your log, not objective progress. Objective progress is on each quest's detail page, as **"Wolf  3/10"**. The count never goes past the target, even if you kill more.
- You can track up to **3 quests** at a time.
- Each quest has **Share** and **Drop** buttons.

### Quest givers

- When you open a quest, some quest givers act out what they're saying: they nod, talk, wave, or make a questioning or exclaiming gesture.
- When you hand a quest in, they may nod, applaud, cheer or bow.
- A few shake their head ("no") if you come back before you've finished, and a few salute when you hand in.
- Most quest givers do none of this; only around 66 quests have these gestures.

### Friends and ignored players

- The social screen has **Friends** and **Ignored** lists, and an **Add** button that adds a friend by name.
- **Tap a friend** for a menu: **Whisper**, **Invite**, **Ignore**, **Delete**.
  - Whisper opens the chat box addressed "to [name]".
  - Ignore moves them from your friends to your ignored list: "Ignored [name] successfully."
  - Delete asks first: "Delete [name] from the friend list?" Then: "Deleted friend [name] successfully."
- **Tap someone on your ignored list** for: **Add as friend**, **Relieve**, **Report to GM**.
  - Add as friend asks first: "You have ignored [name] before. Are you sure you want to add him/her to the friend list?"
  - Relieve takes them off your ignored list.
  - Report to GM opens a report letter to a GM.
- **Tap another player in the world** for: **Whisper**, **Invite**, **Add as friend**, **Ignore**, **Report to GM**. So you can ignore someone without typing their name.

### Mail

- The mailbox shows up to **20 letters**. Each one shows how long it has left ("Time left: 3 day(s)", then hours on its last day).
- **Write** starts a new letter, with up to **5 attachments** ("5 attachments are maxed out.") and money.
- When you read a letter:
  - **Reply** starts a letter back titled "Re: [title]".
  - **Send Back** returns the letter to its sender.
  - **Extract All** takes every attachment.
  - Tap a single attachment to see its card with an **Extract** button, which takes just that item.
  - The money has its own extract button.
  - **Delete** warns you first if anything is still attached: "If you delete this mail, the attachments will also be deleted. Are you sure?"
- Sending shows "Sent Successfully" or "Not Sent", and "Send failed. The character doesn't exist." for a wrong name.

### Bags, storage and vendors

- **Bags have no sort button.** The storage box's layout includes an **Auto-arrange** button, but the game keeps it hidden.
- **Vendors have no buyback.** Once you sell an item, it's gone.

### Options

- The **Options** menu opens **Settings** and **Language**.
- Settings is a scrolling list of on/off switches:
  - Auto-adjust the vertical camera
  - Auto-adjust the horizontal camera
  - Reorientation
  - Display loot messages
  - Reverse Y-axis
  - Display guild names
  - Ping indicator
  - Enable cellular data network (2G/3G)
- Settings also has **Music** and **SFX** controls.
- A separate **Hints** page lists the tutorial hints.

## Chat

### Channels

You pick a channel from the chat bar before you type. Each has its own colour in the chat log.

| Channel | Who hears it | How it shows | Colour |
| --- | --- | --- | --- |
| **Speak** | Everyone within **20 metres** | "Name: text" | White (your own lines a soft cream) |
| **Yell** | Everyone within **100 metres** | "Name: text" | Red |
| **Whisper** | One player, wherever they are | "Name: text", or "to Name: text" for what you sent | Purple |
| **Team** | Your party, wherever they are (only offered while you're in one) | "Name: text" | Light blue |
| **Guild** | Your guild, wherever they are (only offered while you're in one) | "Name: text" | Green |
| **Local** | Everyone in the same region of the world as you | "[Local] Name: text" | White |
| **Global** | Everyone online on the server | "[Global] Name: text" | White |

- There's no separate Trade channel to pick in 1.1.4.
- **Local** follows you: crossing into another region moves you into that region's Local channel.
- Whatever channel the server talks on (notices, system messages) shows in yellow, and loot pick-ups in pink.
- Free trial accounts can't speak on Local.

### Whispers

- Whisper someone from their menu (tap them in the world, on your friends list or in the party screen), or from the chat bar's **To last whisper**. If nobody has whispered you yet: "No one whispered to you yet."
- A whisper to **GM Team** reaches the support team.

### Emotes

- The **Expression** page of the chat bar plays an emote. Everyone within **20 metres** sees the animation and a line of text in orange.
- With someone selected, the line names them ("Hero waves to Archer."; they see "Hero waves to you."). With nobody selected it's to everyone ("Hero waves to everyone.").
- The emotes: Bow, Wave, Kiss, Work, Talk, Amaze, No, Yes, Shout, Laugh, Point, Beg, Cheer, Applaud, Cry, Rude, Flex, Dance, Salute, Eat and Sit.

### Quick phrases

- The chat bar has ready-made lines you can send with one tap: "Hello!", "Thank you!", "Help me please!", "Congratulations!" and "See you!". **Edit** and **Custom** let you set lines of your own, and **Repeat** sends your last line again ("There are no conversations to repeat." if there isn't one).

### Limits

- A chat line is up to **255 characters**. The whisper box opened from a player's menu takes up to 100.
- Bad words are filtered out of what you send.
- **Slow down on the big channels:** on Local and Global you can send **3 lines every 10 seconds**. Say, Yell and emotes allow **10 every 10 seconds**. Past that the line isn't sent and you see "You speak too frequently, please hold on." GMs aren't limited.

### Choosing what you see

- Each channel can be switched off in chat settings, so you stop seeing it.
- The **Language Blocker** hides Local and Global lines from players whose game is set to a language you've blocked. It doesn't touch Speak, Yell, whispers, party or guild chat.
