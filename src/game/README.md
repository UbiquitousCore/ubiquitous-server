# Ubiquitous.Game

Transport-agnostic game rules, one service per subsystem (combat, spells and
auras, creature movement, quests, loot, inventory, vendors, gossip, leveling,
travel, parties, guilds, chat, trade, mail, auction, duels, PvP, instances,
emotes, ambient state), each driven by a `PlaySession` handler. `GameContent`
is the definition bundle they read. Behaviour: `docs/BEHAVIOUR.md`. Tests:
`tests/game`.
