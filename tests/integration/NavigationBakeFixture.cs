using Ubiquitous.AssetExtractor;
using Ubiquitous.Data;

namespace Ubiquitous.Integration.Tests;

internal static class NavigationBakeFixture
{
    public static string Write(string directory, ushort mapId, IReadOnlyList<BakeTriangle> triangles)
    {
        var result = NavigationBaker.Bake(triangles);
        var path = Path.Combine(directory, NavigationPackage.FileName(mapId));
        using var stream = File.Create(path);
        NavigationPackage.Write(
            stream, new NavigationPackageFile(mapId, result.Parameters, NavigationBaker.Serialise(result.Mesh)));
        return path;
    }
}
