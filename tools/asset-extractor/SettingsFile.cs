using System.Reflection;

namespace Ubiquitous.AssetExtractor;

// The `.env` beside docker-compose.yml, written on the first import when
// there is none: env.example with the answers to a few questions, or as it
// stands when nobody is at a terminal to ask. An existing file is never
// touched. The import writes it because it is the one step every operator
// runs from that folder; the server's container cannot see it.
public static class SettingsFile
{
    public const string FileName = ".env";

    // Writes `.env` into `directory` if it is missing. False when it was there already.
    public static bool EnsureExists(string directory, TextReader input, TextWriter output, bool interactive)
    {
        var path = Path.Combine(directory, FileName);
        if (File.Exists(path)) return false;

        var answers = interactive ? Ask(input, output) : [];
        File.WriteAllText(path, Fill(Template(), answers));
        output.WriteLine($"Wrote {FileName} beside docker-compose.yml. Every setting is in it; change one and run `docker compose up -d`.");
        return true;
    }

    // The setup questions, each with its default in brackets; Enter takes the default.
    public static List<(string Name, string Value)> Ask(TextReader input, TextWriter output)
    {
        output.WriteLine("No .env yet: a few questions to write one. Enter keeps the default.");
        var answers = new List<(string Name, string Value)>();

        var site = Prompt(input, output,
            "Address other devices will open (a LAN address such as 192.168.1.20, or a domain)", "localhost");
        if (!string.Equals(site, "localhost", StringComparison.OrdinalIgnoreCase))
            answers.Add(("UBIQUITOUS_SITE", site));

        if (YesNo(input, output, "Seasonal festival scenery, creatures and items?"))
            answers.Add(("UBIQUITOUS_FESTIVAL_CONTENT_ENABLED", "true"));

        if (YesNo(input, output, "Bots that play on their own?"))
        {
            answers.Add(("UBIQUITOUS_MODULE_PLAYERBOTS_ENABLED", "true"));
            var count = Prompt(input, output, "How many bots (0-500)", "5");
            if (int.TryParse(count, out var bots) && bots is >= 0 and <= 500)
                answers.Add(("UBIQUITOUS_MODULE_PLAYERBOTS_RANDOM_BOT_COUNT", bots.ToString()));
        }

        return answers;
    }

    // Each NAME=value replaces the line that sets NAME, or the commented line
    // showing it, so it lands beside its explanation; otherwise it is appended.
    public static string Fill(string template, IReadOnlyList<(string Name, string Value)> values)
    {
        var lines = template.Replace("\r\n", "\n").Split('\n').ToList();
        foreach (var (name, value) in values)
        {
            var at = lines.FindIndex(line => line.StartsWith(name + "=", StringComparison.Ordinal));
            if (at < 0) at = lines.FindIndex(line => line.StartsWith("# " + name + "=", StringComparison.Ordinal));
            if (at >= 0) lines[at] = $"{name}={value}";
            else lines.Add($"{name}={value}");
        }

        return string.Join('\n', lines);
    }

    private static string Template()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("env.example")
            ?? throw new InvalidOperationException("env.example is not built into the extractor");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string Prompt(TextReader input, TextWriter output, string question, string fallback)
    {
        output.Write($"{question} [{fallback}]: ");
        var answer = input.ReadLine()?.Trim();
        return string.IsNullOrEmpty(answer) ? fallback : answer;
    }

    private static bool YesNo(TextReader input, TextWriter output, string question)
    {
        output.Write($"{question} [y/N]: ");
        var answer = input.ReadLine()?.Trim();
        return answer is not null && answer.StartsWith("y", StringComparison.OrdinalIgnoreCase);
    }
}
