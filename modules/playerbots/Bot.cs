using Microsoft.Extensions.Logging;
using Ubiquitous.Common;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;
using Position = Ubiquitous.Common.Position;
using WirePosition = Ubiquitous.Protocol.Web.Position;

namespace Ubiquitous.Modules.Playerbots;

// One bot: a client connection, what it has seen, and the brain driving it.
// It signs in, makes a character if it has none, and plays through the same
// messages ubiquitous-client sends.
public sealed class Bot
{
    public const float WalkSpeed = 5.5f;
    // Half the running pace in water, as the client swims.
    public const float SwimSpeed = WalkSpeed / 2f;
    private const int MostNameTries = 8;
    private const float RerouteWhenGoalMoves = 2f;
    private const float WaypointReached = 0.5f;
    // Patches of walkable ground smaller than this are pockets (between water and a
    // fence, on a rooftop): bots never walk to one and leave one they find themselves in.
    public const float MainGroundArea = 2000f;
    private const float StepBackReach = 6f;
    private const float ShortOfGoal = 1.5f;
    private const float DetourWorthFrom = 25f;
    private const float NewGoal = 10f;
    private const int ApproachDirections = 8;
    private const double DetourChance = 0.4;
    private const float MostDetourCost = 1.5f;
    private const float Headway = 1f;
    private static readonly TimeSpan MostTimeWithoutHeadway = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ForgetHeadway = TimeSpan.FromMinutes(2);
    private const float VerticalReach = 2.5f;
    // The server's own stuck rescue allows one every fifteen minutes.
    private static readonly TimeSpan RescueEvery = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan FirstStep = TimeSpan.FromMilliseconds(400);

    private enum Stage { SignIn, AwaitSignIn, AwaitList, AwaitDelete, AwaitCreate, AwaitWorld, InWorld, Failed }

    // Every body's skins, faces and hair are numbered from 1; a character made
    // with 0 has no texture and shows black, so the bot makes a new one.
    private const int FirstLookOption = 1;

    private readonly IModuleHost _host;
    private readonly IClientConnection _connection;
    private readonly ModuleLogin _login;
    private readonly Random _random;
    private readonly IReadOnlySet<int> _races;
    private readonly int _startLevel;
    private readonly (ushort MapId, Position At)? _startPlace;
    private string? _madeName;
    private Stage _stage = Stage.SignIn;
    private long _sequence;
    private int _nameTries;
    private int _correctionsSeen;
    private bool _moving;
    private DateTimeOffset _lastStepAt;
    private double _yaw;
    private List<Position> _route = [];
    private Position _routeGoal;
    // How close each goal has come and when it last got closer, kept whatever else the
    // bot does in between, so a goal it never gets nearer is given up.
    private readonly Dictionary<string, (float Closest, DateTimeOffset Since)> _headway = new(StringComparer.Ordinal);
    private DateTimeOffset _nextRescue;
    private Position? _via;
    private (ushort MapId, Position Position)? _lastMainGround;
    private WalkTo? _walk;

    // What a player says or does without stopping: the bot keeps walking while it sends these.
    private static readonly HashSet<string> SaidOnTheMove =
    [
        WebMessageTypes.QuestRefreshStatuses, WebMessageTypes.PlayerChat, WebMessageTypes.ChatWhisper,
        WebMessageTypes.PartyAccept, WebMessageTypes.PartyDecline, WebMessageTypes.PartyInvite, WebMessageTypes.PartyLeave,
        WebMessageTypes.DuelDecline, WebMessageTypes.TradeDecline, WebMessageTypes.TalentLearn,
        WebMessageTypes.InventoryEquip, WebMessageTypes.InventorySort, WebMessageTypes.PlayerEmote,
    ];

    public Bot(IModuleHost host, IClientConnection connection, ModuleLogin login, BotBrain brain, Random random,
        IReadOnlySet<int> races, int startLevel = CharacterCreation.StartingLevel,
        (ushort MapId, Position At)? startPlace = null)
    {
        _startLevel = startLevel;
        _startPlace = startPlace;
        _races = races;
        _host = host;
        _connection = connection;
        _login = login;
        _random = random;
        Brain = brain;
    }

    public BotView View { get; } = new();

    public BotBrain Brain { get; }

    public string? Name { get; private set; }

    public bool Failed => _stage == Stage.Failed;

    public bool Playing => _stage == Stage.InWorld;

    public async Task TickAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!_connection.IsOpen) return;
        var before = (View.Level, View.MapId, Quests: View.Quests.Keys.ToHashSet(), View.DuelOpponent, Worn: Worn());
        foreach (var message in _connection.TakeReceived()) View.Apply(message, now);
        if (_stage == Stage.InWorld) Narrate(before.Level, before.MapId, before.Quests, before.DuelOpponent, before.Worn);
        if (_stage != Stage.InWorld)
        {
            await SignInAsync(cancellationToken);
            return;
        }

        if (View.MoveCorrections != _correctionsSeen)
        {
            _correctionsSeen = View.MoveCorrections;
            _moving = false;
            _route.Clear();
            Brain.Unreachable(_walk?.Purpose, now);
        }

        switch (Brain.Decide(View, now))
        {
            case WalkTo walk:
                _walk = walk;
                await StepAsync(walk, now, cancellationToken);
                break;
            case SendMessage send when _moving && _walk is { } underway && SaidOnTheMove.Contains(send.Type):
                await SendAsync(send.Type, send.Payload, cancellationToken);
                await StepAsync(underway, now, cancellationToken);
                break;
            case SendMessage send:
                _walk = null;
                await StopAsync(cancellationToken);
                await SendAsync(send.Type, send.Payload, cancellationToken);
                break;
            default:
                _walk = null;
                await StopAsync(cancellationToken);
                break;
        }
    }

    private int Worn() => View.Items.Values.Count(item => item.Area == InventoryAreas.Equipment);

    private void Narrate(int level, ushort mapId, HashSet<uint> quests, string? duel, int worn)
    {
        if (!_host.Logger.IsEnabled(LogLevel.Debug)) return;
        foreach (var taken in View.Quests.Keys.Except(quests)) _host.Logger.LogDebug("Bot {Name} took quest {Quest}", Name, taken);
        foreach (var done in quests.Except(View.Quests.Keys)) _host.Logger.LogDebug("Bot {Name} finished quest {Quest}", Name, done);
        if (View.Level != level) _host.Logger.LogDebug("Bot {Name} reached level {Level}", Name, View.Level);
        if (View.MapId != mapId) _host.Logger.LogDebug("Bot {Name} went to map {Map}", Name, View.MapId);
        if (View.DuelOpponent is { } rival && rival != duel) _host.Logger.LogDebug("Bot {Name} is duelling {Rival}", Name, rival);
        if (Worn() > worn) _host.Logger.LogDebug("Bot {Name} put on gear", Name);
    }

    private async Task SignInAsync(CancellationToken cancellationToken)
    {
        switch (_stage)
        {
            case Stage.SignIn:
                _stage = Stage.AwaitSignIn;
                await SendAsync(WebMessageTypes.AccountLogin, new LoginRequest(_login.UserId, _login.Secret), cancellationToken);
                break;
            case Stage.AwaitSignIn when View.LoggedIn is { } ok:
                if (!ok) Fail("sign-in was refused");
                else await ListAsync(cancellationToken);
                break;
            case Stage.AwaitList when View.Characters is { } characters:
                if (characters is [{ Skin: < FirstLookOption } unseen, ..])
                {
                    _stage = Stage.AwaitDelete;
                    View.ForgetCharacters();
                    await SendAsync(WebMessageTypes.CharactersDelete, new DeleteCharacterRequest(unseen.Name), cancellationToken);
                }
                else if (characters.Count > 0)
                {
                    Name = characters[0].Name;
                    View.Class = characters[0].Class;
                    _stage = Stage.AwaitWorld;
                    await SendAsync(WebMessageTypes.CharactersChoose, new ChooseCharacterRequest(Name), cancellationToken);
                }
                else
                {
                    await CreateAsync(cancellationToken);
                }
                break;
            case Stage.AwaitDelete when View.LastDelete is { } deleted:
                if (deleted.Ok) await CreateAsync(cancellationToken);
                else Fail($"could not remake a character with no look: {deleted.Reason}");
                break;
            case Stage.AwaitCreate when View.LastCreate is { } created:
                if (created.Ok)
                {
                    // A new random character starts at the level it was given, where a player that level would be.
                    if (_startLevel > CharacterCreation.StartingLevel && _madeName is { } made
                        && !await _host.StartAtLevelAsync(_login, made, _startLevel, _startPlace, cancellationToken))
                        _host.Logger.LogWarning("Bot {Name} could not start at level {Level}", made, _startLevel);
                    await ListAsync(cancellationToken);
                }
                else if (++_nameTries < MostNameTries && created.Code is (int)CharacterCreateCode.NameTaken or (int)CharacterCreateCode.NameNotAllowed)
                    await CreateAsync(cancellationToken);
                else Fail($"character creation was refused: {created.Reason}");
                break;
            case Stage.AwaitWorld when View.InWorld:
                _stage = Stage.InWorld;
                _host.Logger.LogInformation("Bot {Name} entered the world", Name);
                break;
        }
    }

    private async Task ListAsync(CancellationToken cancellationToken)
    {
        View.ForgetCharacters();
        _stage = Stage.AwaitList;
        await SendAsync(WebMessageTypes.CharactersList, new { }, cancellationToken);
    }

    private async Task CreateAsync(CancellationToken cancellationToken)
    {
        var offered = View.OfferedRaces.Count > 0 ? View.OfferedRaces.ToHashSet() : null;
        var open = StartingLoadouts.All.Where(loadout => offered is null || offered.Contains(loadout.Race)).ToArray();
        var preferred = open.Where(loadout => _races.Contains(loadout.Race)).ToArray();
        var choices = preferred.Length > 0 ? preferred : open;
        if (choices.Length == 0)
        {
            Fail("no race and class is open on this server");
            return;
        }

        var pick = choices[_random.Next(choices.Length)];
        var gender = _random.Next(2);
        View.ForgetCharacters();
        _stage = Stage.AwaitCreate;
        _madeName = BotNames.Make(_random);
        await SendAsync(WebMessageTypes.CharactersCreate, new CreateCharacterRequest(
            _madeName, pick.Race, pick.PlayerClass, gender,
            LookFor(_host.Content.PlayerLooks, pick.Race, gender, _random)), cancellationToken);
    }

    // Any look the body ships, each option picked on its own; option 1 where the package says none.
    internal static Appearance LookFor(IReadOnlyList<PlayerLookDefinition> looks, int race, int gender, Random random)
    {
        var body = looks.FirstOrDefault(look => look.Race == race && look.Gender == gender);
        int Any(IReadOnlyList<int>? options) =>
            options is { Count: > 0 } ? options[random.Next(options.Count)] : FirstLookOption;
        return new Appearance(Any(body?.Skins), Any(body?.HairStyles), Any(body?.HairColours), Any(body?.Faces));
    }

    private void Fail(string reason)
    {
        _stage = Stage.Failed;
        _host.Logger.LogWarning("Bot {UserId} stopped: {Reason}", _login.UserId, reason);
    }

    private async Task StepAsync(WalkTo walk, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var from = View.Position;
        // A goal far above or below (a corpse under a bridge) is not reached by standing over it.
        var climbing = Math.Abs(walk.Target.Y - from.Y) > VerticalReach;
        var left = BotBrain.Flat(from, walk.Target) - walk.Within;
        if (left <= 0.05f && !climbing)
        {
            await StopAsync(cancellationToken);
            return;
        }

        if (!Progressing(walk, Math.Max(left, 0f) + (climbing ? Math.Abs(walk.Target.Y - from.Y) : 0f), now))
        {
            await GiveUpAsync(walk, "made no headway", now, cancellationToken);
            return;
        }

        Position waypoint;
        if (_host.HasWalkableGround(View.MapId))
        {
            if (!await OnMainGroundAsync(walk, now, cancellationToken)) return;
            from = View.Position;

            if (NextWaypoint(walk, now) is not { } next)
            {
                await GiveUpAsync(walk, "has no walkable route", now, cancellationToken);
                return;
            }

            waypoint = next;
        }
        else
        {
            waypoint = walk.Target;
        }

        var toGoal = _route.Count <= 1;
        var distance = BotBrain.Flat(from, waypoint);
        var elapsed = _moving ? Math.Clamp((now - _lastStepAt).TotalSeconds, 0, 1) : FirstStep.TotalSeconds;
        var pace = SwimLine(from) is null ? WalkSpeed : SwimSpeed;
        var step = Math.Min(pace * (float)elapsed, toGoal && !climbing ? Math.Min(distance, left) : distance);
        if (distance < 0.01f || step <= 0.05f)
        {
            await StopAsync(cancellationToken);
            return;
        }

        var dx = (waypoint.X - from.X) / distance;
        var dz = (waypoint.Z - from.Z) / distance;
        var x = from.X + dx * step;
        var z = from.Z + dz * step;
        var y = _host.GroundHeight(View.MapId, x, z, from.Y) ?? from.Y + (waypoint.Y - from.Y) * (step / distance);
        View.Position = new Position(x, y, z);
        _moving = true;
        _lastStepAt = now;
        _yaw = Math.Atan2(dx, -dz);
        var swimLine = SwimLine(View.Position);
        // The velocity lets a watching client carry the body on between steps and know
        // which way it walks; without one it guesses from positions 400 ms apart.
        await SendAsync(WebMessageTypes.PlayerMove, new PlayerMovePayload(
            x, swimLine ?? y, z, _yaw, true, swimLine is null ? MovementState.Walking : MovementState.Swimming,
            Velocity: new WirePosition(dx * pace, 0, dz * pace), Speed: pace), cancellationToken);
    }

    // On walkable ground a bot only ever walks the map's own route. A goal with
    // no route, or whose route stops short of it, is out of reach.
    private Position? NextWaypoint(WalkTo walk, DateTimeOffset now)
    {
        var goal = walk.Target;
        if (BotBrain.Flat(goal, _routeGoal) > RerouteWhenGoalMoves || _route.Count == 0)
        {
            if (BotBrain.Flat(goal, _routeGoal) > NewGoal) _via = PickDetour(goal);
            if (_via is { } passing && BotBrain.Flat(View.Position, passing) < WaypointReached * 4) _via = null;
            _routeGoal = goal;
            var reach = walk.Reach ?? walk.Within + ShortOfGoal;
            _route = RouteWithin(goal, reach);
            if (_route.Count == 0 && BotBrain.Flat(View.Position, goal) > reach) return null;
            if (_route.Count == 0) _route.Add(View.Position);
        }

        while (_route.Count > 1 && BotBrain.Flat(View.Position, _route[0]) < WaypointReached) _route.RemoveAt(0);
        return _route[0];
    }

    // The route to the goal, or else to the nearest spot on the main ground within
    // reach of it: an NPC behind a counter is talked to from in front of it.
    private List<Position> RouteWithin(Position goal, float reach)
    {
        var floor = new Position(goal.X, _host.GroundHeight(View.MapId, goal.X, goal.Z, goal.Y) ?? goal.Y, goal.Z);
        List<Position>? direct = null;
        if (_host.WalkableAreaUnder(View.MapId, floor) >= MainGroundArea && RouteTo(floor) is { Count: > 0 } walked
            && BotBrain.Flat(walked[^1], goal) <= reach)
        {
            if (BotBrain.Flat(walked[^1], goal) <= ArrivedAt) return walked;
            direct = walked;
        }

        // A goal it cannot stand on (an NPC behind a counter) is reached from any spot near it,
        // not the one nearest the bot, so bots talking to the same NPC do not stand on one another.
        _via = null;
        foreach (var spot in ApproachSpots(goal, reach).OrderBy(spot => BotBrain.Flat(View.Position, spot) + _random.NextSingle() * reach))
        {
            if (_host.FindRoute(View.MapId, View.Position, spot) is { Count: > 0 } around
                && BotBrain.Flat(around[^1], goal) <= reach)
                return [.. around];
        }

        return direct ?? [];
    }

    // Own choice: a route ending this close to its goal got there.
    private const float ArrivedAt = 2f;

    private IEnumerable<Position> ApproachSpots(Position goal, float reach)
    {
        foreach (var ring in new[] { 0.5f, 0.85f })
        {
            for (var step = 0; step < ApproachDirections; step++)
            {
                var angle = step * MathF.Tau / ApproachDirections;
                var x = goal.X + MathF.Cos(angle) * reach * ring;
                var z = goal.Z + MathF.Sin(angle) * reach * ring;
                var spot = new Position(x, _host.GroundHeight(View.MapId, x, z, goal.Y) ?? goal.Y, z);
                if (_host.WalkableAreaUnder(View.MapId, spot) >= MainGroundArea) yield return spot;
            }
        }
    }

    private List<Position> RouteTo(Position goal)
    {
        if (_via is { } via
            && _host.FindRoute(View.MapId, View.Position, via) is { Count: > 0 } toVia
            && _host.FindRoute(View.MapId, via, goal) is { Count: > 0 } onward)
            return [.. toVia, .. onward];
        _via = null;
        return _host.FindRoute(View.MapId, View.Position, goal)?.ToList() ?? [];
    }

    // Players heading the same way do not all take the one shortest line: a long
    // walk sometimes bends through a point off to one side, if that costs little.
    private Position? PickDetour(Position goal)
    {
        var from = View.Position;
        var straight = BotBrain.Flat(from, goal);
        if (straight < DetourWorthFrom || _random.NextDouble() >= DetourChance) return null;

        var side = (_random.Next(2) * 2 - 1) * (0.15f + (float)_random.NextDouble() * 0.25f) * straight;
        var along = 0.3f + (float)_random.NextDouble() * 0.4f;
        var x = from.X + (goal.X - from.X) * along - (goal.Z - from.Z) / straight * side;
        var z = from.Z + (goal.Z - from.Z) * along + (goal.X - from.X) / straight * side;
        var via = new Position(x, _host.GroundHeight(View.MapId, x, z, from.Y) ?? from.Y, z);
        if (_host.WalkableAreaUnder(View.MapId, via) < MainGroundArea) return null;

        var detour = BotBrain.Flat(from, via) + BotBrain.Flat(via, goal);
        return detour <= straight * MostDetourCost ? via : null;
    }

    // A walk that has not closed on its goal for a while is stuck behind something.
    private bool Progressing(WalkTo walk, float left, DateTimeOffset now)
    {
        // Following a leader is never given up: the leader moves.
        if (walk.Purpose is not { } purpose) return true;
        foreach (var stale in _headway.Where(pair => now - pair.Value.Since > ForgetHeadway).Select(pair => pair.Key).ToList())
            _headway.Remove(stale);
        if (!_headway.TryGetValue(purpose, out var seen) || left < seen.Closest - Headway)
        {
            _headway[purpose] = (left, now);
            return true;
        }

        return now - seen.Since < MostTimeWithoutHeadway;
    }

    private async Task GiveUpAsync(WalkTo walk, string why, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (walk.Purpose != "wander")
            _host.Logger.LogDebug("Bot {Name} gives up walking for {Purpose}: it {Why}", Name, walk.Purpose ?? "its leader", why);
        _route.Clear();
        _walk = null;
        if (walk.Purpose is { } purpose) _headway.Remove(purpose);
        Brain.Unreachable(walk.Purpose, now);
        await StopAsync(cancellationToken);
    }

    // A bot just below a floor (a plaza over the terrain) steps up onto it; one off the
    // ground, or in a pocket cut off from the map, asks to be put back as a stuck player would.
    private async Task<bool> OnMainGroundAsync(WalkTo walk, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var at = View.Position;
        var area = _host.WalkableAreaUnder(View.MapId, at);
        if (area == 0f && _host.GroundHeight(View.MapId, at.X, at.Z, at.Y) is { } floor && floor != at.Y)
        {
            var settled = new Position(at.X, floor, at.Z);
            area = _host.WalkableAreaUnder(View.MapId, settled);
            if (area > 0f)
            {
                View.Position = settled;
                await SendAsync(WebMessageTypes.PlayerMove, new PlayerMovePayload(
                    settled.X, settled.Y, settled.Z, _yaw, false, MovementState.Standing), cancellationToken);
            }
        }

        if (area >= MainGroundArea)
        {
            _lastMainGround = (View.MapId, View.Position);
            return true;
        }

        // Slipped off the edge mid-walk: straight back to where it last stood on the main ground.
        if (_lastMainGround is { } last && last.MapId == View.MapId && BotBrain.Flat(at, last.Position) <= StepBackReach)
        {
            var back = last.Position;
            View.Position = back;
            _route.Clear();
            _walk = null;
            Brain.Unreachable(walk.Purpose, now);
            await SendAsync(WebMessageTypes.PlayerMove, new PlayerMovePayload(
                back.X, back.Y, back.Z, _yaw, false, MovementState.Standing), cancellationToken);
            _moving = false;
            return false;
        }

        await RescueAsync(now, area == 0f ? "is off the walkable ground" : "is in a pocket cut off from the map", cancellationToken);
        return false;
    }

    private async Task RescueAsync(DateTimeOffset now, string why, CancellationToken cancellationToken)
    {
        await StopAsync(cancellationToken);
        if (now < _nextRescue) return;
        _nextRescue = now + RescueEvery;
        _host.Logger.LogDebug("Bot {Name} {Why} and asks to be unstuck", Name, why);
        await SendAsync(WebMessageTypes.PlayerUnstuck, new { }, cancellationToken);
    }

    private async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_moving) return;
        _moving = false;
        var at = View.Position;
        var swimLine = SwimLine(at);
        await SendAsync(WebMessageTypes.PlayerMove, new PlayerMovePayload(
            at.X, swimLine ?? at.Y, at.Z, _yaw, false, swimLine is null ? MovementState.Standing : MovementState.Swimming),
            cancellationToken);
    }

    // Where a body floats once the bed is deep enough to swim, as ubiquitous-client has it. The bot
    // keeps its own position on the bed, which is the ground its routes run over.
    private float? SwimLine(Position bed) =>
        _host.WaterSurface(View.MapId, bed.X, bed.Z) is { } surface && surface - NavigationAreas.SwimDepth > bed.Y
            ? surface - NavigationAreas.SwimDepth
            : null;

    private Task SendAsync(string type, object payload, CancellationToken cancellationToken) =>
        _connection.SendAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(type, _sequence++, payload)), cancellationToken);

    public ValueTask DisconnectAsync() => _connection.DisposeAsync();
}
