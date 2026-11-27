using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

// Own choice: a body lying dead acts on nothing in the world until it is released. It may still
// talk, keep its books, tidy its bags, back out of what others asked of it, and release.
public static class WhileDead
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        WebMessageTypes.SessionPing,
        WebMessageTypes.SessionDescribe,
        WebMessageTypes.AccountLogin,
        WebMessageTypes.AccountLogout,
        WebMessageTypes.DevEnterWorld,
        WebMessageTypes.CharactersList,
        WebMessageTypes.CharactersCreate,
        WebMessageTypes.CharactersDelete,
        WebMessageTypes.CharactersOutfit,
        WebMessageTypes.CharactersChoose,
        WebMessageTypes.CharactersReturn,
        // Answered, not refused: the move handler puts the body back where it lies.
        WebMessageTypes.PlayerMove,
        WebMessageTypes.PlayerUnstuck,
        WebMessageTypes.TargetChange,
        WebMessageTypes.PlayerChat,
        WebMessageTypes.ChatJoinChannel,
        WebMessageTypes.ChatLeaveChannel,
        WebMessageTypes.ChatWhisper,
        WebMessageTypes.SocialFriendAdd,
        WebMessageTypes.SocialFriendRemove,
        WebMessageTypes.SocialFriendList,
        WebMessageTypes.SocialWho,
        WebMessageTypes.SocialIgnoreAdd,
        WebMessageTypes.SocialIgnoreRemove,
        WebMessageTypes.SocialReport,
        WebMessageTypes.QuestList,
        WebMessageTypes.QuestRefreshStatuses,
        WebMessageTypes.QuestDestroy,
        WebMessageTypes.ActionBarAssign,
        WebMessageTypes.InventoryMove,
        WebMessageTypes.InventorySplit,
        WebMessageTypes.InventorySort,
        WebMessageTypes.InventoryDestroy,
        WebMessageTypes.SpellAbort,
        WebMessageTypes.TradeDecline,
        WebMessageTypes.TradeCancel,
        WebMessageTypes.DuelDecline,
        WebMessageTypes.PartyInvite,
        WebMessageTypes.PartyAccept,
        WebMessageTypes.PartyDecline,
        WebMessageTypes.PartyLeave,
        WebMessageTypes.PartyKick,
        WebMessageTypes.PartyVoteKick,
        WebMessageTypes.PartyVoteKickAnswer,
        WebMessageTypes.GuildInvite,
        WebMessageTypes.GuildAccept,
        WebMessageTypes.GuildDecline,
        WebMessageTypes.GuildLeave,
        WebMessageTypes.GuildKick,
        WebMessageTypes.GuildRosterRequest,
        WebMessageTypes.NavigationRequest,
        WebMessageTypes.PatrolRequest,
        WebMessageTypes.PatrolSave,
        WebMessageTypes.AdminTeleport,
        WebMessageTypes.AdminGiveItem,
        WebMessageTypes.AdminGiveGold,
        WebMessageTypes.AdminSetGold,
        WebMessageTypes.AdminSetLevel,
        WebMessageTypes.AdminSetInvincible,
        WebMessageTypes.AdminLearnSpells,
        WebMessageTypes.DevStressTest,
    };

    public static bool Allows(string messageType) => Allowed.Contains(messageType);
}
