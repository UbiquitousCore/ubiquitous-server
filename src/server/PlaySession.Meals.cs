using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    // Every food row in the tables pays out on this period, for a total of five payments over its 20 seconds.
    private static readonly TimeSpan MealTickEvery = TimeSpan.FromSeconds(4);

    private sealed class Meal(uint spellId, int health, int mana, int paymentsLeft, DateTimeOffset nextPayment)
    {
        public uint SpellId { get; } = spellId;
        public int Health { get; } = health;
        public int Mana { get; } = mana;
        public int PaymentsLeft { get; set; } = paymentsLeft;
        public DateTimeOffset NextPayment { get; set; } = nextPayment;
    }

    private Meal? _meal;

    public bool IsEating => _meal is not null;

    private IReadOnlyList<string> ResolveMeal(
        WebNetworkId casterId, uint spellId, SpellDefinition spell, DateTimeOffset now)
    {
        var responses = new List<string>();
        EndMeal(responses);
        Dismount(now, responses);
        _meal = new Meal(
            spellId, spell.AuraEatPerTick ?? 0, spell.AuraDrinkPerTick ?? 0,
            (int)(TimeSpan.FromMilliseconds(spell.DurationMs) / MealTickEvery), now + MealTickEvery);
        MarkUnitValues(PlayerUnitId, new Dictionary<string, int> { [WireCodes.UnitValue.Eating] = 1 }, now);
        responses.Add(Reply(WebMessageTypes.SpellReleased, new SpellReleasedPayload(spellId, casterId, [casterId], [], spell.Speed, Aimed: false)));
        responses.Add(Reply(WebMessageTypes.AuraApplied, new AuraAppliedPayload(
            spellId, casterId, casterId, Harmful: 0, MaxDuration: spell.DurationMs, Remaining: spell.DurationMs)));
        return responses;
    }

    private void EndMeal(List<string> responses)
    {
        if (_meal is not { } meal) return;
        _meal = null;
        MarkUnitValues(PlayerUnitId, new Dictionary<string, int> { [WireCodes.UnitValue.Eating] = 0 }, Now);
        var unit = WebNetworkId.FromString(PlayerUnitId);
        responses.Add(Reply(WebMessageTypes.AuraEnded, new AuraEndedPayload(meal.SpellId, unit, unit)));
    }

    private void TickMeal(DateTimeOffset now, List<string> responses)
    {
        if (_meal is not { } meal) return;
        if (_combat.IsPlayerDead)
        {
            EndMeal(responses);
            return;
        }

        while (_meal is not null && now >= meal.NextPayment)
        {
            PayMeal(meal, now);
            meal.NextPayment += MealTickEvery;
            if (--meal.PaymentsLeft <= 0) EndMeal(responses);
        }
    }

    private void PayMeal(Meal meal, DateTimeOffset now)
    {
        var playerId = PlayerUnitId;
        if (meal.Health > 0)
        {
            var stamina = _attributeModifiers.GetFlatAmount(playerId, AttributeIds.Stamina, now);
            if (_combat.HealPlayer(meal.Health, stamina) is { } healed)
                MarkUnitValues(playerId, new Dictionary<string, int>
                {
                    [WireCodes.UnitValue.Health] = healed.FinalHealth,
                    [WireCodes.UnitValue.MaxHealth] = healed.MaxHealth,
                }, now);
        }

        if (meal.Mana > 0)
        {
            var wisdom = _attributeModifiers.GetFlatAmount(playerId, AttributeIds.Wisdom, now);
            if (_spells.RestorePower(PowerKinds.Mana, meal.Mana, wisdom) is not null)
                MarkUnitValues(playerId, new Dictionary<string, int> { [WireCodes.UnitValue.Power] = _spells.Power }, now);
        }
    }
}
