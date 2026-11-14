using Ubiquitous.AssetExtractor;
using Ubiquitous.Data;

namespace Ubiquitous.Integration.Tests;

public sealed class QuestItemSourcesTests
{
    private const uint Giver = 1;

    private static CreatureDefinition Creature(uint entry, string name, int level, int faction = UnitFactionTypes.Hostile) =>
        new(entry, name, level, 100, FactionType: faction);

    private static CreatureSpawnDefinition Spawn(uint id, uint entry, ushort map = 1) => new(id, map, entry, 0, 0, 0, 0);

    private static QuestDefinition Quest(uint id, uint item, int level = 10, uint objectiveText = 0, uint details = 0,
        uint killCreature = 0) =>
        new(id, Giver, 0, details, 1, Level: level, Objectives:
        [
            new QuestObjectiveDefinition(0, QuestObjectiveKind.Held, 5, ObjectiveItem: item, ObjectiveStringId: objectiveText),
            .. killCreature == 0 ? [] : new[] { new QuestObjectiveDefinition(1, QuestObjectiveKind.Killed, 5, ObjectiveCreature: killCreature) },
        ]);

    private static IReadOnlyList<QuestItemSourceDefinition> Find(
        IReadOnlyList<QuestDefinition> quests, IEnumerable<ItemDefinition> items, IReadOnlyList<CreatureDefinition> creatures,
        IReadOnlyDictionary<uint, string>? strings = null, IReadOnlyList<StaticObjectItemBindingDefinition>? lying = null,
        IReadOnlyList<CreatureSpawnDefinition>? spawns = null) =>
        QuestItemSources.Find(quests, items.ToDictionary(item => item.Id), creatures,
            spawns ?? [Spawn(1, Giver), .. creatures.Select(creature => Spawn(100 + creature.EntryId, creature.EntryId))],
            strings ?? new Dictionary<uint, string>(), lying ?? []);

    private static ItemDefinition Item(uint id, string name) => new(id, name, 10, 0, 0);

    private static readonly CreatureDefinition QuestGiver = Creature(Giver, "Edgar", 10, UnitFactionTypes.Townsfolk);

    [Fact]
    public void AQuestThatAsksForKillsToo_NamesItsKillsAsTheCarriers()
    {
        var sources = Find([Quest(1, 50, killCreature: 20)], [Item(50, "Medallion")],
            [QuestGiver, Creature(20, "Bandit", 10), Creature(21, "Medallion Keeper", 10)]);

        Assert.Equal([20u], Assert.Single(sources).CreatureEntryIds);
    }

    [Fact]
    public void AnItemNamedAfterACreature_DropsFromIt_TheLongestNameWinning()
    {
        var sources = Find([Quest(1, 50)], [Item(50, "Goblin Slasher's Ear")],
            [QuestGiver, Creature(20, "Goblin", 10), Creature(21, "Goblin Slasher", 10), Creature(22, "Goblin Slasher", 12)]);

        Assert.Equal([21u, 22u], Assert.Single(sources).CreatureEntryIds);
    }

    [Fact]
    public void TheObjectiveText_ComesBeforeTheStory_AndMarkupIsSkipped()
    {
        var strings = new Dictionary<uint, string>
        {
            [7] = "10 pelts from \u0012b\u0013Gray Wolves\u0012/b\u0013",
            [8] = "The bears and the Gray Wolves took everything.",
        };

        var sources = Find([Quest(1, 50, objectiveText: 7, details: 8)], [Item(50, "Pelt")],
            [QuestGiver, Creature(20, "Gray Wolf", 10), Creature(21, "Bear", 10)], strings);

        Assert.Equal([20u], Assert.Single(sources).CreatureEntryIds);
    }

    [Fact]
    public void ANamedCreature_FarFromTheQuestsLevel_OrTownsfolk_CarriesNothing()
    {
        var sources = Find([Quest(1, 50), Quest(2, 51)], [Item(50, "Orc Tusk"), Item(51, "Guard Badge")],
            [QuestGiver, Creature(20, "Orc", 30), Creature(21, "Guard", 10, UnitFactionTypes.Townsfolk)]);

        Assert.Empty(sources);
    }

    [Fact]
    public void AWordOfACreaturesName_NamesIt_OnlyOnTheGiversMap()
    {
        var spawns = new[] { Spawn(1, Giver), Spawn(2, 20), Spawn(3, 21, map: 2) };

        var sources = Find([Quest(1, 50)], [Item(50, "Tender Wolf Loin")],
            [QuestGiver, Creature(20, "Gray Wolf", 10), Creature(21, "Snow Wolf", 10)], spawns: spawns);

        Assert.Equal([20u], Assert.Single(sources).CreatureEntryIds);
    }

    [Fact]
    public void AnItemLyingInTheWorldForItsQuest_IsNotLookedForOnCreatures()
    {
        var sources = Find([Quest(1, 50)], [Item(50, "Bandit Crate")], [QuestGiver, Creature(20, "Bandit", 10)],
            lying: [new StaticObjectItemBindingDefinition(9, 1, 50)]);

        Assert.Empty(sources);
    }
}
