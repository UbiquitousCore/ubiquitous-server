using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class SpellLearningTests
{
    private const uint Mage = 2;

    private static readonly IReadOnlyDictionary<uint, SpellDefinition> Spells = new Dictionary<uint, SpellDefinition>
    {
        [100] = new(100, "lesson-a", 0, 0, 0, EffectLearnSpellId: 5006),
        [101] = new(101, "lesson-b", 0, 0, 0, EffectLearnSpellId: 5007),
        [102] = new(102, "lesson-b-again", 0, 0, 0, EffectLearnSpellId: 5007),
    };

    private static ItemDefinition Lesson(uint id, uint spell, uint level, uint? classMask = 0) =>
        new(id, $"item-{id}", 4, 0, 0, OnUseSpellId: spell, RequiredLevel: level, AllowableClassMask: classMask);

    [Fact]
    public void ASpell_IsLearnedAtTheLevelOfTheLessonThatTeachesIt()
    {
        var levels = SpellLearning.LevelsFor([Lesson(1, 100, 6), Lesson(2, 101, 12)], Spells, (int)Mage);

        Assert.Equal(6, levels[5006]);
        Assert.Equal(12, levels[5007]);
    }

    [Fact]
    public void WhenSeveralLessonsTeachIt_TheLowestLevelCounts()
    {
        var levels = SpellLearning.LevelsFor([Lesson(1, 101, 18), Lesson(2, 102, 12)], Spells, (int)Mage);

        Assert.Equal(12, levels[5007]);
    }

    [Fact]
    public void ALessonForAnotherClass_IsNotCounted()
    {
        var warriorOnly = Lesson(1, 100, 6, classMask: ItemClassMask.BitFor(1));

        var levels = SpellLearning.LevelsFor([warriorOnly], Spells, (int)Mage);

        Assert.False(levels.ContainsKey(5006));
    }

    [Fact]
    public void ASpellNoLessonTeaches_TakesTheLevelItIsGiven()
    {
        var levels = SpellLearning.LevelsFor([], Spells, (int)Mage);

        Assert.Equal(1, SpellLearning.LevelOf(levels, 5000, whenNoItemTeachesIt: 1));
        Assert.Equal(60, SpellLearning.LevelOf(levels, 5000, whenNoItemTeachesIt: 60));
    }
}
