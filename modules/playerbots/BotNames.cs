namespace Ubiquitous.Modules.Playerbots;

// Names are letters only and 2-16 long, the same rule a player's name meets.
public static class BotNames
{
    private static readonly string[] Starts = ["Al", "Bra", "Cor", "Da", "El", "Fen", "Gar", "Hal", "Ir", "Jor", "Kel", "Lor", "Mar", "Nor", "Or", "Pel", "Quin", "Ros", "Sel", "Tor", "Ul", "Val", "Wen", "Yar", "Zan"];
    private static readonly string[] Middles = ["", "a", "e", "i", "o", "an", "en", "ar", "or", "il"];
    private static readonly string[] Ends = ["dor", "wyn", "ric", "mir", "las", "dan", "rik", "len", "nus", "ra", "th", "mund", "gar", "vin", "ra", "sa"];

    public static string Make(Random random) =>
        Starts[random.Next(Starts.Length)] + Middles[random.Next(Middles.Length)] + Ends[random.Next(Ends.Length)];
}
