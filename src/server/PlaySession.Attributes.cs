using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private Dictionary<string, int>? _sentAttributes;

    private Dictionary<string, int> AttributeValues(DateTimeOffset now)
    {
        var own = _combat.OwnAttributes;
        int Bonus(int attributeId) => _attributeModifiers.GetFlatAmount(PlayerUnitId, attributeId, now);
        int Current(int ownValue, int attributeId) => Math.Max(0, ownValue + Bonus(attributeId));
        static int Chance(double chance) => (int)Math.Round(chance * 10000, MidpointRounding.AwayFromZero);
        int armor = Bonus(AttributeIds.Armor);

        return new Dictionary<string, int>
        {
            [WireCodes.UnitValue.Strength] = Current(own.Strength, AttributeIds.Strength),
            [WireCodes.UnitValue.Agility] = Current(own.Agility, AttributeIds.Agility),
            [WireCodes.UnitValue.Stamina] = Current(own.Stamina, AttributeIds.Stamina),
            [WireCodes.UnitValue.Wisdom] = Current(own.Intellect, AttributeIds.Wisdom),
            [WireCodes.UnitValue.BaseStrength] = own.Strength,
            [WireCodes.UnitValue.BaseAgility] = own.Agility,
            [WireCodes.UnitValue.BaseStamina] = own.Stamina,
            [WireCodes.UnitValue.BaseWisdom] = own.Intellect,
            [WireCodes.UnitValue.AttackPower] =
                _combat.OwnAttackPower(Bonus(AttributeIds.Strength), Bonus(AttributeIds.Agility)),
            [WireCodes.UnitValue.HitChance] = Chance(CombatService.HitChance(Bonus(AttributeIds.HitRating))),
            [WireCodes.UnitValue.CritChance] = Chance(CombatService.CritChance(Bonus(AttributeIds.CriticalStrike))),
            [WireCodes.UnitValue.SpellPower] = Bonus(AttributeIds.SpellPower),
            [WireCodes.UnitValue.HealingPower] = Bonus(AttributeIds.HealingPower),
            [WireCodes.UnitValue.SpellHitChance] = Chance(SpellService.SpellHitChance),
            [WireCodes.UnitValue.SpellCritChance] =
                Chance(SpellService.SpellCritChance(Bonus(AttributeIds.SpellCriticalRating))),
            [WireCodes.UnitValue.Armor] = armor,
            [WireCodes.UnitValue.ArmorMitigation] = Chance(Math.Max(0, CombatService.ArmorMitigation(armor))),
            [WireCodes.UnitValue.DodgeChance] = Chance(CombatService.DodgeChance(
                Bonus(AttributeIds.Agility), Bonus(AttributeIds.DodgeRating))),
            [WireCodes.UnitValue.ParryChance] = Chance(CombatService.ParryChance(Bonus(AttributeIds.ParryRating))),
            [WireCodes.UnitValue.BlockChance] = Chance(CombatService.BlockChance(Bonus(AttributeIds.BlockRating))),
        };
    }

    private void AddAttributesTo(Dictionary<string, int> values, DateTimeOffset now)
    {
        _sentAttributes = AttributeValues(now);
        foreach (var (key, value) in _sentAttributes) values[key] = value;
    }

    private void SendChangedAttributes(DateTimeOffset now, List<string> responses)
    {
        var values = AttributeValues(now);
        if (_sentAttributes is not null && values.All(pair => _sentAttributes.GetValueOrDefault(pair.Key) == pair.Value))
            return;
        _sentAttributes = values;
        responses.Add(Reply(WebMessageTypes.PlayerValuesChanged, new PlayerValuesChangedPayload(new Dictionary<string, int>(values))));
    }
}
