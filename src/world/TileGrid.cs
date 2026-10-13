using Ubiquitous.Common;

namespace Ubiquitous.World;

public static class TileGrid
{
    public const float TileSize = 16f;

    public const int ActivationRadiusTiles = 2;

    public static readonly TimeSpan SleepGrace = TimeSpan.FromMinutes(3);

    public static (int X, int Z) TileOf(float x, float z) =>
        ((int)Math.Floor(x / TileSize), (int)Math.Floor(z / TileSize));

    public static bool IsActive(Position home, Position player)
    {
        var (homeX, homeZ) = TileOf(home.X, home.Z);
        var (playerX, playerZ) = TileOf(player.X, player.Z);
        return Math.Abs(homeX - playerX) <= ActivationRadiusTiles
            && Math.Abs(homeZ - playerZ) <= ActivationRadiusTiles;
    }
}
