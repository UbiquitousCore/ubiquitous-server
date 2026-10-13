namespace Ubiquitous.Integration.Tests;

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;

public sealed class WireVocabularyTests
{
    private const string FixtureName = "wire-vocabulary.json";

    private static IReadOnlyList<string> ConstantsOf(Type type) =>
        [.. type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

    public static JsonObject Vocabulary() => new()
    {
        ["messageTypes"] = Array(ConstantsOf(typeof(WebMessageTypes))),
        ["outcomes"] = Array(ConstantsOf(typeof(WireCodes.Outcome))),
        ["spellRefusals"] = Array(ConstantsOf(typeof(WireCodes.SpellRefusal))),
        ["itemOutcomes"] = Array(ConstantsOf(typeof(ItemErrors))),
        ["lootStates"] = Array(ConstantsOf(typeof(WireCodes.Loot))),
        ["choiceResults"] = Array(ConstantsOf(typeof(WireCodes.ChoiceResult))),
        ["questMarks"] = Array(Enum.GetValues<QuestMark>().Select(QuestService.WireStatus)),
        ["unitValues"] = Array(ConstantsOf(typeof(WireCodes.UnitValue))),
        ["hands"] = Array(ConstantsOf(typeof(WireCodes.Hand))),
        ["questShareResults"] = Array(ConstantsOf(typeof(WireCodes.ShareResult))),
        ["summonResults"] = Array(ConstantsOf(typeof(WireCodes.SummonResult))),
        ["talentRefusals"] = Array(ConstantsOf(typeof(WireCodes.TalentRefusal))),
    };

    private static JsonArray Array(IEnumerable<string> values) =>
        new([.. values.Select(value => (JsonNode)JsonValue.Create(value)!)]);

    private static string Canonical() =>
        Vocabulary().ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    [Fact]
    public void TheCommittedVocabularyIsWhatThisServerActuallySends()
    {
        var canonical = Canonical();
        if (Environment.GetEnvironmentVariable("UBIQUITOUS_WRITE_WIRE_FIXTURES") == "1")
        {
            var source = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "fixtures", FixtureName));
            File.WriteAllText(source, canonical + "\n");
        }

        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", FixtureName);
        Assert.True(File.Exists(path), $"Missing {path}. Regenerate with UBIQUITOUS_WRITE_WIRE_FIXTURES=1.");
        Assert.Equal(
            canonical.ReplaceLineEndings("\n"),
            File.ReadAllText(path).ReplaceLineEndings("\n").TrimEnd('\n'));
    }
}
