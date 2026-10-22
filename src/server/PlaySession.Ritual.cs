using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Scripts;
using DomainPosition = Ubiquitous.Common.Position;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private sealed class RitualRun(Ritual ritual, RitualSiteDefinition site, string keeperId, DateTimeOffset now)
    {
        public Ritual Ritual { get; } = ritual;
        public RitualSiteDefinition Site { get; } = site;
        public string KeeperId { get; set; } = keeperId;
        public RitualStage SeenStage { get; set; } = ritual.Stage;
        public DateTimeOffset StageSince { get; set; } = now;
        public HashSet<string> Wave { get; } = new(StringComparer.Ordinal);
        public string? BossId { get; set; }
        public int NextGate { get; set; }
    }

    // Own choice: how close the keeper must come to the ritual ground to count as there.
    private const double KeeperArrivesWithin = 4.0;

    private RitualRun? _ritual;

    // The first session to talk to the keeper leads the ritual for the whole copy; its partners see what it spawns.
    private bool StartRitual(string keeperId, uint gossipScript, DateTimeOffset now, List<string> responses)
    {
        if (_instanceWorld is null || _instanceKey is null || !_sight.Entries.TryGetValue(keeperId, out var entry))
            return false;
        var site = _content.RitualSites.FirstOrDefault(candidate =>
            candidate.MapId == _instanceWorld.MapId && candidate.GossipScript == gossipScript);
        if (site is null || _copy.AuthoredEntry(entry) != site.KeeperEntry) return false;

        if (_ritual is null)
        {
            if (ScriptRegistry.RitualFor(site.Ritual) is not { } ritual) return false;
            if (!_instances.Reach(_instanceKey, $"{site.Ritual}.led")) return false;
            _ritual = new RitualRun(ritual, site, keeperId, now);
        }

        _ritual.KeeperId = keeperId;
        if (_ritual.Ritual.Start() is not { } orders) return false;
        Obey(_ritual, orders, now, responses);
        return true;
    }

    private void TickRitual(DateTimeOffset now, List<string> responses)
    {
        if (_ritual is not { } run) return;
        var site = Grounded(run.Site.Site);
        var keeperAlive = !BodyGone(run.KeeperId);
        var keeperAt = CreaturePosition(run.KeeperId);
        var arrived = keeperAt is { } standing && Apart(standing, site) <= KeeperArrivesWithin;
        if (run.Ritual.Stage == RitualStage.Escort && keeperAlive && !arrived && keeperAt is { } from)
            ChaseTowards(run.KeeperId, from, site, now, responses);

        run.Wave.RemoveWhere(BodyGone);
        var view = new RitualView(
            now - run.StageSince, keeperAlive, arrived, run.Wave.Count, run.BossId is { } boss && BodyGone(boss));
        if (run.Ritual.Advance(view) is { } orders) Obey(run, orders, now, responses);
    }

    private void Obey(RitualRun run, RitualOrders orders, DateTimeOffset now, List<string> responses)
    {
        if (orders.Say is { } line) YellAs(run.KeeperId, line, responses);

        if (orders.KeeperCasts is uint channel)
        {
            var at = CreaturePosition(run.KeeperId) ?? Grounded(run.Site.Site);
            Vanish(run.KeeperId, now, responses);
            if (SummonAt(run.Site.CastingKeeperEntry, at, responses) is { } caster)
            {
                run.KeeperId = caster;
                var castId = _copy.Spell(channel);
                if (_content.Spells.TryGetValue(castId, out var spell))
                    LandCreatureSpell(caster, caster, castId, spell, now, responses);
            }
        }

        var home = Grounded(run.Site.Site);
        foreach (var entry in orders.Wave ?? [])
        {
            if (SummonAt(entry, NextGate(run), responses) is not { } creature) continue;
            run.Wave.Add(creature);
            BeginHunt(creature, run.KeeperId, home, now, responses);
        }

        if (orders.Boss is uint bossEntry && SummonAt(bossEntry, NextGate(run), responses) is { } bossId)
        {
            run.BossId = bossId;
            BeginHunt(bossId, run.KeeperId, home, now, responses);
        }

        if (orders.Fails)
        {
            foreach (var creature in run.Wave) Vanish(creature, now, responses);
            run.Wave.Clear();
            if (run.BossId is { } boss) Vanish(boss, now, responses);
            run.BossId = null;
        }

        if (orders.Resets)
        {
            Vanish(run.KeeperId, now, responses);
            if (SummonAt(run.Site.KeeperEntry, Grounded(run.Site.KeeperStart), responses) is { } keeper)
                run.KeeperId = keeper;
        }

        if (run.Ritual.Stage != run.SeenStage)
        {
            run.SeenStage = run.Ritual.Stage;
            run.StageSince = now;
        }
    }

    private DomainPosition NextGate(RitualRun run)
    {
        var gates = run.Site.WaveGates;
        var turn = run.NextGate++;
        var gate = Grounded(gates[turn % gates.Count]);
        var spread = (turn / gates.Count) * 1.3;
        return new DomainPosition((float)(gate.X + Math.Cos(spread) * 2), gate.Y, (float)(gate.Z + Math.Sin(spread) * 2));
    }

    // Points this project placed by hand sit on the terrain where it is known; the operator's own spawns keep the
    // heights their tables give.
    private DomainPosition Grounded(MapPoint point)
    {
        var height = _instanceWorld is null ? null : _content.Terrain?.HeightAt(_instanceWorld.MapId, point.X, point.Z);
        return new DomainPosition(point.X, height ?? point.Y, point.Z);
    }

    private void Vanish(string wireId, DateTimeOffset now, List<string> responses)
    {
        if (!_sight.Contains(wireId)) return;
        _combat.MarkDead(wireId);
        _creaturePulses.Clear(wireId);
        _creatureCasts.Remove(wireId);
        KillShared(wireId, now);
        _deathLines.RemoveAll(line => string.Equals(line.WireId, wireId, StringComparison.Ordinal));
        Forget(wireId);
        responses.Add(Reply(WebMessageTypes.EntityRemoved, new EntityRemovedPayload(WebNetworkId.FromString(wireId))));
    }
}
