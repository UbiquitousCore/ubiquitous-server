namespace Ubiquitous.AssetExtractor;

// An `achievement` row names its category's string in column 1 and holds up to ten
// conditions: their types in columns 7-16, three targets each in 17-46 and their values
// in 47-56. `achievement_type` row 30 is "Player Level", whose value is the level the
// achievement asks for. Only the "Level" category counts: two achievements elsewhere in
// 4.2.2 ask for level 99 while their own text says 69 or names a festival quest.
public static class AchievementColumns
{
    public const int Category = 1;
    public const string LevelCategory = "Level";
    public const int FirstConditionType = 7;
    public const int FirstConditionValue = 47;
    public const int ConditionCount = 10;
    public const int PlayerLevelCondition = 30;

    public static int? HighestPlayerLevelAskedFor(FTableDocument achievements, IReadOnlyDictionary<uint, string> strings)
    {
        int? highest = null;
        foreach (var row in achievements.Rows)
        {
            if (Number(row, Category) is not int category
                || !strings.TryGetValue((uint)category, out var name) || name != LevelCategory) continue;
            for (int condition = 0; condition < ConditionCount; condition++)
            {
                if (Number(row, FirstConditionType + condition) != PlayerLevelCondition) continue;
                if (Number(row, FirstConditionValue + condition) is int level && level > 0)
                    highest = Math.Max(highest ?? 0, level);
            }
        }

        return highest;
    }

    private static int? Number(IReadOnlyList<object> row, int column) =>
        column < row.Count ? row[column] switch { int value => value, uint value => (int)value, _ => null } : null;
}
