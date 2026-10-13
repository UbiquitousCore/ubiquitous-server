namespace Ubiquitous.Server;

public static class ContentDirectory
{
    public const string Name = "content";

    private const string RepositoryMarker = "ubiquitous-server.sln";

    public static string Find() => Find(AppContext.BaseDirectory);

    public static string Find(string startingAt)
    {
        for (var directory = new DirectoryInfo(startingAt); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, RepositoryMarker)))
                return Path.Combine(directory.FullName, Name);
        }

        return Path.Combine(startingAt, Name);
    }
}
