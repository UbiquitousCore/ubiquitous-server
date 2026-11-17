using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class StartingLoadoutsTests
{
    private static CharacterStartingLoadout Loadout(int race, int playerClass)
    {
        var loadout = StartingLoadouts.For(race, playerClass);
        Assert.NotNull(loadout);
        return loadout;
    }

    [Fact]
    public void All_CoversEveryPlayableRaceAndClassPairing_Once()
    {
        Assert.Equal(
            PlayerRaceIds.All.Count * PlayerClassIds.All.Count,
            StartingLoadouts.All.Count);
        Assert.Equal(
            StartingLoadouts.All.Count,
            StartingLoadouts.All.Select(l => (l.Race, l.PlayerClass)).Distinct().Count());

        foreach (var race in PlayerRaceIds.All)
        {
            foreach (var playerClass in PlayerClassIds.All)
            {
                Assert.NotNull(StartingLoadouts.For(race, playerClass));
            }
        }
    }

    [Fact]
    public void For_MatchesOnRaceAndClassTogether_NotEither()
    {
        Assert.Null(StartingLoadouts.For(PlayerRaceIds.Human, playerClass: 99));
        Assert.Null(StartingLoadouts.For(race: 99, PlayerClassIds.Ranger));
        Assert.NotEqual(
            Loadout(PlayerRaceIds.Human, PlayerClassIds.Ranger).Attributes,
            Loadout(PlayerRaceIds.Orc, PlayerClassIds.Ranger).Attributes);
    }

    [Fact]
    public void HumanRanger_StartsAtTheHumanStartPointWithItsOwnAttributes()
    {
        var ranger = Loadout(PlayerRaceIds.Human, PlayerClassIds.Ranger);

        Assert.Equal(1, ranger.MapId);
        Assert.Equal(2f, ranger.X);
        Assert.Equal(7f, ranger.Y);
        Assert.Equal(55f, ranger.Z);
        Assert.Equal(0f, ranger.Orientation);
        Assert.Equal(new StartingAttributes(13, 22, 18, 10), ranger.Attributes);
    }

    [Fact]
    public void HumanRanger_WearsAndCarriesExactlyTheCreationKit()
    {
        var ranger = Loadout(PlayerRaceIds.Human, PlayerClassIds.Ranger);
        var worn = ranger.WornEquipment();

        Assert.Equal(1053, worn[EquipmentSlots.Chest]);
        Assert.Equal(1060, worn[EquipmentSlots.Legs]);
        Assert.Equal(1063, worn[EquipmentSlots.Feet]);
        Assert.Equal(3907, worn[EquipmentSlots.MainHand]);
        Assert.Equal(3907, worn[EquipmentSlots.OffHand]);
        Assert.Equal(4149, worn[EquipmentSlots.Ranged]);
        Assert.Equal(0, worn[EquipmentSlots.Head]);
        Assert.Equal(0, worn[EquipmentSlots.Back]);

        Assert.Equal(
            [(1168u, 17, 5), (3000u, 18, 20), (3008u, 19, 5)],
            ranger.BagItems.Select(item => (item.ItemEntry, item.Slot, item.Count)));
    }

    [Fact]
    public void WornPieces_AreHandedOverWithNoCount()
    {
        foreach (var loadout in StartingLoadouts.All)
        {
            Assert.All(loadout.Items.Where(item => item.IsWorn), item => Assert.Equal(0, item.Count));
        }
    }

    [Fact]
    public void WarriorKit_IsExactlyItsEightEntries()
    {
        var warrior = Loadout(PlayerRaceIds.Human, PlayerClassIds.Warrior);

        Assert.Equal(
            [1103u, 1168u, 1213u, 3000u, 3007u, 3008u, 3070u, 3071u],
            warrior.Items.Select(item => item.ItemEntry).OrderBy(entry => entry));
    }

    [Fact]
    public void Kits_AreTheSameForEveryRaceOfAClass()
    {
        foreach (var playerClass in PlayerClassIds.All)
        {
            var expected = Loadout(PlayerRaceIds.Human, playerClass).Items;
            foreach (var race in PlayerRaceIds.All)
            {
                Assert.Equal(expected, Loadout(race, playerClass).Items);
            }
        }
    }

    [Fact]
    public void Spellbooks_EndInTheirRacesRidingPermit()
    {
        Assert.Equal(
            [5692u, 5815u, 5813u, 23u, 57u, 58u, 108u, 648u, 690u, 1822u, 111u, 6995u],
            Loadout(PlayerRaceIds.Human, PlayerClassIds.Ranger).Spells);

        Assert.Equal(6996u, Loadout(PlayerRaceIds.Elf, PlayerClassIds.Warrior).Spells[^1]);
        Assert.Equal(6997u, Loadout(PlayerRaceIds.Orc, PlayerClassIds.Mage).Spells[^1]);
        Assert.Equal(6998u, Loadout(PlayerRaceIds.Undead, PlayerClassIds.Monk).Spells[^1]);
        Assert.Equal(89u, Loadout(PlayerRaceIds.Mendel, PlayerClassIds.Monk).Spells[^1]);

        foreach (var race in PlayerRaceIds.All)
        {
            var warriorHead = Loadout(race, PlayerClassIds.Warrior).Spells.Take(3);
            Assert.Equal([5233u, 5253u, 5350u], warriorHead);
        }
    }

    [Fact]
    public void Validate_PassesForTheDevPairingAgainstTheDevelopmentCatalogue()
    {
        StartingLoadouts.Validate([GameContent.DevelopmentLoadout], GameContent.Development.Spells);
    }

    [Fact]
    public void ValidateAgainst_PassesForEveryPairingAgainstAWholeCatalogue()
    {
        StartingLoadouts.ValidateAgainst(WholeCatalogue());
    }

    [Fact]
    public void ValidateAgainst_FailsLoudlyOnAnUndefinedId()
    {
        var withoutPermit = WholeCatalogue()
            .Where(entry => entry.Key != 6995)
            .ToDictionary(entry => entry.Key, entry => entry.Value);

        var error = Assert.Throws<InvalidOperationException>(
            () => StartingLoadouts.ValidateAgainst(withoutPermit));

        Assert.Contains("unknown spell 6995", error.Message);
    }

    private static Dictionary<uint, SpellDefinition> WholeCatalogue() =>
        StartingLoadouts.All
            .SelectMany(loadout => loadout.Spells)
            .Distinct()
            .ToDictionary(id => id, id => new SpellDefinition(id, $"spell-{id}", 0, 0, 0f));

    [Fact]
    public void ActionBar_StartsWithAutoAttackThenTheClassOpener()
    {
        foreach (var loadout in StartingLoadouts.All)
        {
            Assert.Equal(2, loadout.ActionBar.Count);
            Assert.Equal(new StartingActionButton(0, 108), loadout.ActionBar[0]);
            Assert.Equal(loadout.Spells[0], loadout.ActionBar[1].SpellId);
            Assert.Equal(1, loadout.ActionBar[1].Slot);
        }
    }

    [Fact]
    public void StartPoints_FollowTheRaceNotTheClass()
    {
        foreach (var playerClass in PlayerClassIds.All)
        {
            var human = Loadout(PlayerRaceIds.Human, playerClass);
            var elf = Loadout(PlayerRaceIds.Elf, playerClass);
            var orc = Loadout(PlayerRaceIds.Orc, playerClass);
            var undead = Loadout(PlayerRaceIds.Undead, playerClass);
            var mendel = Loadout(PlayerRaceIds.Mendel, playerClass);

            Assert.Equal((1, 2f, 7f, 55f), (human.MapId, human.X, human.Y, human.Z));
            Assert.Equal((human.MapId, human.X, human.Y, human.Z), (elf.MapId, elf.X, elf.Y, elf.Z));
            Assert.Equal((7, -2640f, 33f, 100f), (orc.MapId, orc.X, orc.Y, orc.Z));
            Assert.Equal((orc.MapId, orc.X, orc.Y, orc.Z), (undead.MapId, undead.X, undead.Y, undead.Z));
            Assert.Equal((6, -860f, -6.5f, 20f), (mendel.MapId, mendel.X, mendel.Y, mendel.Z));
        }
    }

    [Fact]
    public void EveryPairing_WakesUpAmongTheTrainersOfItsStartingArea_InTheOperatorsData()
    {
        var generated = Path.Combine(FindRepositoryRoot(), "data", "generated");
        var spawnsPath = Path.Combine(generated, "creatureSpawns.json");
        var npcsPath = Path.Combine(generated, "npcs.json");
        if (!File.Exists(spawnsPath) || !File.Exists(npcsPath)) return;

        const float Reach = 12f;
        const int MinimumNearby = 2;
        var questGivers = ReadEntries(npcsPath)
            .Where(npc => npc.TryGetProperty("offersQuests", out var offers) && offers.GetBoolean())
            .Select(npc => npc.GetProperty("entryId").GetUInt32())
            .ToHashSet();
        var spawns = ReadEntries(spawnsPath)
            .Where(spawn => questGivers.Contains(spawn.GetProperty("creatureEntryId").GetUInt32()))
            .Select(spawn => (
                Map: spawn.GetProperty("mapId").GetInt32(),
                X: spawn.GetProperty("x").GetSingle(),
                Z: spawn.GetProperty("z").GetSingle()))
            .ToList();

        foreach (var loadout in StartingLoadouts.All)
        {
            var nearby = spawns.Count(spawn =>
                spawn.Map == loadout.MapId
                && MathF.Sqrt(MathF.Pow(spawn.X - loadout.X, 2) + MathF.Pow(spawn.Z - loadout.Z, 2)) <= Reach);
            Assert.True(
                nearby >= MinimumNearby,
                $"race {loadout.Race}/class {loadout.PlayerClass} wakes up with {nearby} quest givers within {Reach}");
        }

        static IEnumerable<System.Text.Json.JsonElement> ReadEntries(string path)
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.EnumerateArray().Select(entry => entry.Clone()).ToList();
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ubiquitous-server.sln"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }

    [Fact]
    public void DevelopmentContent_IsBuiltFromTheHumanRangerLoadout()
    {
        var ranger = Loadout(PlayerRaceIds.Human, PlayerClassIds.Ranger);
        Assert.Equal(ranger, GameContent.DevelopmentLoadout);

        Assert.Equal(
            [
                100u,
                .. OperatorSpells.All,
                .. ranger.Spells,
                .. PlaytestSpellbook.RangerToLevel40.Where(spellId => !ranger.Spells.Contains(spellId)),
            ],
            GameContent.Development.StartingSpells);

        Assert.Equal(
            ranger.Spells,
            GameContent.Development.StartingSpells
                .Skip(1 + OperatorSpells.All.Count)
                .Take(ranger.Spells.Count)
                .ToArray());
        Assert.Equal(
            [new StartingItemStack(100, 1), new StartingItemStack(1168, 5), new StartingItemStack(3000, 20), new StartingItemStack(3008, 5)],
            GameContent.Development.StartingItems);
        Assert.Equal(
            [
                .. ranger.ActionBar,
                new StartingActionButton(2, OperatorSpells.Finish),
                new StartingActionButton(3, OperatorSpells.Restore),
            ],
            GameContent.Development.StartingActionBar);
    }
}
