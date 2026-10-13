using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.World;
using DomainPosition = Ubiquitous.Common.Position;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private int? AggroRangeFor(CreatureDefinition? creature, bool isServiceNpc)
    {
        if (creature?.NoticeDistance is int sight && sight > 0) return NoticeRangeFor(sight, creature);
        if (isServiceNpc) return null;
        if (FactionStance.Resolve(creature?.FactionType) == UnitFactionTypes.Hostile)
            return NoticeRangeFor(DefaultAggroRange, creature);
        return null;
    }

    private int NoticeRangeFor(int baseRange, CreatureDefinition? creature) =>
        EncounterRules.NoticeRange(baseRange, creature, _level);

    private bool IsServiceNpc(uint entry) =>
        _content.Npcs.TryGetValue(entry, out var npc)
        && (npc.OffersQuests || (npc.Flags & (NpcFlags.Vendor | NpcFlags.Teleporter
            | NpcFlags.Auctioneer)) != 0);

    private CreatureDefinition? CreatureFor(string wireId) =>
        _sight.Entries.TryGetValue(wireId, out var entry) && _content.Creatures.TryGetValue(entry, out var creature)
            ? creature
            : null;

    private static int FactionOf(CreatureDefinition? creature) => FactionStance.Resolve(creature?.FactionType);

    private bool IsFriendlyCreature(string wireId) =>
        FactionStance.Between(UnitFactionTypes.Townsfolk, FactionOf(CreatureFor(wireId))) == Stance.Friendly;

    private static bool IsHostile(CreatureDefinition? creature) =>
        FactionStance.Between(UnitFactionTypes.Townsfolk, FactionOf(creature)) == Stance.Hostile;

    private bool SimulatesNow(string wireId, CreatureSpawnDefinition spawn, DateTimeOffset now)
    {
        if (_shared is null || _world is null) return true;
        var home = new DomainPosition(spawn.X, spawn.Y, spawn.Z);
        return _shared.IsSimulating(wireId, home, _world.PlayerPosition, now);
    }
}
