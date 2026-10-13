# Playerbots

Characters the server plays itself, for testing and to fill the world. A bot
is an ordinary client: it signs in to an account of its own, creates a
character through `characters.create`, and plays by sending the messages
`ubiquitous-client` sends. It knows only what those replies tell a client, plus the
client tables (`BotKnowledge`: which creature a quest wants, which creatures
drop its item, what a spell does). The server cannot tell a bot from a player
and gives it nothing a player would not get.

## Settings

| Setting | Default | |
|---|---|---|
| `enabled` | `false` | start bots at all |
| `randomBotCount` | `5` | how many bots play on their own (0-500) |
| `loginsPerTick` | `1` | bots signed in per world tick at startup |
| `acceptPartyInvites` | `true` | whether bots join any group they are invited to |
| `groupWithBots` | `false` | whether bots invite each other into groups; a bot in a bot's group cannot be invited by a player |
| `grindLevelsAbove` | `1` | the highest creature level above its own a bot picks a fight with |
| `doubleUpPercent` | `10` | how often a bot joins a creature another player is fighting or is nearer to (0-100) |
| `acceptDuels` | `true` | whether bots accept duels |
| `idleBreaks` | `true` | whether bots pause, stop to talk and step away like players do |
| `lootStyle` | `mixed` | `hoarder`, `ordinary` or `picky` for every bot; `mixed` gives each bot its own |
| `seed` | random | makes names, races and wandering repeatable |
| `minLevel`, `maxLevel` | `1`, `20` | the level range a new random bot's character starts at |
| `logInAndOut` | `true` | whether random bots log off and come back as players do over a day |
| `onlineMinutesMin`, `onlineMinutesMax` | `40`, `240` | how long a bot plays before logging off |
| `offlineMinutesMin`, `offlineMinutesMax` | `10`, `90` | how long it stays away |
| `unwatchedTurnEvery` | `2` | a bot with no player within 80 units plays one world tick in this many (1-2; a step covers at most a second of walking) |

Bot `N` signs in as `module.playerbots.randomN` and keeps its character across
restarts. Even-numbered bots are made as humans or elves and odd-numbered ones
as orcs or undead, so both starting lands have bots; class and sex are random.
Every bot takes the first skin, face, hair style and hair colour its body
offers, since the server does not know how many more each body has. A bot
whose character was made with look 0 (no texture, drawn black) deletes it and
makes a new one.

## What a bot does

Each turn it does the first of these that applies (`BotBrain.Decide`):

1. Dead: waits a few seconds, then releases (`player.unstuck`).
2. Answers group invites, duels, trades, summons and its leader's commands.
   It comes when a group mate summons it, unless it is fighting or trading.
3. Mid-trade: confirms once the other side has.
4. A healer in a group heals the most hurt group mate below 60%.
5. Fights: its duel opponent once the countdown ends, whatever hits it, then
   (a tank) whatever hits a group mate, then its leader's or its own target.
6. Low on health and out of a fight: heals itself or rests for a while.
7. Loots creatures it fought, keeping to its own taste (below).
8. Wears a better item from its bags; spends talent points down one tree.
9. In someone else's group: follows its leader, through a map gate if the
   leader walked through one, and does nothing else.
10. Refreshes quest marks; sells at the nearest vendor it has seen when its
    bags are nearly full.
11. Five or more levels above the creatures within 150 units, leaves for a
    place nearer its level (below).
12. Hands in, picks up and works on quests, reading any quest item in its
    bags and asking any object that starts a quest; grinds hostile creatures
    near its level that still give experience.
13. Between tasks, sometimes takes a break (below).
14. After three minutes with nothing of that to do, travels somewhere else.
15. With `groupWithBots` on, invites a bot of a similar level it meets.
16. Wanders, stopping a few seconds at each spot.

A new random character starts at a level between `minLevel` and `maxLevel`,
standing at a bus stop whose creatures (the median level within 150 units) are
nearest that level, as mod-playerbots places its random bots. With
`logInAndOut` each bot plays a stint, logs off, and comes back as the same
character; its first stint after startup is any part of a whole one, so bots
started together leave at different times. A bot in a fight or a group stays
until it is out of it. A new character wears a look its body ships: any of the
skins, faces, hair styles and colours the package's `playerLooks` lists.

Travelling, a bot weighs every bus fare from the nearest stop and every
teleport an NPC's conversation on its map offers (such as the Orc Teleporter's
to the Town of Silence) that it can afford and that lands on main ground,
judges each by the creatures around where it lands, and takes one of those
within two levels of the best fit. Leaving a place it has outgrown, it stays
if nothing fits better.

A bot talks, hands in, loots and uses objects from up to 8 units away (the
server allows 10); an NPC it cannot walk right up to, behind a counter or off
the walkable ground, is dealt with from any reachable spot in reach, picked at
random so bots talking to the same NPC do not stand on one another.
A quest objective that asks for a spell to be cast is met by casting it, at a
training dummy if one is in sight, else at a creature nobody else is fighting,
or on itself for a spell aimed at the caster. A quest the server gives no
directions for (item quests) sends the bot to the nearest spawn of a creature
that drops what it needs. It sells whatever a vendor buys and it will not
wear, use or hand in once three or fewer bag slots are free, and keeps selling
until six are free after the server turns something away for full bags. A sale
refused because its picture of the bag is out of step makes it sort the bag,
which brings the whole bag back.

A bot works round a quest's creatures: the server's guide areas first, then
every place the creatures spawn, leaving a place it finds empty for four
minutes and trying the next. A quest that has not moved for eight minutes is
put aside for ten while the bot does other quests, grinds, sells or travels. A
quest asking for a spell the bot does not have is abandoned, and not taken up
again; a quest-giver offering only such quests is left alone for ten minutes.
A goal the bot has not got closer to in eight seconds of trying (however much
else it does in between) is given up for a while, and distances count height:
a corpse under a bridge is walked down to, not stood over. Each step carries
the bot's velocity, so a watching client moves it smoothly and faces it the
way it walks.

Each bot loots in its own way: a hoarder (three in ten) takes everything, an
ordinary looter (most) leaves grey trash it cannot use, and a picky one (one in
four) takes only coin, quest items, decent gear its class wears and things
worth at least 10 coins. With full bags, a bot throws away the item it values
least (its sale price, unless a quest needs it or it would wear it) to make
room for something it values more.

A creature another player is fighting, or is nearer to, is left to them
unless they are in the bot's group; `doubleUpPercent` of creatures are joined
anyway. Roles follow class: warriors tank, monks heal, mages and rangers deal
damage. Only group mates may trade with a bot, so a player can hand their bot
gear and it will wear it.

A bot finishes what it starts: it never stops partway through a walk or a
fight. Between tasks (after a kill, a hand-in, picking up a quest, looting,
selling, a bus ride or reaching a wander spot) it pauses one to four seconds
about a third of the time, as if reading its quest log. At most once every
three minutes the pause can be longer: one time in ten it stands 8-40 seconds
playing the talk emote, as if typing to someone, and one time in thirty it
steps away for one to three minutes. Anything attacking it, an invite or a
leader's command still gets an answer; a following bot never takes a break.
Saying or sending something that needs no standing still (quest marks, chat,
group answers, talents, gear, emotes) does not stop a walking bot.

A long walk (25 units or more) bends, two times in five, through a point off
to one side that stands on walkable ground and costs at most half as much
again, so bots going the same way do not walk in single file.

On a map with baked walkable ground a bot only ever walks the route the map
gives it, never a straight line, and only on the main ground: a connected
patch of 2,000 square units or more (`Bot.MainGroundArea`). Smaller patches,
such as a strip between water and a fence or a rooftop, are pockets: a bot
never walks to one, steps straight back out if it slips into one, and asks to
be unstuck only if it finds itself in one with nowhere to step back to. A bot
just below a floor (a plaza laid over the terrain) steps up onto it. A route is
planned again only when its goal moves, so a bot does not flip between two
equally short ways round something. A goal with no route, a route that stops
short, or eight seconds without getting closer means the goal is out of reach,
and the bot leaves it for a while. On a map with no baked ground bots walk
straight. In water deep enough to swim (the bed 1.25 below the surface) a bot
swims at the swim line at half pace; routes go round water unless the way
round is long. A refused spell, talent or item is set aside for a while, and so is
whatever a bot was walking to when the server moved it back.

With `--log-level Debug` the log notes each bot's quests, levels, map changes,
duels and new gear, and every goal it gives up walking to.

## Playing with bots

Invite a bot (`party.invite`) and it joins and follows you, fighting what you
fight. Say these in party chat, or whisper them, as the group leader:

| Command | |
|---|---|
| `follow` | follow the leader (the default in a group) |
| `stay` | stand still, fighting only what attacks it |
| `free` (`grind`, `quest`) | play on its own again while staying in the group |
| `attack` | attack the leader's target |
| `leave` | leave the group |
| `report` (`stats`) | say its level, health and quest count |
