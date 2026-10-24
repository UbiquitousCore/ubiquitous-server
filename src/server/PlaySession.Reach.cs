using System.Text.Json;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private enum InteractionBody
    {
        None,

        Npc,

        StaticObject,
    }

    private readonly record struct InteractionReach(InteractionBody Body, string? ErrorCode);

    private static readonly InteractionReach NoBody = new(InteractionBody.None, null);

    private static readonly IReadOnlyDictionary<string, InteractionReach> ReachByMessageType =
        new Dictionary<string, InteractionReach>(StringComparer.Ordinal)
        {
            [WebMessageTypes.NpcTalk] = new(InteractionBody.Npc, WireCodes.ProtocolError.InvalidGossip),
            [WebMessageTypes.NpcPreviousPage] = new(InteractionBody.Npc, WireCodes.ProtocolError.InvalidGossip),
            [WebMessageTypes.NpcPickOption] = new(InteractionBody.Npc, WireCodes.ProtocolError.InvalidGossip),

            [WebMessageTypes.AuctioneerOpen] = new(InteractionBody.Npc, WireCodes.ProtocolError.InvalidAuctioneer),
            [WebMessageTypes.VendorOpen] = new(InteractionBody.Npc, WireCodes.ProtocolError.InvalidVendor),
            [WebMessageTypes.VendorBuy] = new(InteractionBody.Npc, WireCodes.ProtocolError.InvalidVendor),
            [WebMessageTypes.VendorSell] = new(InteractionBody.Npc, WireCodes.ProtocolError.InvalidVendor),

            [WebMessageTypes.QuestList] = new(InteractionBody.Npc, WireCodes.ProtocolError.InvalidQuest),
            [WebMessageTypes.QuestAccept] = new(InteractionBody.Npc, WireCodes.ProtocolError.InvalidQuest),
            [WebMessageTypes.QuestComplete] = new(InteractionBody.Npc, WireCodes.ProtocolError.InvalidQuest),
            [WebMessageTypes.QuestCheckMark] = NoBody,
            [WebMessageTypes.QuestRefreshStatuses] = NoBody,
            [WebMessageTypes.QuestDestroy] = NoBody,
            [WebMessageTypes.ShareQuest] = NoBody,
            [WebMessageTypes.QuestShareAnswer] = NoBody,

            [WebMessageTypes.NpcTeleportAsk] = new(InteractionBody.Npc, WireCodes.ProtocolError.InvalidTeleport),

            [WebMessageTypes.LootOpen] = NoBody,
            [WebMessageTypes.LootClose] = NoBody,
            [WebMessageTypes.LootTake] = NoBody,
            [WebMessageTypes.LootTakeMoney] = NoBody,
            [WebMessageTypes.StaticObjectInteract] = NoBody,
            [WebMessageTypes.InventoryOpenBank] = NoBody,
            [WebMessageTypes.BusRequest] = NoBody,
            [WebMessageTypes.BusConfirm] = NoBody,
            [WebMessageTypes.TradeRequest] = NoBody,
            [WebMessageTypes.TradeAccept] = NoBody,
            [WebMessageTypes.TradeConfirm] = NoBody,
            [WebMessageTypes.DuelChallenge] = NoBody,

            [WebMessageTypes.AuctionBrowse] = NoBody,
            [WebMessageTypes.AuctionBid] = NoBody,
            [WebMessageTypes.AuctionBuyout] = NoBody,
            [WebMessageTypes.AuctionCancel] = NoBody,
            [WebMessageTypes.AuctionList] = NoBody,
            [WebMessageTypes.MailOpen] = NoBody,
            [WebMessageTypes.SummonOpen] = new(InteractionBody.StaticObject, WireCodes.ProtocolError.InvalidSummon),
            [WebMessageTypes.SummonRequest] = new(InteractionBody.StaticObject, WireCodes.ProtocolError.InvalidSummon),
            [WebMessageTypes.SummonAnswer] = NoBody,
            [WebMessageTypes.MailRead] = NoBody,
            [WebMessageTypes.MailSend] = NoBody,
            [WebMessageTypes.MailClaim] = NoBody,
            [WebMessageTypes.MailDelete] = NoBody,
            [WebMessageTypes.GuildFound] = NoBody,
            [WebMessageTypes.GuildInvite] = NoBody,
            [WebMessageTypes.GuildAccept] = NoBody,
            [WebMessageTypes.GuildDecline] = NoBody,
            [WebMessageTypes.GuildLeave] = NoBody,
            [WebMessageTypes.GuildKick] = NoBody,
            [WebMessageTypes.GuildDisband] = NoBody,
            [WebMessageTypes.GuildRosterRequest] = NoBody,
            [WebMessageTypes.PartyInvite] = NoBody,
            [WebMessageTypes.PartyAccept] = NoBody,
            [WebMessageTypes.PartyDecline] = NoBody,
            [WebMessageTypes.PartyLeave] = NoBody,
            [WebMessageTypes.PartyKick] = NoBody,
            [WebMessageTypes.PartyVoteKick] = NoBody,
            [WebMessageTypes.PartyVoteKickAnswer] = NoBody,
            [WebMessageTypes.InstanceEnter] = NoBody,
            [WebMessageTypes.DungeonRestart] = NoBody,
            [WebMessageTypes.DungeonChooseMode] = NoBody,
            [WebMessageTypes.NavigationRequest] = NoBody,
            [WebMessageTypes.PatrolRequest] = NoBody,
            [WebMessageTypes.PatrolSave] = NoBody,

            [WebMessageTypes.SessionPing] = NoBody,
            [WebMessageTypes.AccountLogin] = NoBody,
            [WebMessageTypes.AccountLogout] = NoBody,
            [WebMessageTypes.DevEnterWorld] = NoBody,
            [WebMessageTypes.DevStressTest] = NoBody,
            [WebMessageTypes.CharactersList] = NoBody,
            [WebMessageTypes.CharactersChoose] = NoBody,
            [WebMessageTypes.CharactersCreate] = NoBody,
            [WebMessageTypes.CharactersDelete] = NoBody,
            [WebMessageTypes.CharactersOutfit] = NoBody,
            [WebMessageTypes.CharactersReturn] = NoBody,
            [WebMessageTypes.PlayerMove] = NoBody,
            [WebMessageTypes.PlayerChat] = NoBody,
            [WebMessageTypes.TargetChange] = NoBody,
            [WebMessageTypes.AttackEngage] = NoBody,
            [WebMessageTypes.PlayerUnstuck] = NoBody,
            [WebMessageTypes.InventoryMove] = NoBody,
            [WebMessageTypes.InventoryEquip] = NoBody,
            [WebMessageTypes.InventoryUse] = NoBody,
            [WebMessageTypes.InventoryDestroy] = NoBody,
            [WebMessageTypes.InventorySplit] = NoBody,
            [WebMessageTypes.InventorySort] = NoBody,
            [WebMessageTypes.InventoryExpand] = NoBody,
            [WebMessageTypes.SpellRequest] = NoBody,
            [WebMessageTypes.SpellAbort] = NoBody,
            [WebMessageTypes.MountDismount] = NoBody,
            [WebMessageTypes.AdminTeleport] = NoBody,
            [WebMessageTypes.AdminGiveItem] = NoBody,
            [WebMessageTypes.AdminGiveGold] = NoBody,
            [WebMessageTypes.AdminSetLevel] = NoBody,
            [WebMessageTypes.AdminSetInvincible] = NoBody,
            [WebMessageTypes.AdminLearnSpells] = NoBody,
            [WebMessageTypes.ActionBarAssign] = NoBody,
            [WebMessageTypes.PlayerEmote] = NoBody,
            [WebMessageTypes.ChatJoinChannel] = NoBody,
            [WebMessageTypes.ChatLeaveChannel] = NoBody,
            [WebMessageTypes.ChatWhisper] = NoBody,
            [WebMessageTypes.SocialFriendAdd] = NoBody,
            [WebMessageTypes.SocialFriendRemove] = NoBody,
            [WebMessageTypes.SocialFriendList] = NoBody,
            [WebMessageTypes.SocialWho] = NoBody,
            [WebMessageTypes.SocialIgnoreAdd] = NoBody,
            [WebMessageTypes.SocialIgnoreRemove] = NoBody,
            [WebMessageTypes.SocialReport] = NoBody,
            [WebMessageTypes.DuelAccept] = NoBody,
            [WebMessageTypes.DuelDecline] = NoBody,
            [WebMessageTypes.DuelForfeit] = NoBody,
            [WebMessageTypes.PvpFlagSet] = NoBody,
            [WebMessageTypes.TalentLearn] = NoBody,
            [WebMessageTypes.TalentReset] = NoBody,
            [WebMessageTypes.TradeDecline] = NoBody,
            [WebMessageTypes.TradeOffer] = NoBody,
            [WebMessageTypes.TradeCancel] = NoBody,
        };

    private string? RefuseOutOfReach(string messageType, JsonElement payload)
    {
        if (!ReachByMessageType.TryGetValue(messageType, out var reach)) return null;
        if (reach.Body == InteractionBody.None || _world is null) return null;

        var networkId = PayloadIds.NetworkId(payload);
        if (networkId is null) return null;

        Ubiquitous.Common.Position target;
        if (reach.Body == InteractionBody.StaticObject)
        {
            if (!_staticObjectPositions.TryGetValue(networkId, out target)) return null;
        }
        else if (_shared is null || !_shared.TryGetPosition(networkId, out target)) return null;

        if (InteractionRange.Within(_world.PlayerPosition, target)) return null;

        return reach.ErrorCode is null
            ? string.Empty
            : Error(reach.ErrorCode, "Too far away.");
    }
}
