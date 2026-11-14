using Ubiquitous.Game;
using Ubiquitous.Modules;
using Ubiquitous.Modules.Playerbots;

namespace Ubiquitous.Playerbots.Tests;

public sealed class PlayerbotSettingsTests
{
    [Fact]
    public void BotLevels_ReachTheBuildsOwnCap_AndNoFurther()
    {
        var asked = new ModuleSettings(new Dictionary<string, string> { ["minLevel"] = "70", ["maxLevel"] = "90" });

        var later = PlayerbotSettings.From(asked, levelCap: 75);
        var seen = PlayerbotSettings.From(asked, LevelingService.SeenMaxLevel);

        Assert.Equal((70, 75), (later.MinLevel, later.MaxLevel));
        Assert.Equal((60, 60), (seen.MinLevel, seen.MaxLevel));
    }
}
