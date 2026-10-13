using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;
using Position = Ubiquitous.Common.Position;

namespace Ubiquitous.Modules.Playerbots;

// Going somewhere else: after a leader who walked out of sight or through a map
// gate, and by bus when there is nothing left to do here.
public sealed partial class BotBrain
{
    private const float GateNear = 8f;
    private const float BoardReach = 6f;
    private static readonly TimeSpan BoredAfter = TimeSpan.FromMinutes(3);
    private DateTimeOffset _lastProgressAt;
    private Trip? _journey;

    private BotDecision? FollowOutOfSight(BotView view, string leader)
    {
        if (!view.LastSeenPlayers.TryGetValue(leader, out var seen) || seen.MapId != view.MapId) return null;
        var gate = knowledge.MapGates
            .Where(gate => gate.SourceMapId == view.MapId && DistanceToGate(gate, seen.Position) <= GateNear)
            .OrderBy(gate => DistanceToGate(gate, seen.Position))
            .FirstOrDefault();
        if (gate is not null)
            return new WalkTo(new Position((gate.SourceMinX + gate.SourceMaxX) / 2, seen.Position.Y, (gate.SourceMinZ + gate.SourceMaxZ) / 2), 0f);
        return Flat(view.Position, seen.Position) > FollowSlack / 2 ? new WalkTo(seen.Position, FollowSlack / 2) : null;
    }

    private static float DistanceToGate(MapGateDefinition gate, Position at)
    {
        var dx = Math.Max(0, Math.Max(gate.SourceMinX - at.X, at.X - gate.SourceMaxX));
        var dz = Math.Max(0, Math.Max(gate.SourceMinZ - at.Z, at.Z - gate.SourceMaxZ));
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    // Somewhere to go, and how: a bus fare from a stop, or a choice in an NPC's conversation.
    private sealed record Trip(string Key, Position Board, ushort ToMap, Position ToAt, TravelQuote? Fare, NpcTrip? Npc);

    // How far round a place its creatures are counted to judge its level. Own choice: about a hunting ground.
    internal const float AreaRadius = 150f;

    // Own choice: a bot this far above where it stands goes somewhere nearer its level at once.
    private const int OutgrownBy = 5;

    // Own choice: trips this close to the best fit are all worth taking, so bots spread over them.
    internal const int FitSlack = 2;

    private static readonly TimeSpan RethinkOutgrown = TimeSpan.FromMinutes(5);
    private DateTimeOffset _nextOutgrownCheck;

    // A bot far above everything around it leaves without waiting to be bored there.
    private bool Outgrown(BotView view, DateTimeOffset now)
    {
        if (view.InParty || now < _nextOutgrownCheck) return false;
        _nextOutgrownCheck = now + RethinkOutgrown;
        return knowledge.LevelAround(view.MapId, view.Position, AreaRadius) is { } here && view.Level - here >= OutgrownBy;
    }

    private BotDecision? Travel(BotView view, DateTimeOffset now, bool urgent = false)
    {
        if (view.InParty || (!urgent && _journey is null && now - _lastProgressAt < BoredAfter)) return null;
        if (_journey is null)
        {
            _journey = PlanJourney(view, now, urgent);
            if (_journey is null)
            {
                _lastProgressAt = now;
                return null;
            }
        }

        var trip = _journey;
        if (Shunned(trip.Key, now))
        {
            _journey = null;
            return null;
        }

        var reach = trip.Npc is null ? BoardReach : InteractReach;
        if (Flat(view.Position, trip.Board) > reach)
            return new WalkTo(trip.Board, reach * 0.5f, trip.Key, trip.Npc is null ? null : InteractReach);

        _journey = null;
        _lastProgressAt = now;
        Shun(trip.Key, now, SetAsideFor);
        if (trip.Fare is { } fare) return new SendMessage(WebMessageTypes.BusConfirm, new { from = fare.From, to = fare.To });

        var npc = trip.Npc!;
        var talker = view.Units.Values
            .Where(unit => unit.Kind == UnitKind.Creature && unit.Entry == npc.NpcEntry)
            .OrderBy(unit => Flat(view.Position, unit.Position))
            .FirstOrDefault();
        return talker is null
            ? null
            : new SendMessage(WebMessageTypes.NpcPickOption, new { networkId = talker.Id, menuId = npc.MenuId, choice = npc.Choice });
    }

    // Every trip the bot can afford from here, the ones landing nearest its level first, picked among
    // those about as good so bots of a level spread out; where nothing says a level, any trip will do.
    private Trip? PlanJourney(BotView view, DateTimeOffset now, bool urgent)
    {
        var trips = new List<Trip>();
        var stop = knowledge.BusStopsOn(view.MapId).OrderBy(stop => Flat(view.Position, At(stop))).FirstOrDefault();
        if (stop is not null)
        {
            trips.AddRange(knowledge.FaresFrom(stop).Where(fare => fare.Cost <= view.Gold).Select(fare =>
                new Trip($"stop:{stop.Id}:{fare.To}", At(stop), fare.Destination.MapId, At(fare.Destination), fare, null)));
        }

        trips.AddRange(knowledge.NpcTripsOn(view.MapId).Where(trip => trip.Cost <= view.Gold).Select(trip =>
            new Trip($"npc:{trip.NpcEntry}:{trip.MenuId}:{trip.Choice}", trip.At, trip.ToMap, trip.ToAt, null, trip)));
        trips.RemoveAll(trip => Shunned(trip.Key, now) || !knowledge.StandsOnMainGround(trip.ToMap, trip.ToAt));
        if (trips.Count == 0) return null;

        var here = knowledge.LevelAround(view.MapId, view.Position, AreaRadius);
        var fits = trips.Select(trip => (Trip: trip, Misfit: Misfit(view.Level, knowledge.LevelAround(trip.ToMap, trip.ToAt, AreaRadius))))
            .ToList();
        var best = fits.Min(fit => fit.Misfit);
        var good = fits.Where(fit => fit.Misfit <= best + FitSlack).Select(fit => fit.Trip).ToList();
        // Leaving a place it has outgrown, a trip no nearer the bot's level than staying is not worth the fare.
        if (urgent && here is { } stay && best >= Misfit(view.Level, stay)) return null;
        return good[random.Next(good.Count)];
    }

    // How far a place's level is from the bot's; a place with no creatures counts as a middling fit.
    private static int Misfit(int level, int? there) => there is { } at ? Math.Abs(at - level) : OutgrownBy;

    private static Position At(TeleportStopDefinition stop) => new(stop.X, stop.Y, stop.Z);
}
