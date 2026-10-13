using System.Text;
using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public class WorldItemReaderTests
{
    internal sealed record Record(string Model, (float X, float Y, float Z) At)
    {
        public (float X, float Y, float Z, float W) Turn { get; init; } = (0f, 0f, 0f, 1f);

        public (float X, float Y, float Z) Size { get; init; } = (1f, 1f, 1f);
    }

    [Fact]
    public void ATile_PlacesEveryModelItNames()
    {
        var tile = WorldItemReader.ReadTile(Tile(
            tileX: 3,
            tileZ: -2,
            version: 4,
            new Record("model/scene/build/town/inn.bdae", (1f, 2f, 3f)),
            new Record("model/scene/build/town/wall.bdae", (10f, 0f, 20f))));

        Assert.NotNull(tile);
        Assert.Equal(3, tile.TileX);
        Assert.Equal(-2, tile.TileZ);
        Assert.Equal(
            ["model/scene/build/town/inn.bdae", "model/scene/build/town/wall.bdae"],
            tile.Placements.Select(placement => placement.Model));
        Assert.Equal(1f, tile.Placements[0].Position.X, precision: 4);
        Assert.Equal(2f, tile.Placements[0].Position.Y, precision: 4);
        Assert.Equal(3f, tile.Placements[0].Position.Z, precision: 4);
    }

    [Fact]
    public void TwoPlacementsOfOneModel_BothNameIt()
    {
        var tile = WorldItemReader.ReadTile(Tile(
            tileX: 0,
            tileZ: 0,
            version: 4,
            new Record("model/scene/nature/tree.bdae", (1f, 0f, 1f)),
            new Record("model/scene/nature/tree.bdae", (5f, 0f, 9f))));

        Assert.NotNull(tile);
        Assert.Equal(2, tile.Placements.Count);
        Assert.All(tile.Placements, placement => Assert.Equal("model/scene/nature/tree.bdae", placement.Model));
    }

    [Fact]
    public void TheOlderShapeOfRecord_ReadsTheSameWay()
    {
        var tile = WorldItemReader.ReadTile(Tile(
            tileX: 1, tileZ: 1, version: 3, new Record("model/scene/build/town/inn.bdae", (4f, 5f, 6f))));

        Assert.NotNull(tile);
        var placement = Assert.Single(tile.Placements);
        Assert.Equal(5f, placement.Position.Y, precision: 4);
    }

    [Fact]
    public void ATileWhoseVersionDoesNotAccountForItsLength_IsReadAtTheShapeThatDoes()
    {
        var bytes = Tile(tileX: 0, tileZ: 0, version: 4, new Record("model/scene/build/town/inn.bdae", (7f, 8f, 9f)));
        bytes[4] = 3;

        var tile = WorldItemReader.ReadTile(bytes);

        Assert.NotNull(tile);
        Assert.Equal(9f, Assert.Single(tile.Placements).Position.Z, precision: 4);
    }

    [Fact]
    public void SomethingThatIsNotASceneryTile_IsNotOne()
    {
        Assert.Null(WorldItemReader.ReadTile("not a tile at all"u8));
        Assert.Null(WorldItemReader.ReadTile([]));
    }

    [Fact]
    public void ATileTruncatedPastItsRecords_IsRefusedRatherThanGuessedAt()
    {
        var bytes = Tile(tileX: 0, tileZ: 0, version: 4, new Record("model/scene/build/town/inn.bdae", (1f, 1f, 1f)));

        Assert.Null(WorldItemReader.ReadTile(bytes.AsSpan(0, bytes.Length - 8)));
    }

    [Fact]
    public void ATileThatPlacesNothing_IsAnEmptyTileRatherThanAFailure()
    {
        var tile = WorldItemReader.ReadTile(Tile(tileX: 4, tileZ: 5, version: 4));

        Assert.NotNull(tile);
        Assert.Empty(tile.Placements);
        Assert.Equal(4, tile.TileX);
    }

    [Fact]
    public void TheTileAnEntryIsStoredUnder_IsReadOffItsName()
    {
        Assert.Equal((17, 12), WorldItemReader.TileOfEntry("world/town/items/0017_0012.itm"));
        Assert.Equal((-25, -16), WorldItemReader.TileOfEntry("world/town/items/-025_-016.itm"));
        Assert.Null(WorldItemReader.TileOfEntry("world/town/terrain/0017_0012.trn"));
    }

    [Fact]
    public void EveryTileInAnArchive_IsReadAndTheOnesThatWillNotAreCounted()
    {
        var archive = PackedArchiveFixture.Build(
            ("world/town/items/0000_0000.itm",
                Tile(0, 0, 4, new Record("model/scene/build/town/inn.bdae", (1f, 0f, 1f)))),
            ("world/town/items/0001_0000.itm", "nonsense"u8.ToArray()),
            ("world/town/terrain/0000_0000.trn", [1, 2, 3, 4]));

        using var packed = PackedArchive.Open(new MemoryStream(archive));
        var tiles = WorldItemReader.ReadTiles(packed, out int unreadable);

        Assert.Equal(1, unreadable);
        Assert.Single(tiles);
        Assert.Single(tiles[0].Placements);
    }

    internal static byte[] Tile(int tileX, int tileZ, uint version, params Record[] records)
    {
        int stride = version == 3 ? 56 : 64;
        var names = records.Select(record => record.Model).Distinct(StringComparer.Ordinal).ToList();

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write("AITM"u8);
        writer.Write(version);
        writer.Write(tileX);
        writer.Write(tileZ);
        writer.Write(records.Length);
        writer.Write(names.Count);

        foreach (var record in records)
        {
            var slot = new byte[stride];
            writer.Write(slot, 0, 12);
            writer.Write((uint)names.IndexOf(record.Model));
            writer.Write(record.At.X);
            writer.Write(record.At.Y);
            writer.Write(record.At.Z);
            writer.Write(record.Turn.X);
            writer.Write(record.Turn.Y);
            writer.Write(record.Turn.Z);
            writer.Write(record.Turn.W);
            writer.Write(record.Size.X);
            writer.Write(record.Size.Y);
            writer.Write(record.Size.Z);
            writer.Write(slot, 0, stride - 56);
        }

        writer.Write(names.Count);
        var blob = new List<byte>();
        foreach (var name in names)
        {
            blob.AddRange(Encoding.ASCII.GetBytes(name));
            blob.Add(0);
            writer.Write((uint)blob.Count);
        }

        writer.Write(blob.ToArray());
        writer.Flush();
        return stream.ToArray();
    }
}
