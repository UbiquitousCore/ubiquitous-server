using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Scripts;
using DomainPosition = Ubiquitous.Common.Position;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    // One dev request can redraw the area around the dummy; past this the replies alone are the flood.
    private const int MaxStressBots = 200;

    // Above the fight-summon range, so a stress spawn id can never be a dungeon summon.
    private const uint StressSpawnIdBase = 1_600_000_000;

    // Bots mill around the dummy at wander pace and swing when they are close enough to hit it.
    private const float StressSpawnRadius = 6f;
    private const float StressWanderRadius = 8f;

    // Bots are real player bodies on the wire (race, looks and gear included) so the
    // client builds, animates and labels them exactly like players. Sight-scoped:
    // they are announced only to this session, never through the hub.
    private static readonly int[] StressRaces =
    [
        PlayerRaceIds.Human, PlayerRaceIds.Elf, PlayerRaceIds.Orc, PlayerRaceIds.Undead,
    ];

    private readonly HashSet<string> _stressBots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> _stressBotSpawns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DomainPosition> _stressBotHomes = new(StringComparer.Ordinal);
    private string? _stressDummyId;
    private uint _nextStressSpawn;

    private IReadOnlyList<string> StressTest(JsonElement payload)
    {
        if (_world is null || _character is null)
            return [Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before a stress test.")];
        if (_account?.IsAdmin != true)
            return [Error(WireCodes.ProtocolError.Forbidden, "Only an administrator may run a stress test.")];
        var request = WebMessageJson.DeserializePayload<StressTestPayload>(payload);
        if (request.Count < 1 || request.Count > MaxStressBots)
            return [Error(WireCodes.ProtocolError.InvalidPayload, $"Request between 1 and {MaxStressBots} bots.")];
        if (_shared is null)
            return [Error(WireCodes.ProtocolError.NotInWorld, "There is no shared map here.")];
        if (FindStressDummy() is not { } dummy)
            return [Error(WireCodes.ProtocolError.InvalidTarget, NoDummyReason())];

        var responses = new List<string>();
        ClearStressBots(responses);
        _stressDummyId = dummy.Id;
        if (!_shared.TryGetPosition(dummy.Id, out var dummyAt)) return responses;
        var staminaBonus = _attributeModifiers.GetFlatAmount(PlayerUnitId, AttributeIds.Stamina, Now);
        var wisdomBonus = _attributeModifiers.GetFlatAmount(PlayerUnitId, AttributeIds.Wisdom, Now);
        for (var i = 0; i < request.Count; i++)
        {
            var wireId = $"player:stress-{_character.Guid}-{i}";
            var angle = (Math.PI * 2 * i / request.Count) + (Random.Shared.NextDouble() * 0.5);
            var x = dummyAt.X + (float)(Math.Cos(angle) * StressSpawnRadius);
            var z = dummyAt.Z + (float)(Math.Sin(angle) * StressSpawnRadius);
            var y = _content.Terrain?.HeightAt(_world.MapId, x, z) ?? dummyAt.Y;
            var at = new DomainPosition(x, y, z);
            var spawnId = StressSpawnIdBase + _nextStressSpawn++;
            _shared.Adopt(wireId, spawnId, at);
            _shared.SetPosition(wireId, at);
            responses.Add(Reply(WebMessageTypes.EntitySpawned, StressBotSpawned(wireId, i, at, dummyAt)));
            _stressBots.Add(wireId);
            _stressBotSpawns[wireId] = spawnId;
            _stressBotHomes[wireId] = at;
            EngageStressBot(wireId, dummy.Id, responses);
        }
        _logger.LogInformation("Stress test by {Character}: {Count} bots on map {MapId}", _character.Name, request.Count, _world.MapId);
        return responses;
    }

    // Looks come from operator data where it names them, otherwise from the requester:
    // every value below is a combination the client can already draw.
    private PlayerSpawnedPayload StressBotSpawned(string wireId, int index, DomainPosition at, DomainPosition dummyAt)
    {
        var race = StressRaces[index % StressRaces.Length];
        var gender = (index / StressRaces.Length) % 2;
        var displayInfoId = PlayerBodies.DisplayInfoFor(race, gender) ?? _character!.DisplayInfoId;
        var look = _content.PlayerLooks.FirstOrDefault(dress => dress.Race == race && dress.Gender == gender);
        return new PlayerSpawnedPayload(
            WebNetworkId.FromString(wireId),
            new Position(at.X, at.Y, at.Z),
            FacingYaw(at, dummyAt),
            $"Stressbot{index + 1}",
            _character!.Level,
            _character.Class,
            race,
            gender,
            displayInfoId,
            PickLook(look?.Skins, index, _character.Skin),
            PickLook(look?.HairStyles, index, _character.HairStyle),
            PickLook(look?.HairColours, index, _character.HairColour),
            PickLook(look?.Faces, index, _character.Face),
            _character.Equipment,
            _combat.EffectiveMaxHealth(_attributeModifiers.GetFlatAmount(PlayerUnitId, AttributeIds.Stamina, Now)),
            _combat.EffectiveMaxHealth(_attributeModifiers.GetFlatAmount(PlayerUnitId, AttributeIds.Stamina, Now)),
            _spells.EffectiveMaxPower(_attributeModifiers.GetFlatAmount(PlayerUnitId, AttributeIds.Wisdom, Now)),
            _spells.EffectiveMaxPower(_attributeModifiers.GetFlatAmount(PlayerUnitId, AttributeIds.Wisdom, Now)));
    }

    private static int PickLook(IReadOnlyList<int>? options, int index, int fallback) =>
        options is { Count: > 0 } ? options[index % options.Count] : fallback;

    private void EngageStressBot(string wireId, string dummyId, List<string> responses)
    {
        var allowed = _sight.Entries.Keys.ToHashSet(StringComparer.Ordinal);
        var result = _combat.Start(wireId, dummyId, allowed, Now, CombatStatsFor(dummyId));
        if (result is null) return;
        // No HoldEngaged: the victim is a stationary dummy, and holding it would settle
        // a stop the next real fight then announces at a stale position.
        responses.Add(Reply(WebMessageTypes.AttackMeleeStart, new AttackMeleeStartPayload(
            WebNetworkId.FromString(wireId), WebNetworkId.FromString(dummyId))));
        if (result.EnteredCombat)
        {
            responses.Add(Reply(WebMessageTypes.AttackEnterCombat,
                new AttackEnterCombatPayload(WebNetworkId.FromString(wireId))));
            responses.Add(Reply(WebMessageTypes.AttackEnterCombat,
                new AttackEnterCombatPayload(WebNetworkId.FromString(dummyId))));
        }
    }

    // The closest living training dummy this session sees, so the bots gather around the right one.
    private (string Id, uint Entry)? FindStressDummy()
    {
        if (_world is null || _shared is null) return null;
        var player = _world.PlayerPosition;
        (string Id, uint Entry)? best = null;
        var bestSquared = double.MaxValue;
        foreach (var (wireId, entry) in _sight.Entries)
        {
            // The script is the rule: entry 1226 is bound to the dummy script by name,
            // so a table row without the extra-flags bit still counts.
            if (ScriptFor(wireId) is not TrainingDummyScript) continue;
            if (_combat.IsDead(wireId) || !_shared.IsAlive(wireId)) continue;
            if (!_shared.TryGetPosition(wireId, out var at)) continue;
            var dx = at.X - player.X;
            var dz = at.Z - player.Z;
            var squared = (double)dx * dx + dz * dz;
            if (squared >= bestSquared) continue;
            bestSquared = squared;
            best = (wireId, entry);
        }
        return best;
    }

    // The refusal names what is actually in sight, so a missing flag on the dummy's
    // table row shows up as an entry number instead of a mystery.
    private string NoDummyReason()
    {
        if (_sight.Entries.Count == 0)
            return "There is no training dummy in sight (nothing else in sight either; move closer).";
        var entries = _sight.Entries.Values.Distinct().Take(10);
        return $"There is no training dummy in sight ({_sight.Entries.Count} creatures, entries {string.Join(",", entries)}).";
    }

    // Bots wander through the shared wander math but answer only to this tick: they are
    // in no sight set, so the idle-movement pass never sees them. Swings stay range-gated:
    // a bot that wandered off holds fire until it drifts back.
    private void TickStressBots(DateTimeOffset now, List<string> responses)
    {
        if (_stressBots.Count == 0 || _shared is null) return;
        var dummyAlive = _stressDummyId is not null && _sight.Contains(_stressDummyId)
            && !_combat.IsDead(_stressDummyId) && _shared.IsAlive(_stressDummyId);
        var dummyAt = dummyAlive && _shared.TryGetPosition(_stressDummyId!, out var found) ? found : default;
        foreach (var wireId in _stressBots.ToArray())
        {
            if (_combat.IsDead(wireId)) continue;
            if (!dummyAlive)
            {
                if (_combat.CurrentTargetOf(wireId) is not null)
                {
                    _combat.Disengage(wireId);
                    responses.Add(Reply(WebMessageTypes.AttackHalted,
                        new AttackHaltedPayload(WebNetworkId.FromString(wireId))));
                }
                continue;
            }
            if (!_shared.TryGetPosition(wireId, out var at)
                || !_stressBotHomes.TryGetValue(wireId, out var home)) continue;
            var dx = at.X - dummyAt.X;
            var dz = at.Z - dummyAt.Z;
            _combat.SetInMeleeReach(wireId, Math.Sqrt(dx * dx + dz * dz) <= MeleeRange);
            var update = _shared.AdvanceWander(wireId, at, home, StressWanderRadius, now);
            if (update is null) continue;
            _shared.SetPosition(wireId, update.Position);
            if (!update.IsTransition) continue;
            responses.Add(Reply(WebMessageTypes.EntityMoved, new EntityMovedPayload(
                NetworkId: WebNetworkId.FromString(wireId),
                Position: new Position(update.Position.X, update.Position.Y, update.Position.Z),
                Yaw: update.Target is { } destination ? FacingYaw(update.Position, destination) : null,
                Moving: update.Moving,
                Speed: CreatureWanderService.Speed,
                Target: update.Target is { } target ? new Position(target.X, target.Y, target.Z) : null)));
        }
    }

    // A rerun replaces the pack: the old bots halt and leave the view, and their shared state goes with them.
    private void ClearStressBots(List<string>? responses)
    {
        if (_stressBots.Count == 0) return;
        foreach (var wireId in _stressBots.ToArray())
        {
            if (_combat.CurrentTargetOf(wireId) is not null)
            {
                _combat.Disengage(wireId);
                responses?.Add(Reply(WebMessageTypes.AttackHalted,
                    new AttackHaltedPayload(WebNetworkId.FromString(wireId))));
            }
            if (responses is not null)
                responses.Add(Reply(WebMessageTypes.EntityRemoved,
                    new EntityRemovedPayload(WebNetworkId.FromString(wireId))));
            DropStressBot(wireId);
        }
        _stressDummyId = null;
    }

    private void DropStressBot(string wireId)
    {
        if (_stressDummyId is not null)
        {
            _shared?.RemoveFighter(_stressDummyId, wireId);
            _shared?.ForgetAttacker(_stressDummyId, wireId);
        }
        if (_stressBotSpawns.Remove(wireId, out var spawnId)) _shared?.ForgetSpawn(spawnId);
        _stressBotHomes.Remove(wireId);
        _stressBots.Remove(wireId);
    }
}
