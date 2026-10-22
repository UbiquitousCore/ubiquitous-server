namespace Ubiquitous.Scripts;

public sealed class AndareScript : CreatureScript
{
    public static readonly AndareScript Instance = new();

    private AndareScript() { }

    public override string Name => "npc.andare";

    public override bool NoticesEnemies => false;

    public override bool FightsBack => false;

    public override bool Chases => false;

    public override bool DrawsOthersIn => false;

    public override IReadOnlyList<string> Shouts =>
    [
        "I'll tell you again: the judgment on me is not justice!",
        "Forget about it! I will never join you!",
        "It's a blessing from the Lord of the Dead!",
    ];
}
