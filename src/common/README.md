# Ubiquitous.Common

Primitives shared by every layer (`EntityId`, `ObjectType`, `Position`,
`Orientation`, `MapId`) and the store interfaces game services persist through
(`IInventoryStore`, `IQuestLogStore`, `IGuildStore`, `IMailStore`,
`IAuctionStore`, `IFriendStore`, `IReportStore`). No I/O, protocol or game
rules. Depends on nothing.
