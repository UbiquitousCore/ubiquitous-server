namespace Ubiquitous.Protocol.Web;

public static class WireCodes
{
    public static class ProtocolError
    {
        public const string BarrierClosed = "barrier_closed";
        public const string CannotUse = "cannot_use";
        public const string DuelRefused = "duel_refused";
        public const string DungeonModeRefused = "dungeon_mode_refused";
        public const string DungeonResetRefused = "dungeon_reset_refused";
        public const string Forbidden = "forbidden";
        public const string InsufficientFunds = "insufficient_funds";
        public const string Internal = "internal";
        public const string InvalidAuctioneer = "invalid_auctioneer";
        public const string InvalidChannel = "invalid_channel";
        public const string InvalidChat = "invalid_chat";
        public const string InvalidEmote = "invalid_emote";
        public const string InvalidGossip = "invalid_gossip";
        public const string InvalidGuild = "invalid_guild";
        public const string InvalidInstance = "invalid_instance";
        public const string InvalidInventory = "invalid_inventory";
        public const string InvalidMail = "invalid_mail";
        public const string InvalidParty = "invalid_party";
        public const string InvalidPayload = "invalid_payload";
        public const string InvalidQuest = "invalid_quest";
        public const string InvalidReport = "invalid_report";
        public const string InvalidState = "invalid_state";
        public const string InvalidStaticObject = "invalid_staticobject";
        public const string InvalidSummon = "invalid_summon";
        public const string InvalidTarget = "invalid_target";
        public const string InvalidTeleport = "invalid_teleport";
        public const string InvalidTrade = "invalid_trade";
        public const string InvalidVendor = "invalid_vendor";
        public const string Malformed = "malformed";
        public const string NotAuthenticated = "not_authenticated";
        public const string NotInWorld = "not_in_world";
        public const string PatrolRefused = "patrol_refused";
        public const string RateLimited = "rate_limited";
        public const string TradeRefused = "trade_refused";
        public const string UnstuckRefused = "unstuck_refused";
        public const string Unsupported = "unsupported";
        public const string UpgradeRefused = "upgrade_refused";
    }

    public static class Outcome
    {
        public const string Hit = "hit";
        public const string Miss = "miss";
        public const string Dodge = "dodge";
        public const string Parry = "parry";
        public const string Block = "block";
        public const string Critical = "critical";

        public const string Evade = "evade";

        public const string Heal = "heal";

        public const string PeriodicHeal = "periodicHeal";
    }

    public static class Hand
    {
        public const string Main = "main";
        public const string Off = "off";
    }

    public static class DungeonMode
    {
        public const string Normal = "normal";
        public const string Legend = "legend";
    }

    public static class SummonResult
    {
        public const string Asked = "asked";

        public const string Accepted = "accepted";

        public const string Refused = "refused";

        public const string Failed = "failed";

        public const string LevelTooLow = "levelTooLow";
    }

    public static class ShareResult
    {
        public const string AlreadyHas = "alreadyHas";

        public const string NotEligible = "notEligible";

        public const string OutOfReach = "outOfReach";

        public const string Accepted = "accepted";
        public const string Declined = "declined";
    }

    public static class MailResult
    {
        public const string Ok = "ok";
        public const string SelfMail = "selfMail";
        public const string UnknownRecipient = "unknownRecipient";
        public const string EmptyBody = "emptyBody";
        public const string NegativeGold = "negativeGold";
        public const string NotEnoughMoney = "notEnoughMoney";
        public const string MailNotFound = "mailNotFound";
        public const string AlreadyClaimed = "alreadyClaimed";
        public const string NothingToClaim = "nothingToClaim";
        public const string InventoryFull = "inventoryFull";
        public const string NotInMail = "notInMail";
        public const string ItemNotInBag = "itemNotInBag";
        public const string Refused = "refused";
    }

    public static class Loot
    {
        public const string Lootable = "lootable";
        public const string Emptied = "emptied";
    }

    public static class ChoiceResult
    {
        public const string Teleported = "teleported";

        public const string NotEnoughMoney = "notEnoughMoney";

        public const string NothingHappens = "nothingHappens";

        public const string Started = "started";
    }

    public static class SpellRefusal
    {
        public const string UnknownSpell = "unknownSpell";
        public const string NoEffectData = "noEffectData";
        public const string OnCooldown = "onCooldown";
        public const string NotEnoughPower = "notEnoughPower";
        public const string InvalidTarget = "invalidTarget";
        public const string Incapacitated = "incapacitated";
        public const string Silenced = "silenced";
        public const string MissingEquipment = "missingEquipment";
        public const string AlreadyCasting = "alreadyCasting";
        public const string OutOfRange = "outOfRange";
    }

    public static class TalentRefusal
    {
        public const string UnknownTalent = "unknownTalent";
        public const string WrongClass = "wrongClass";
        public const string MaxRank = "maxRank";
        public const string NoPoints = "noPoints";
        public const string TierLocked = "tierLocked";
        public const string Prerequisite = "prerequisite";
    }

    public static class UnitValue
    {
        public const string Level = "level";
        public const string Health = "health";
        public const string MaxHealth = "maxHealth";
        public const string Power = "power";
        public const string MaxPower = "maxPower";
        public const string Xp = "xp";
        public const string XpToNextLevel = "xpToNextLevel";
        public const string Gold = "gold";
        public const string Strength = "strength";
        public const string Agility = "agility";
        public const string Stamina = "stamina";
        public const string Wisdom = "wisdom";
        public const string BaseStrength = "baseStrength";
        public const string BaseAgility = "baseAgility";
        public const string BaseStamina = "baseStamina";
        public const string BaseWisdom = "baseWisdom";

        // Chances are in hundredths of a percent: 9500 is 95%.
        public const string AttackPower = "attackPower";
        public const string HitChance = "hitChance";
        public const string CritChance = "critChance";
        public const string SpellPower = "spellPower";
        public const string HealingPower = "healingPower";
        public const string SpellHitChance = "spellHitChance";
        public const string SpellCritChance = "spellCritChance";
        public const string Armor = "armor";
        public const string ArmorMitigation = "armorMitigation";
        public const string DodgeChance = "dodgeChance";
        public const string ParryChance = "parryChance";
        public const string BlockChance = "blockChance";

        public const string Slowed = "slowed";
        public const string Stunned = "stunned";
        public const string Chilled = "chilled";
        public const string SpedUp = "spedUp";
        public const string Stealthed = "stealthed";
        public const string Silenced = "silenced";

        // A `mountprototype` row id; 0 is on foot.
        public const string Mount = "mount";

        public const string MoveSpeed = "moveSpeed";
    }
}
