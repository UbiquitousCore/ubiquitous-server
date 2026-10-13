using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class GameContentTests
{
    [Fact]
    public void Prison_BarrierAndLastBossAreMeasuredFromTheInnerDoor()
    {
        var door = GameContent.PrisonInnerDoor;
        var summon = Assert.Single(GameContent.Development.DungeonSummons, s => s.CreatureEntryId == 2667);
        var barrier = Assert.Single(GameContent.Development.DungeonBarriers);

        Assert.Equal((door.X, door.Y, door.Z - GameContent.CosmoDepthPastDoor), (summon.X, summon.Y, summon.Z));
        Assert.InRange(door.X, barrier.MinX, barrier.MaxX);
        Assert.InRange(door.Z, barrier.MinZ, barrier.MaxZ);
    }

    [Fact]
    public async Task Session_UsesTheStartingPurseItsContentNames()
    {
        var session = await SessionAsync(GameContent.Development with { StartingCurrency = 7 });

        var response = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.VendorBuy, 3,
                new { networkId = "Creature:1", slot = 0, entry = 100, count = 1 }))));

        Assert.Contains(VendorService.ResultCantAfford.ToString(), response);
    }

    [Fact]
    public async Task Session_TreatsAnEntryAsAVendorOnlyWhenItsContentSaysSo()
    {
        var content = GameContent.Development with
        {
            Npcs = new Dictionary<uint, NpcDefinition> { [9001] = new(9001, NpcFlags.None, OffersQuests: true) },
        };
        var session = await SessionAsync(content);

        var response = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.VendorOpen, 3, new { networkId = "Creature:1" }))));

        Assert.Contains("invalid_vendor", response);
    }

    [Fact]
    public async Task Session_OffersOnlyTheQuestsItsContentHolds()
    {
        var session = await SessionAsync(GameContent.Development with { Quests = [] });

        var response = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.QuestList, 3, new { networkId = "Creature:1" }))));

        Assert.Contains("\"quests\":[]", response);
    }

    [Fact]
    public async Task Session_SeedsEveryEntryItsContentNamesAsAStartingItem()
    {
        var content = GameContent.Development with
        {
            StartingItems = [new StartingItemStack(100, 1), new StartingItemStack(9301, 4)],
        };

        var response = await EnterWorldRepliesAsync(content);

        Assert.Contains(response, message =>
            message.Contains("\"id\":\"starter-item\"") && message.Contains("\"entry\":100"));
        Assert.Contains(response, message =>
            message.Contains("\"id\":\"starter-item-1\"") && message.Contains("\"entry\":9301"));
    }

    [Fact]
    public async Task Session_SeedsAStartingItemAtItsOwnStackCount()
    {
        var content = GameContent.Development with
        {
            StartingItems = [new StartingItemStack(100, 1), new StartingItemStack(9301, 4)],
        };

        var response = await EnterWorldRepliesAsync(content);

        Assert.Contains(response, message =>
            message.Contains("\"entry\":9301") && message.Contains("\"count\":4"));
    }

    [Fact]
    public async Task Session_SeedsTheCarriedHalfOfItsOwnCreationLoadout()
    {
        var response = await EnterWorldRepliesAsync(GameContent.Development);

        Assert.Contains(response, message =>
            message.Contains("\"entry\":1168") && message.Contains("\"count\":5"));
        Assert.Contains(response, message =>
            message.Contains("\"entry\":3000") && message.Contains("\"count\":20"));
        Assert.Contains(response, message =>
            message.Contains("\"entry\":3008") && message.Contains("\"count\":5"));
    }

    [Fact]
    public async Task Session_SeedsNoItemsWhenItsContentNamesNone()
    {
        var content = GameContent.Development with { StartingItems = [] };

        var response = await EnterWorldRepliesAsync(content);

        var snapshot = response.Single(message => message.Contains("\"type\":\"inventory.updated\""));
        using var document = System.Text.Json.JsonDocument.Parse(snapshot);
        Assert.DoesNotContain(
            document.RootElement.GetProperty("payload").GetProperty("items").EnumerateArray(),
            item => item.GetProperty("slot").GetProperty("area").GetString() == "bag");
    }

    private static async Task<IReadOnlyList<string>> EnterWorldRepliesAsync(GameContent content)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([TravelSession.Character()]), TravelSession.Spawns,
            TravelSession.StaticSpawns, content);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        return await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
    }

    [Fact]
    public async Task Session_CanAcceptAndCompleteTheOpeningQuestChainForAReward()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 1, 25.6f, 3f, -13.2f, 1, [])]),
            [
                new CreatureSpawnDefinition(1010, 1, 9010, 24, 3, 4, 3.8f),
                new CreatureSpawnDefinition(1011, 1, 9011, 27, 3, -30, 3.1f),
            ],
            null, GameContent.Development);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var step = await WalkTo(session, 2, (25.6f, 3f, -13.2f), (24f, 3f, 4f));
        var accepted = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestAccept, step++, new { networkId = "Creature:1", questId = 5010u })));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestComplete, step++, new { networkId = "Creature:1", questId = 5010u, rewardIndex = 0 })));
        step = await WalkTo(session, step, (24f, 3f, 4f), (27f, 3f, -30f));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestAccept, step++, new { networkId = "Creature:2", questId = 5011u })));
        var completed = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestComplete, step++, new { networkId = "Creature:2", questId = 5011u, rewardIndex = 0 })));

        Assert.Contains(accepted, message => message.Contains("\"type\":\"quest.updated\"") && message.Contains("\"status\":\"underway\""));
        Assert.Contains(completed, message => message.Contains("\"type\":\"quest.updated\"") && message.Contains("\"status\":\"complete\""));
        Assert.Contains(completed, message => message.Contains("\"type\":\"inventory.itemAdded\"") && message.Contains("\"entry\":9301"));
    }

    [Fact]
    public async Task Session_CanAcceptAndCompleteTheClassSpecificTutorialChainForRewards()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 0, 3, 1, 1, 1, 1, 1, 1, 3.5f, 7f, 64f, 1,
                EquipmentSlots.Build((EquipmentSlots.Ranged, 4149)))]),
            [
                new CreatureSpawnDefinition(1012, 1, 9012, 2, 7, 63, 0),
                new CreatureSpawnDefinition(1013, 1, 9013, 5, 7, 64, 0),
            ],
            null, GameContent.Development);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var refused = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.QuestAccept, 2, new { networkId = "Creature:2", questId = 5014u }))));
        Assert.Contains("\"type\":\"protocol.error\"", refused);

        var accepted = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestAccept, 3, new { networkId = "Creature:1", questId = 5013u })));
        var completedFirst = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestComplete, 4, new { networkId = "Creature:2", questId = 5013u, rewardIndex = 0 })));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestAccept, 5, new { networkId = "Creature:2", questId = 5014u })));

        var early = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.QuestComplete, 6,
                new { networkId = "Creature:2", questId = 5014u, rewardIndex = 0 }))));
        Assert.Contains("invalid_quest", early);

        var castAt = DateTimeOffset.UtcNow;
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 7, new { spellId = 5692u, targetId = "Creature:2" })));
        session.TickCombat(castAt + TimeSpan.FromSeconds(1));

        var completedSecond = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestComplete, 8, new { networkId = "Creature:2", questId = 5014u, rewardIndex = 0 })));

        Assert.Contains(accepted, message => message.Contains("\"type\":\"quest.updated\"") && message.Contains("\"status\":\"underway\""));
        Assert.Contains(completedFirst, message => message.Contains("\"type\":\"quest.updated\"") && message.Contains("\"status\":\"complete\""));
        Assert.Contains(completedSecond, message => message.Contains("\"type\":\"inventory.itemAdded\"") && message.Contains("\"entry\":1168"));
        Assert.Contains(completedSecond, message => message.Contains("\"type\":\"inventory.itemAdded\"") && message.Contains("\"entry\":9305"));
    }

    [Fact]
    public async Task Session_SellsVendorGoodsWithoutStockLimit()
    {
        var content = GameContent.Development with { StartingItems = [] };
        var session = await SessionAsync(content);
        for (var purchase = 0; purchase < 12; purchase++)
            Assert.Contains(
                VendorService.ResultOk.ToString(), string.Join(' ', await BuyAsync(session)));

        Assert.Contains(
            VendorService.ResultBagsFull.ToString(), string.Join(' ', await BuyAsync(session)));
    }

    [Fact]
    public async Task Session_GivesEachVendorItsOwnStockByEntry()
    {
        var content = GameContent.Development with
        {
            Npcs = new Dictionary<uint, NpcDefinition>
            {
                [9001] = new(9001, NpcFlags.Vendor),
                [9003] = new(9003, NpcFlags.Vendor),
            },
            Creatures = new Dictionary<uint, CreatureDefinition>
            {
                [9001] = new(9001, "creature-9001", 3, 100),
                [9003] = new(9003, "creature-9003", 3, 100),
            },
            VendorTabs = new Dictionary<uint, IReadOnlyList<VendorTab>>
            {
                [9001] = [new VendorTab(0, 0, 1, true, [new VendorItem(0, 100, 5001, 700, 50, 10, 1)])],
                [9003] = [new VendorTab(0, 0, 1, true, [new VendorItem(0, 400, 5401, 701, 999, 1, 1)])],
            },
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([TravelSession.Character()]),
            [
                new CreatureSpawnDefinition(1, 13, 9001, 2f, 2f, 3f, 0f),
                new CreatureSpawnDefinition(2, 13, 9003, 2.5f, 2f, 3f, 0f),
            ],
            null, content, combatRollUnit: () => 1.0);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var first = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.VendorOpen, 3, new { networkId = "Creature:1" }))));
        var second = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.VendorOpen, 4, new { networkId = "Creature:2" }))));

        Assert.Contains("\"entry\":100", first);
        Assert.DoesNotContain("\"entry\":400", first);
        Assert.Contains("\"entry\":400", second);
        Assert.DoesNotContain("\"entry\":100", second);
    }

    [Fact]
    public async Task Session_FightsOverTheCreaturesOwnHealthPoolOnceItsContentCarriesOne()
    {
        var content = GameContent.Development with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>
            {
                [9001] = new(9001, "creature-9001", 3, MaxHealth: 24, MinDamage: 1, MaxDamage: 1),
            },
        };
        var session = await SessionAsync(content);
        await AttackAsync(session);

        var first = session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2));
        Assert.DoesNotContain(first, message => message.Contains("unit.died"));

        var second = session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(4));
        Assert.Contains(second, message => message.Contains("unit.died"));
    }

    [Fact]
    public async Task Session_UsesTheCreaturesHealthEvenWhenNoDamageRangeIsKnownYet()
    {
        var content = GameContent.Development with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>
            {
                [9001] = new(9001, "creature-9001", 3, MaxHealth: 24),
            },
        };
        var session = await SessionAsync(content);
        await AttackAsync(session);

        var first = session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2));
        Assert.DoesNotContain(first, message => message.Contains("unit.died"));

        var second = session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(4));
        Assert.Contains(second, message => message.Contains("unit.died"));
    }

    [Fact]
    public async Task Session_RollsLootMoneyFromTheCreaturesOwnRangeOnceItHasOne()
    {
        var content = GameContent.Development with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>
            {
                [9001] = new(9001, "creature-9001", 3, 100, MoneyDropMin: 77, MoneyDropMax: 77),
            },
        };
        var session = await SessionAsync(content);
        await KillAsync(session);

        var contents = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.LootOpen, 3, new { networkId = "Creature:1" })));

        Assert.Contains(contents, message => message.Contains("\"money\":77"));
    }

    [Fact]
    public async Task Session_RespawnsOnTheCreaturesOwnDelayOnceItHasOne()
    {
        var content = GameContent.Development with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>
            {
                [9001] = new(9001, "creature-9001", 3, 100,
                    RespawnTimeMinSeconds: 5, RespawnTimeMaxSeconds: 5),
            },
        };
        var session = await SessionAsync(content);
        var now = await KillAsync(session);

        var despawnTickNow = now + TimeSpan.FromSeconds(150);
        var removed = session.TickCombat(despawnTickNow);
        Assert.Contains(removed, message => message.Contains("entity.removed"));

        var tooSoon = session.TickCombat(despawnTickNow + TimeSpan.FromSeconds(3));
        Assert.DoesNotContain(tooSoon, message => message.Contains("entity.spawned"));

        var respawned = session.TickCombat(despawnTickNow + TimeSpan.FromSeconds(6));
        Assert.Contains(respawned, message => message.Contains("entity.spawned"));
    }

    private static async Task AttackAsync(PlaySession session) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));

    private static async Task<DateTimeOffset> KillAsync(PlaySession session)
    {
        await AttackAsync(session);
        var now = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        for (var swing = 0; swing < 20; swing++)
        {
            session.TickCombat(now);
            now += TimeSpan.FromSeconds(2);
        }

        return now;
    }

    private static async Task<IReadOnlyList<string>> BuyAsync(PlaySession session) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.VendorBuy, 3,
            new { networkId = "Creature:1", slot = 0, entry = 100, count = 1 })));

    private static async Task<PlaySession> SessionAsync(GameContent content)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([TravelSession.Character()]),
            [new CreatureSpawnDefinition(1, 13, 9001, 2f, 2f, 3f, 0f)], null, content,
            combatRollUnit: () => 1.0);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }

    private static async Task<int> WalkTo(
        PlaySession session, int sequence, (float X, float Y, float Z) from, (float X, float Y, float Z) to)
    {
        const float StepLength = 10f;
        var distance = MathF.Sqrt(
            ((to.X - from.X) * (to.X - from.X))
            + ((to.Y - from.Y) * (to.Y - from.Y))
            + ((to.Z - from.Z) * (to.Z - from.Z)));
        var steps = Math.Max(1, (int)MathF.Ceiling(distance / StepLength));
        for (var step = 1; step <= steps; step++)
        {
            var t = (float)step / steps;
            await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.PlayerMove, sequence++, new PlayerMovePayload(
                    from.X + ((to.X - from.X) * t),
                    from.Y + ((to.Y - from.Y) * t),
                    from.Z + ((to.Z - from.Z) * t),
                    0, true, MovementState.Walking))));
        }

        return sequence;
    }
}
