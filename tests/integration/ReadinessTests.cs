using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class ReadinessTests
{
    [Fact]
    public void Readiness_DistinguishesDataUnavailableFromProcessDown()
    {
        var readiness = new ServerReadinessState();

        Assert.True(readiness.Current.ProcessUp);
        Assert.False(readiness.Current.DataReady);

        readiness.SetDataReady();

        Assert.True(readiness.Current.ProcessUp);
        Assert.True(readiness.Current.DataReady);
    }
}
