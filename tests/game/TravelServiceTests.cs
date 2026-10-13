using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class TravelServiceTests
{
    private static readonly Dictionary<uint, TeleportStopDefinition> Stops = new()
    {
        [1] = new(1, 13, 2f, 2f, 3f),
        [2] = new(2, 13, 200f, 2f, 3f),
        [3] = new(3, 14, 100f, 0f, 100f, 1.5f),
        [4] = new(4, 13, 20f, 2f, 3f),
    };

    private static readonly TravelRouteDefinition[] Routes =
    [
        new(1, 1, 2, 70),
        new(2, 2, 3, 430),
    ];

    private static TravelService Network() => new(Stops, Routes);

    [Fact]
    public void Quote_ChargesTheLegPriceForOneHop()
    {
        var quote = Network().Quote(1, 2, 13, 2f, 2f, 3f);

        Assert.NotNull(quote);
        Assert.Equal(1u, quote.From);
        Assert.Equal(2u, quote.To);
        Assert.Equal(70, quote.Cost);
        Assert.Equal(2u, quote.Destination.Id);
    }

    [Fact]
    public void Quote_AddsUpTheLegsWhenTheJourneyChangesAtAStop()
    {
        var quote = Network().Quote(1, 3, 13, 2f, 2f, 3f);

        Assert.Equal(500, quote!.Cost);
        Assert.Equal((ushort)14, quote.Destination.MapId);
    }

    [Fact]
    public void Quote_TakesTheCheaperRouteWhenTwoJoinTheSameStops()
    {
        var service = new TravelService(Stops,
        [
            .. Routes,
            new TravelRouteDefinition(3, 1, 3, 120),
        ]);

        Assert.Equal(120, service.Quote(1, 3, 13, 2f, 2f, 3f)!.Cost);
    }

    [Fact]
    public void Quote_PricesTheJourneyTheSameInEitherDirection()
    {
        var outward = Network().Quote(1, 2, 13, 2f, 2f, 3f);
        var back = Network().Quote(2, 1, 13, 200f, 2f, 3f);

        Assert.Equal(outward!.Cost, back!.Cost);
    }

    [Fact]
    public void Quote_SaysNothingAboutAStopNoRouteReaches()
    {
        Assert.Null(Network().Quote(1, 4, 13, 2f, 2f, 3f));
    }

    [Fact]
    public void Quote_SaysNothingWhenThePlayerIsNotStandingAtTheDepartureStop()
    {
        Assert.Null(Network().Quote(1, 2, 13, 500f, 2f, 3f));
    }

    [Fact]
    public void Quote_SellsFromTheEdgeOfInteractionRangeAndNoFurther()
    {
        var service = Network();

        Assert.NotNull(service.Quote(1, 2, 13, 2f + 9.9f, 2f, 3f));
        Assert.Null(service.Quote(1, 2, 13, 2f + 10.1f, 2f, 3f));
    }

    [Fact]
    public void Quote_MeasuresAStopAcrossTheGroundWhateverHeightItRecords()
    {
        var service = Network();

        Assert.NotNull(service.Quote(1, 2, 13, 2f, -900f, 3f));
        Assert.NotNull(service.Quote(1, 2, 13, 2f, 900f, 3f));

        Assert.Null(service.Quote(1, 2, 13, 2f + 10.1f, 900f, 3f));
    }

    [Fact]
    public void Quote_SaysNothingWhenTheDepartureStopIsOnAnotherMap()
    {
        Assert.Null(Network().Quote(3, 1, 13, 100f, 0f, 100f));
    }

    [Fact]
    public void Quote_PricesAJourneyToWhereThePlayerAlreadyStandsAtNothing()
    {
        Assert.Equal(0, Network().Quote(1, 1, 13, 2f, 2f, 3f)!.Cost);
    }

    [Fact]
    public void Quote_SaysNothingAboutAStopThatDoesNotExist()
    {
        Assert.Null(Network().Quote(1, 99, 13, 2f, 2f, 3f));
        Assert.Null(Network().Quote(99, 1, 13, 2f, 2f, 3f));
    }

    [Fact]
    public void Quote_NeverSellsAJourneyToOrFromADungeonGate()
    {
        var stops = new Dictionary<uint, TeleportStopDefinition>
        {
            [1] = new(1, 13, 2f, 2f, 3f),
            [5] = new(5, 13, 40f, 2f, 3f, Kind: TeleportStopKind.DungeonGate),
        };
        var service = new TravelService(stops, [new TravelRouteDefinition(1, 1, 5, 10)]);

        Assert.Null(service.Quote(1, 5, 13, 2f, 2f, 3f));
        Assert.Null(service.Quote(5, 1, 13, 40f, 2f, 3f));
    }

    [Fact]
    public void Quote_IgnoresARouteBetweenTwoDungeonGatesEntirely()
    {
        var stops = new Dictionary<uint, TeleportStopDefinition>
        {
            [5] = new(5, 13, 40f, 2f, 3f, Kind: TeleportStopKind.DungeonGate),
            [6] = new(6, 13, 45f, 2f, 3f, Kind: TeleportStopKind.DungeonGate),
        };
        var service = new TravelService(stops, [new TravelRouteDefinition(1, 5, 6, 10)]);

        Assert.Null(service.Quote(5, 6, 13, 40f, 2f, 3f));
    }
}
