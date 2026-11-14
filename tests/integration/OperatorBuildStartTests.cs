using Microsoft.Extensions.Logging.Abstractions;
using Ubiquitous.AssetExtractor;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

// Points at the operator's own client build (an .ipa, its app directory or a directory
// holding the packs); skipped when unset, and when the build is 1.1.4, whose starts were seen.
public sealed class OperatorBuildStartTests
{
    [Fact]
    public void ALaterBuild_ImportsALevelCap_AndStartsEveryClassWithSpellsOfItsOwn()
    {
        if (Environment.GetEnvironmentVariable("UBIQUITOUS_TEST_CLIENT_BUILD") is not { Length: > 0 } build) return;
        var package = Directory.CreateTempSubdirectory("ubiquitous-operator-");
        var opened = BuildPacks.Open(build, ["tables.bin"]);
        try
        {
            var entries = TableArchiveReader.ReadEntries(Assert.Single(opened).ReadAllBytes());
            PackageImporter.ImportBuild114(entries, package.FullName, "operator");
            var catalog = DataCatalog.LoadFromDirectory(package.FullName);
            if (DataManifest.IsTargetGameVersion(catalog.Manifest.GameVersion)) return;

            var content = new ServerContentLoader(catalog, ServerOptions.Defaults, NullLogger.Instance, ContentDirectory.Find())
                .Load().Game;

            Assert.True(catalog.Manifest.LevelCap > LevelingService.SeenMaxLevel);
            Assert.Equal(catalog.Manifest.LevelCap, content.LevelCap);
            Assert.Contains(PlayerRaceIds.Mendel, content.PlayableRaces);
            var classes = content.Loadouts.Select(loadout => loadout.PlayerClass).ToHashSet();
            Assert.Contains(PlayerClassIds.FlameKnight, classes);
            foreach (var loadout in content.Loadouts)
            {
                Assert.All(loadout.Spells, spell => Assert.True(content.Spells.ContainsKey(spell)));
                Assert.All(loadout.Items, item => Assert.True(content.Items.ContainsKey(item.ItemEntry)));
                Assert.Equal("Auto Attack", content.Spells[loadout.ActionBar[0].SpellId].NameKey);
                Assert.True(content.Maps.ContainsKey(loadout.MapId));
            }

            var talentNames = content.TalentTrees.SelectMany(tree => tree.Talents)
                .Select(talent => content.Spells[talent.RankSpellIds[0]].NameKey).ToList();
            Assert.DoesNotContain(talentNames, name => System.Text.RegularExpressions.Regex.IsMatch(name, @"^spell-\d+$"));
            Assert.DoesNotContain(talentNames, name => name.StartsWith("Permanently increases", StringComparison.Ordinal));

            Assert.Contains(content.StorageUpgrades, rung => rung is { Area: "bag", FromSlotCount: 12, PriceCurrency: 0 });
            Assert.Contains(content.StorageUpgrades, rung => rung is { Area: "bank", FromSlotCount: 24, PriceCurrency: 0 });
            Assert.All(content.StorageUpgrades.GroupBy(rung => (rung.Area, rung.FromSlotCount)), rungs => Assert.Single(rungs));
            foreach (var playerClass in classes)
            {
                var start = content.Loadouts.First(loadout => loadout.PlayerClass == playerClass);
                var whole = ClassSpellbook.WholeBookAt(content, playerClass, content.LevelCap);
                Assert.Superset(content.Spellbook!.Classes[playerClass].ClassSpells.ToHashSet(), whole.ToHashSet());
                if (playerClass != PlayerClassIds.FlameKnight) Assert.True(whole.Count > start.Spells.Count);
                Assert.All(whole, spell => Assert.True(content.Spells.ContainsKey(spell)));
            }

            var mage = content.LoadoutFor(PlayerRaceIds.Human, PlayerClassIds.Mage)!;
            Assert.Equal(["Fireball", "Frost Bolt", "Energy Field"],
                mage.Spells.Take(3).Select(spell => content.Spells[spell].NameKey));
        }
        finally
        {
            foreach (var pack in opened) pack.Dispose();
            package.Delete(recursive: true);
        }
    }
}
