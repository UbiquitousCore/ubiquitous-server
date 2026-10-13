using Ubiquitous.Common;
using Ubiquitous.Persistence;

namespace Ubiquitous.Server;

public sealed record SessionStores(
    IAccountAuthenticator Accounts,
    ICharacterStore Characters,
    IInventoryStore? Inventory = null,
    IQuestLogStore? QuestLog = null,
    IMailStore? Mail = null,
    IAuctionStore? Auction = null,
    IGuildStore? Guild = null,
    IFriendStore? Friend = null,
    IReportStore? Report = null,
    IProgressStore? Progress = null);
