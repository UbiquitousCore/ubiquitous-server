using System.Text;
using Ubiquitous.AssetExtractor;
using Ubiquitous.Data;

namespace Ubiquitous.Integration.Tests;

public sealed class PlayerLookReaderTests
{
    [Fact]
    public void ABodyOffersEachImageItShipsAndEachHairMeshItCarries()
    {
        var textures = PlayerLookReader.TextureNames(
        [
            "texture/avatar/human_m_body_01.tga", "texture/avatar/human_m_body_02.PVR", "texture/avatar/human_m_body_04.tga",
            "texture/avatar/human_m_body_01_face_01.tga", "texture/avatar/human_m_body_01_face_03.tga",
            "texture/avatar/human_m_body_02_face_02.tga", "texture/avatar/human_m_hair_a_02.png",
            "texture/avatar/human_f_body_01.tga", "model/npc/human_m_body_03.bdae",
        ]);
        var model = Encoding.Latin1.GetBytes("\0hair_00-mesh\0HAIR_02-mesh\0hair_a_01.tga\0chair_09\0hair_02-node\0");

        var look = PlayerLookReader.Read(PlayerRaceIds.Human, PlayerBodies.Male, "model/npc/character/human/male/human_m_h.bdae", textures, model);

        Assert.Equal([1, 2, 4], look.Skins);
        Assert.Equal([1, 3], look.Faces);
        Assert.Equal([2], look.HairColours);
        Assert.Equal([0, 2], look.HairStyles);
    }

    [Fact]
    public void APackageListsItsBodiesLooksAndReadsThemBack()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ubiquitous-looks-" + Guid.NewGuid().ToString("N"));
        try
        {
            DataPackageWriter.WriteSyntheticPackage(directory);
            PlayerLookDefinition[] looks = [new(PlayerRaceIds.Orc, PlayerBodies.Female, [1, 2], [1], [0, 3], [4])];

            DataPackageWriter.WriteDataset(directory, PlayerLookDefinition.Dataset, looks);

            var read = Assert.Single(DataCatalog.LoadFromDirectory(directory).LoadPlayerLooks());
            Assert.Equal((PlayerRaceIds.Orc, PlayerBodies.Female), (read.Race, read.Gender));
            Assert.Equal([0, 3], read.HairStyles);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
