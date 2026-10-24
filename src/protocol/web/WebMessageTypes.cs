namespace Ubiquitous.Protocol.Web;

public static partial class WebMessageTypes
{
    public const string SessionPing = "session.ping";

    public const string SessionPong = "session.pong";

    public const string AccountLogin = "account.login";
    public const string AccountLoginResult = "account.loginResult";
    public const string AccountLogout = "account.logout";
    public const string AccountLoggedOut = "account.loggedOut";
    public const string DevEnterWorld = "dev.enterWorld";
    public const string DevStressTest = "dev.stressTest";
    public const string CharactersList = "characters.list";
    public const string CharactersListed = "characters.listed";
    public const string CharactersChoose = "characters.choose";
    public const string CharactersChosen = "characters.chosen";

    public const string CharactersCreate = "characters.create";

    public const string CharactersOutfit = "characters.outfit";

    public const string CharactersOutfitted = "characters.outfitted";

    public const string CharactersDelete = "characters.delete";

    public const string CharactersDeleted = "characters.deleted";

    public const string CharactersCreated = "characters.created";

    public const string CharactersReturn = "characters.return";

    public const string CharactersReturned = "characters.returned";
    public const string WorldLoading = "world.loading";
    public const string WorldEntered = "world.entered";
    public const string EntityRemoved = "entity.removed";
    public const string PlayerMove = "player.move";
    public const string MoveAccepted = "move.accepted";
    public const string ProtocolError = "protocol.error";
    public const string PlayerChat = "player.chat";
    public const string ChatReceived = "chat.received";
    public const string TargetChange = "target.change";
    public const string TargetChanged = "target.changed";
    public const string NpcTalk = "npc.talk";
    public const string NpcPickOption = "npc.pickOption";
    public const string NpcPreviousPage = "npc.previousPage";
    public const string Gossip = "npc.gossip";
    public const string NpcChoiceResult = "npc.choiceResult";
    public const string QuestCheckMark = "quest.checkMark";
    public const string QuestStatuses = "quest.statuses";
    public const string QuestRefreshStatuses = "quest.refreshStatuses";
    public const string QuestList = "quest.list";
    public const string QuestListUpdated = "quest.listUpdated";
    public const string QuestDetails = "quest.details";
    public const string QuestLog = "quest.log";
    public const string QuestAccept = "quest.accept";
    public const string QuestUpdated = "quest.updated";
    public const string QuestComplete = "quest.complete";
    public const string QuestDestroy = "quest.destroy";
    public const string QuestRemoved = "quest.removed";
    public const string AuctioneerOpen = "npc.auctioneerOpen";
    public const string AuctionOpened = "auction.opened";
    public const string VendorOpen = "npc.vendorOpen";
    public const string VendorItems = "npc.vendorItems";
    public const string VendorBuy = "npc.vendorBuy";
    public const string BuyResult = "npc.buyResult";
    public const string VendorSell = "npc.vendorSell";
    public const string SellResult = "npc.sellResult";
    public const string PlayerValuesChanged = "player.updated";

    public const string PlayerExpGained = "player.expGained";
    public const string InventoryUpdated = "inventory.updated";
    public const string InventoryItemAdded = "inventory.itemAdded";
    public const string InventoryMove = "inventory.move";
    public const string InventoryItemMoved = "inventory.itemMoved";
    public const string InventoryEquip = "inventory.equip";

    public const string InventoryItemsSwapped = "inventory.itemsSwapped";

    public const string InventoryEquipResult = "inventory.equipResult";

    public const string ItemRefused = "item.refused";

    public const string EntityAppearanceChanged = "entity.appearanceChanged";
    public const string InventoryDestroy = "inventory.destroy";
    public const string InventoryItemRemoved = "inventory.itemRemoved";
    public const string InventoryUse = "inventory.use";
    public const string InventorySplit = "inventory.split";
    public const string InventoryStackChanged = "inventory.stackChanged";
    public const string InventorySort = "inventory.sort";
    public const string InventorySorted = "inventory.sorted";
    public const string InventoryOpenBank = "inventory.openBank";
    public const string InventoryBankOpen = "inventory.bankOpen";
    public const string InventoryBankUpdated = "inventory.bankUpdated";
    public const string InventoryExpand = "inventory.expand";
    public const string InventoryCapacityChanged = "inventory.capacityChanged";
    public const string EntitySpawned = "entity.spawned";
    public const string EntityMoved = "entity.moved";
    public const string AttackEngage = "attack.engage";
    public const string AttackMeleeStart = "attack.meleeStart";
    public const string AttackSwingResult = "attack.swingResult";
    public const string AttackRangeStart = "attack.rangeStart";
    public const string AttackShotFired = "attack.shotFired";
    public const string AttackShotResult = "attack.shotResult";
    public const string AttackHalted = "attack.halted";
    public const string AttackEnterCombat = "attack.enterCombat";
    public const string AttackExitCombat = "attack.exitCombat";
    public const string UnitDied = "unit.died";
    public const string PlayerUnstuck = "player.unstuck";
    public const string PlayerRevived = "player.revived";
    public const string PlayerDeath = "player.death";
    public const string EntityValuesChanged = "entity.updated";
    public const string EntityLootable = "entity.lootable";
    public const string LootOpen = "loot.open";
    public const string LootClose = "loot.close";
    public const string LootTake = "loot.take";
    public const string LootTakeMoney = "loot.takeMoney";
    public const string LootContents = "loot.contents";
    public const string LootBusy = "loot.busy";
    public const string LootSlotEmptied = "loot.slotEmptied";
    public const string NpcTeleportAsk = "npc.teleportAsk";
    public const string NpcTeleportAnswer = "npc.teleportAnswer";
    public const string PlayerTeleported = "player.teleported";
    public const string BusRequest = "bus.request";
    public const string BusConfirm = "bus.confirm";
    public const string AdminTeleport = "admin.teleport";
    public const string AdminGiveItem = "admin.giveItem";
    public const string AdminGiveGold = "admin.giveGold";
    public const string AdminSetLevel = "admin.setLevel";
    public const string AdminSetInvincible = "admin.setInvincible";
    public const string AdminInvincible = "admin.invincible";
    public const string AdminLearnSpells = "admin.learnSpells";
    public const string BusFare = "bus.fare";
    public const string SpellRequest = "spell.request";
    public const string SpellAbort = "spell.abort";
    public const string ActionBarAssign = "actionBar.assign";
    public const string SpellBook = "spell.book";
    public const string SpellLearned = "spell.learned";
    public const string MountDismount = "mount.dismount";
    public const string ActionBarAssigned = "actionBar.assigned";
    public const string SpellCasting = "spell.casting";
    public const string SpellReleased = "spell.released";
    public const string SpellCooldownStarted = "spell.cooldownStarted";
    public const string SpellFailed = "spell.failed";
    public const string SpellInterrupted = "spell.interrupted";
    public const string SpellOutcome = "spell.outcome";
    public const string AuraApplied = "aura.applied";
    public const string AuraEnded = "aura.ended";
    public const string StaticObjectInteract = "staticobject.interact";
}
