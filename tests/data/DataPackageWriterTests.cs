
namespace Ubiquitous.Data.Tests;

public class DataPackageWriterTests
{
    [Fact]
    public void WriteSyntheticPackage_ValidatesClean()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"ubiquitous-package-{Guid.NewGuid():N}");
        try
        {
            DataPackageWriter.WriteSyntheticPackage(dir);

            var result = DataPackageWriter.ValidatePackage(dir);

            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ValidatePackage_MissingManifest_Throws()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"ubiquitous-package-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Throws<FileNotFoundException>(() => DataPackageWriter.ValidatePackage(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
