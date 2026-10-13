using Ubiquitous.Common;

namespace Ubiquitous.Game.Tests;

public sealed class TradeServiceTests
{
    private static readonly Position Origin = new(0, 0, 0);
    private static readonly Position Nearby = new(5, 0, 0);
    private static readonly Position FarAway = new(500, 0, 0);

    [Fact]
    public void Request_RefusesTradingYourself()
    {
        var trades = new TradeService();

        Assert.Equal(TradeService.RequestOutcome.SelfTrade, trades.Request("player:a", Origin, "player:a"));
    }

    [Fact]
    public void Request_RefusesWhenEitherSideIsAlreadyTrading()
    {
        var trades = new TradeService();
        trades.Request("player:a", Origin, "player:b");
        trades.Accept("player:b", Origin);

        Assert.Equal(TradeService.RequestOutcome.AlreadyTrading, trades.Request("player:a", Origin, "player:c"));
        Assert.Equal(TradeService.RequestOutcome.AlreadyTrading, trades.Request("player:c", Origin, "player:b"));
    }

    [Fact]
    public void Accept_WithNoPendingRequestIsRefused()
    {
        var trades = new TradeService();

        Assert.Equal(TradeService.AcceptOutcome.NoPendingRequest, trades.Accept("player:b", Origin).Outcome);
    }

    [Fact]
    public void Accept_TooFarAwayIsRefusedAndNamesTheRequester()
    {
        var trades = new TradeService();
        trades.Request("player:a", Origin, "player:b");

        var result = trades.Accept("player:b", FarAway);

        Assert.Equal(TradeService.AcceptOutcome.OutOfRange, result.Outcome);
        Assert.Equal("player:a", result.PartnerId);
    }

    [Fact]
    public void Accept_WithinRangeStartsTheTradeWithBothOffersEmpty()
    {
        var trades = new TradeService();
        trades.Request("player:a", Origin, "player:b");

        var result = trades.Accept("player:b", Nearby);

        Assert.Equal(TradeService.AcceptOutcome.Ok, result.Outcome);
        Assert.Equal("player:a", result.PartnerId);
        Assert.Equal("player:b", trades.PartnerOf("player:a"));
        Assert.Equal("player:a", trades.PartnerOf("player:b"));
        Assert.Equal(TradeService.TradeOffer.Empty, trades.OfferOf("player:a"));
        Assert.Equal(TradeService.TradeOffer.Empty, trades.OfferOf("player:b"));
    }

    [Fact]
    public void Decline_ReturnsTheRequesterAndLeavesNothingPending()
    {
        var trades = new TradeService();
        trades.Request("player:a", Origin, "player:b");

        Assert.Equal("player:a", trades.Decline("player:b"));
        Assert.Equal(TradeService.AcceptOutcome.NoPendingRequest, trades.Accept("player:b", Origin).Outcome);
    }

    [Fact]
    public void SetOffer_ReplacesItemsAndGoldWhenNeitherSideHasConfirmed()
    {
        var trades = new TradeService();
        trades.Request("player:a", Origin, "player:b");
        trades.Accept("player:b", Origin);

        var result = trades.SetOffer("player:a", ["item-1", "item-2"], 50);

        Assert.Equal(TradeService.OfferOutcome.Ok, result.Outcome);
        Assert.Equal("player:b", result.PartnerId);
        var offer = trades.OfferOf("player:a")!;
        Assert.Equal(["item-1", "item-2"], offer.ItemIds);
        Assert.Equal(50, offer.Gold);
        Assert.False(offer.Confirmed);
    }

    [Fact]
    public void SetOffer_ClampsNegativeGoldToZero()
    {
        var trades = new TradeService();
        trades.Request("player:a", Origin, "player:b");
        trades.Accept("player:b", Origin);

        trades.SetOffer("player:a", [], -30);

        Assert.Equal(0, trades.OfferOf("player:a")!.Gold);
    }

    [Fact]
    public void SetOffer_WhenNotTradingIsRefused()
    {
        var trades = new TradeService();

        Assert.Equal(TradeService.OfferOutcome.NotTrading, trades.SetOffer("player:a", [], 0).Outcome);
    }

    [Fact]
    public void Confirm_BothSidesConfirmingReportsBothConfirmed()
    {
        var trades = new TradeService();
        trades.Request("player:a", Origin, "player:b");
        trades.Accept("player:b", Origin);

        var first = trades.Confirm("player:a", true);
        Assert.Equal(TradeService.ConfirmOutcome.Ok, first.Outcome);

        var second = trades.Confirm("player:b", true);
        Assert.Equal(TradeService.ConfirmOutcome.BothConfirmed, second.Outcome);
        Assert.Equal("player:a", second.PartnerId);
    }

    [Fact]
    public void Confirm_UnconfirmingDoesNotReportBothConfirmed()
    {
        var trades = new TradeService();
        trades.Request("player:a", Origin, "player:b");
        trades.Accept("player:b", Origin);
        trades.Confirm("player:a", true);
        trades.Confirm("player:b", true);

        var result = trades.Confirm("player:a", false);

        Assert.Equal(TradeService.ConfirmOutcome.Ok, result.Outcome);
        Assert.False(trades.OfferOf("player:a")!.Confirmed);
    }

    [Fact]
    public void SetOffer_AfterEitherSideConfirmedCancelsTheWholeTradeRatherThanJustResettingAccepts()
    {
        var trades = new TradeService();
        trades.Request("player:a", Origin, "player:b");
        trades.Accept("player:b", Origin);
        trades.Confirm("player:b", true);

        var result = trades.SetOffer("player:a", ["item-1"], 10);

        Assert.Equal(TradeService.OfferOutcome.CancelledByChange, result.Outcome);
        Assert.Equal("player:b", result.PartnerId);
        Assert.Null(trades.PartnerOf("player:a"));
        Assert.Null(trades.PartnerOf("player:b"));
        Assert.Null(trades.OfferOf("player:a"));
        Assert.Null(trades.OfferOf("player:b"));
    }

    [Fact]
    public void SetOffer_ByTheAlreadyConfirmedSideItselfAlsoCancels()
    {
        var trades = new TradeService();
        trades.Request("player:a", Origin, "player:b");
        trades.Accept("player:b", Origin);
        trades.Confirm("player:a", true);

        var result = trades.SetOffer("player:a", ["item-1"], 0);

        Assert.Equal(TradeService.OfferOutcome.CancelledByChange, result.Outcome);
        Assert.Null(trades.PartnerOf("player:a"));
    }

    [Fact]
    public void Cancel_TearsDownBothSidesAndReturnsThePartner()
    {
        var trades = new TradeService();
        trades.Request("player:a", Origin, "player:b");
        trades.Accept("player:b", Origin);
        trades.SetOffer("player:a", ["item-1"], 10);

        var partnerId = trades.Cancel("player:a");

        Assert.Equal("player:b", partnerId);
        Assert.Null(trades.PartnerOf("player:a"));
        Assert.Null(trades.PartnerOf("player:b"));
        Assert.Null(trades.OfferOf("player:a"));
        Assert.Null(trades.OfferOf("player:b"));
    }

    [Fact]
    public void Cancel_WhenNotTradingReturnsNull()
    {
        var trades = new TradeService();

        Assert.Null(trades.Cancel("player:a"));
    }
}
