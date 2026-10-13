using System.Buffers.Binary;
using Ubiquitous.AssetExtractor;
using Ubiquitous.Data;

namespace Ubiquitous.Integration.Tests;

public class SpellEffectColumnsTests
{
    private const string Build114Format = "uuuuufuuiiiuuuuuuuuuusss";

    [Fact]
    public void Build114_ReadsWhomTheEffectLandsOnAndItsConditions()
    {
        var data = BuildRow(
            uints: new Dictionary<int, uint> { [0] = 5, [4] = 2, [6] = 0, [7] = 9, [11] = 900, [17] = 8 },
            ints: new Dictionary<int, int> { [9] = 100 });

        var effect = Assert.Single(SpellEffectTableMapper.MapEffects(FTableDocument.Load(data), SpellEffectColumns.Build114));

        Assert.Equal(SpellEffectTargets.SingleEnemy, effect.Target);
        Assert.Equal(0u, effect.TargetCondition);
        Assert.Equal(8u, effect.CasterCondition);
    }

    [Fact]
    public void Build114_MapsARowShapedLikeTheTable()
    {
        var data = BuildRow(
            uints: new Dictionary<int, uint> { [0] = 3, [6] = 0, [7] = 1 },
            ints: new Dictionary<int, int> { [8] = 1, [9] = 20, [10] = 10 });

        var effect = Assert.Single(SpellEffectTableMapper.MapEffects(FTableDocument.Load(data), SpellEffectColumns.Build114));

        Assert.Equal(3u, effect.Id);
        Assert.Equal(SpellEffectColumns.DirectDamageType, effect.Type);
        Assert.Equal(SpellEffectColumns.DirectDamageSubType, effect.SubType);
        Assert.Equal(1, effect.School);
        Assert.Equal(20, effect.BaseValue);
        Assert.Equal(10, effect.Spread);
    }

    [Fact]
    public void Build114_MapsAHealEffect()
    {
        var data = BuildRow(
            uints: new Dictionary<int, uint> { [0] = 5, [6] = 0, [7] = 3 },
            ints: new Dictionary<int, int> { [9] = 140, [10] = 40 });

        var effect = Assert.Single(SpellEffectTableMapper.MapEffects(FTableDocument.Load(data), SpellEffectColumns.Build114));

        Assert.Equal(SpellEffectColumns.HealType, effect.Type);
        Assert.Equal(SpellEffectColumns.HealSubType, effect.SubType);
        Assert.Equal(140, effect.BaseValue);
    }

    [Fact]
    public void Build114_MapsAPeriodicDamageAura()
    {
        var data = BuildRow(
            uints: new Dictionary<int, uint> { [0] = 64, [6] = 1, [7] = 1 },
            ints: new Dictionary<int, int> { [8] = 6, [9] = 12 });

        var effect = Assert.Single(SpellEffectTableMapper.MapEffects(FTableDocument.Load(data), SpellEffectColumns.Build114));

        Assert.Equal(SpellEffectColumns.AuraType, effect.Type);
        Assert.Equal(SpellEffectColumns.PeriodicDamageSubType, effect.SubType);
        Assert.Equal(12, effect.BaseValue);
    }

    [Fact]
    public void Build114_MapsAPeriodicHealAura()
    {
        var data = BuildRow(
            uints: new Dictionary<int, uint> { [0] = 62, [6] = 1, [7] = 2 },
            ints: new Dictionary<int, int> { [8] = -1, [9] = 20 });

        var effect = Assert.Single(SpellEffectTableMapper.MapEffects(FTableDocument.Load(data), SpellEffectColumns.Build114));

        Assert.Equal(SpellEffectColumns.AuraType, effect.Type);
        Assert.Equal(SpellEffectColumns.PeriodicHealSubType, effect.SubType);
        Assert.Equal(20, effect.BaseValue);
    }

    [Fact]
    public void Build114_MapsAStunAura()
    {
        var data = BuildRow(
            uints: new Dictionary<int, uint> { [0] = 61, [6] = 1, [7] = 10 },
            ints: new Dictionary<int, int>());

        var effect = Assert.Single(SpellEffectTableMapper.MapEffects(FTableDocument.Load(data), SpellEffectColumns.Build114));

        Assert.Equal(SpellEffectColumns.AuraType, effect.Type);
        Assert.Equal(SpellEffectColumns.StunSubType, effect.SubType);
        Assert.Equal(0, effect.BaseValue);
    }

    [Fact]
    public void Build114_MapsASleepAura()
    {
        var data = BuildRow(
            uints: new Dictionary<int, uint> { [0] = 65, [6] = 1, [7] = 13 },
            ints: new Dictionary<int, int>());

        var effect = Assert.Single(SpellEffectTableMapper.MapEffects(FTableDocument.Load(data), SpellEffectColumns.Build114));

        Assert.Equal(SpellEffectColumns.AuraType, effect.Type);
        Assert.Equal(SpellEffectColumns.SleepSubType, effect.SubType);
    }

    [Fact]
    public void Build114_MapsASilenceAura()
    {
        var data = BuildRow(
            uints: new Dictionary<int, uint> { [0] = 90, [6] = 1, [7] = 31 },
            ints: new Dictionary<int, int>());

        var effect = Assert.Single(SpellEffectTableMapper.MapEffects(FTableDocument.Load(data), SpellEffectColumns.Build114));

        Assert.Equal(SpellEffectColumns.AuraType, effect.Type);
        Assert.Equal(SpellEffectColumns.SilenceSubType, effect.SubType);
        Assert.Equal(0, effect.BaseValue);
    }

    [Fact]
    public void Build114_MapsAFlatStrengthModifier()
    {
        var data = BuildRow(
            uints: new Dictionary<int, uint> { [0] = 61, [6] = 1, [7] = 4, [11] = 25 },
            ints: new Dictionary<int, int> { [9] = 10 });

        var effect = Assert.Single(SpellEffectTableMapper.MapEffects(FTableDocument.Load(data), SpellEffectColumns.Build114));

        Assert.Equal(SpellEffectColumns.AuraType, effect.Type);
        Assert.Equal(SpellEffectColumns.AttributeModifierFlatSubType, effect.SubType);
        Assert.Equal(AttributeIds.Strength, (int)effect.EffectArgument);
        Assert.Equal(10, effect.BaseValue);
    }

    [Fact]
    public void Build114_MapsANegativePercentArmorModifier()
    {
        var data = BuildRow(
            uints: new Dictionary<int, uint> { [0] = 88, [6] = 1, [7] = 14, [11] = 0 },
            ints: new Dictionary<int, int> { [9] = -40 });

        var effect = Assert.Single(SpellEffectTableMapper.MapEffects(FTableDocument.Load(data), SpellEffectColumns.Build114));

        Assert.Equal(SpellEffectColumns.AuraType, effect.Type);
        Assert.Equal(SpellEffectColumns.AttributeModifierPercentSubType, effect.SubType);
        Assert.Equal(AttributeIds.Armor, (int)effect.EffectArgument);
        Assert.Equal(-40, effect.BaseValue);
    }

    [Fact]
    public void Build114_MapsASlowAura()
    {
        var data = BuildRow(
            uints: new Dictionary<int, uint> { [0] = 41, [6] = 1, [7] = 7 },
            ints: new Dictionary<int, int> { [9] = 50 });

        var effect = Assert.Single(SpellEffectTableMapper.MapEffects(FTableDocument.Load(data), SpellEffectColumns.Build114));

        Assert.Equal(SpellEffectColumns.AuraType, effect.Type);
        Assert.Equal(SpellEffectColumns.SlowSubType, effect.SubType);
        Assert.Equal(50, effect.BaseValue);
    }

    [Fact]
    public void Build114_MapsASpeedupAura()
    {
        var data = BuildRow(
            uints: new Dictionary<int, uint> { [0] = 170, [6] = 1, [7] = 16 },
            ints: new Dictionary<int, int> { [9] = 20 });

        var effect = Assert.Single(SpellEffectTableMapper.MapEffects(FTableDocument.Load(data), SpellEffectColumns.Build114));

        Assert.Equal(SpellEffectColumns.AuraType, effect.Type);
        Assert.Equal(SpellEffectColumns.SpeedupSubType, effect.SubType);
        Assert.Equal(20, effect.BaseValue);
    }

    [Fact]
    public void Build114_MapsASpeedupAuraWithANegativeValue()
    {
        var data = BuildRow(
            uints: new Dictionary<int, uint> { [0] = 3752, [6] = 1, [7] = 16 },
            ints: new Dictionary<int, int> { [9] = -50 });

        var effect = Assert.Single(SpellEffectTableMapper.MapEffects(FTableDocument.Load(data), SpellEffectColumns.Build114));

        Assert.Equal(SpellEffectColumns.SpeedupSubType, effect.SubType);
        Assert.Equal(-50, effect.BaseValue);
    }

    [Fact]
    public void Build114_MapsAMeleeAttackSpeedModifier()
    {
        var data = BuildRow(
            uints: new Dictionary<int, uint> { [0] = 386, [6] = 1, [7] = 26 },
            ints: new Dictionary<int, int> { [9] = 30 });

        var effect = Assert.Single(SpellEffectTableMapper.MapEffects(FTableDocument.Load(data), SpellEffectColumns.Build114));

        Assert.Equal(SpellEffectColumns.AuraType, effect.Type);
        Assert.Equal(SpellEffectColumns.MeleeAttackSpeedModifierSubType, effect.SubType);
        Assert.Equal(30, effect.BaseValue);
    }

    [Fact]
    public void Build114_MapsARangeAttackSpeedModifier()
    {
        var data = BuildRow(
            uints: new Dictionary<int, uint> { [0] = 961, [6] = 1, [7] = 27 },
            ints: new Dictionary<int, int> { [9] = 50 });

        var effect = Assert.Single(SpellEffectTableMapper.MapEffects(FTableDocument.Load(data), SpellEffectColumns.Build114));

        Assert.Equal(SpellEffectColumns.RangeAttackSpeedModifierSubType, effect.SubType);
        Assert.Equal(50, effect.BaseValue);
    }

    [Fact]
    public void Build114_MapsAChillAuraWithNoMagnitude()
    {
        var data = BuildRow(
            uints: new Dictionary<int, uint> { [0] = 117, [6] = 1, [7] = 11, [11] = 0 },
            ints: new Dictionary<int, int> { [9] = 0 });

        var effect = Assert.Single(SpellEffectTableMapper.MapEffects(FTableDocument.Load(data), SpellEffectColumns.Build114));

        Assert.Equal(SpellEffectColumns.AuraType, effect.Type);
        Assert.Equal(SpellEffectColumns.ChillSubType, effect.SubType);
        Assert.Equal(0, effect.BaseValue);
        Assert.Equal(0u, effect.EffectArgument);
    }

    [Fact]
    public void Build114_MapsATriggerSpellAura()
    {
        var data = BuildRow(
            uints: new Dictionary<int, uint> { [0] = 156, [6] = 1, [7] = 15, [11] = 95 },
            ints: new Dictionary<int, int> { [9] = 20 });

        var effect = Assert.Single(SpellEffectTableMapper.MapEffects(FTableDocument.Load(data), SpellEffectColumns.Build114));

        Assert.Equal(SpellEffectColumns.AuraType, effect.Type);
        Assert.Equal(SpellEffectColumns.TriggerSpellSubType, effect.SubType);
        Assert.Equal(20, effect.BaseValue);
        Assert.Equal(95u, effect.EffectArgument);
    }

    [Fact]
    public void Build114_MapsAPeriodicTriggerSpellAura()
    {
        var data = BuildRow(
            uints: new Dictionary<int, uint> { [0] = 3736, [6] = 1, [7] = 37, [11] = 2679 },
            ints: new Dictionary<int, int> { [9] = 100 });

        var effect = Assert.Single(SpellEffectTableMapper.MapEffects(FTableDocument.Load(data), SpellEffectColumns.Build114));

        Assert.Equal(SpellEffectColumns.AuraType, effect.Type);
        Assert.Equal(SpellEffectColumns.PeriodicTriggerSpellSubType, effect.SubType);
        Assert.Equal(100, effect.BaseValue);
        Assert.Equal(2679u, effect.EffectArgument);
    }

    [Fact]
    public void Build114_MapsAModifierSpellFlatAura()
    {
        var data = BuildRow(
            uints: new Dictionary<int, uint> { [0] = 210, [6] = 1, [7] = 8, [11] = 6 },
            ints: new Dictionary<int, int> { [9] = 500 });

        var effect = Assert.Single(SpellEffectTableMapper.MapEffects(FTableDocument.Load(data), SpellEffectColumns.Build114));

        Assert.Equal(SpellEffectColumns.AuraType, effect.Type);
        Assert.Equal(SpellEffectColumns.ModifierSpellFlatSubType, effect.SubType);
        Assert.Equal(500, effect.BaseValue);
        Assert.Equal(6u, effect.EffectArgument);
    }

    [Fact]
    public void Build114_MapsAModifierSpellPercentAura()
    {
        var data = BuildRow(
            uints: new Dictionary<int, uint> { [0] = 211, [6] = 1, [7] = 9, [11] = 11 },
            ints: new Dictionary<int, int> { [9] = 20 });

        var effect = Assert.Single(SpellEffectTableMapper.MapEffects(FTableDocument.Load(data), SpellEffectColumns.Build114));

        Assert.Equal(SpellEffectColumns.AuraType, effect.Type);
        Assert.Equal(SpellEffectColumns.ModifierSpellPercentSubType, effect.SubType);
        Assert.Equal(20, effect.BaseValue);
        Assert.Equal(11u, effect.EffectArgument);
    }

    [Fact]
    public void Build114_MapsARangedWeaponHitEffect()
    {
        var data = BuildRow(
            uints: new Dictionary<int, uint> { [0] = 5394, [6] = 0, [7] = 28, [11] = 100 },
            ints: new Dictionary<int, int> { [8] = 7, [9] = 15 });

        var effect = Assert.Single(SpellEffectTableMapper.MapEffects(FTableDocument.Load(data), SpellEffectColumns.Build114));

        Assert.Equal(SpellEffectColumns.DirectDamageType, effect.Type);
        Assert.Equal(SpellEffectColumns.RangedWeaponHitSubType, effect.SubType);
        Assert.Equal(7, effect.School);
        Assert.Equal(15, effect.BaseValue);
        Assert.Equal(100u, effect.EffectArgument);
    }

    [Fact]
    public void Build114_MapsAnInstantTriggerSpellEffect()
    {
        var data = BuildRow(
            uints: new Dictionary<int, uint> { [0] = 5396, [6] = 0, [7] = 9, [11] = 5342 },
            ints: new Dictionary<int, int> { [9] = 100 });

        var effect = Assert.Single(SpellEffectTableMapper.MapEffects(FTableDocument.Load(data), SpellEffectColumns.Build114));

        Assert.Equal(SpellEffectColumns.DirectDamageType, effect.Type);
        Assert.Equal(SpellEffectColumns.InstantTriggerSpellSubType, effect.SubType);
        Assert.Equal(100, effect.BaseValue);
        Assert.Equal(5342u, effect.EffectArgument);
    }

    [Fact]
    public void Build114_MapsAMainHandWeaponHitEffect()
    {
        var data = BuildRow(
            uints: new Dictionary<int, uint> { [0] = 5570, [6] = 0, [7] = 23, [11] = 110 },
            ints: new Dictionary<int, int> { [9] = 10 });

        var effect = Assert.Single(SpellEffectTableMapper.MapEffects(FTableDocument.Load(data), SpellEffectColumns.Build114));

        Assert.Equal(SpellEffectColumns.DirectDamageType, effect.Type);
        Assert.Equal(SpellEffectColumns.MainHandWeaponHitSubType, effect.SubType);
        Assert.Equal(10, effect.BaseValue);
        Assert.Equal(110u, effect.EffectArgument);
    }

    [Fact]
    public void Build114_MapsAStealthAura()
    {
        var data = BuildRow(
            uints: new Dictionary<int, uint> { [0] = 5565, [6] = 1, [7] = 23, [11] = 0 },
            ints: new Dictionary<int, int> { [9] = 0 });

        var effect = Assert.Single(SpellEffectTableMapper.MapEffects(FTableDocument.Load(data), SpellEffectColumns.Build114));

        Assert.Equal(SpellEffectColumns.AuraType, effect.Type);
        Assert.Equal(SpellEffectColumns.StealthSubType, effect.SubType);
        Assert.Equal(0, effect.BaseValue);
        Assert.Equal(0u, effect.EffectArgument);
    }

    [Fact]
    public void Build114_StealthAndMainhandWeaponDamageShareASubTypeNumberAndDifferByTypeAlone()
    {
        Assert.Equal(
            SpellEffectColumns.MainHandWeaponHitSubType, SpellEffectColumns.StealthSubType);
        Assert.NotEqual(SpellEffectColumns.DirectDamageType, SpellEffectColumns.AuraType);
    }

    [Fact]
    public void Build114_KeepsANegativeBaseValueIntact()
    {
        var data = BuildRow(
            uints: new Dictionary<int, uint> { [0] = 99 },
            ints: new Dictionary<int, int> { [9] = -2000 });

        var effect = Assert.Single(SpellEffectTableMapper.MapEffects(FTableDocument.Load(data), SpellEffectColumns.Build114));

        Assert.Equal(-2000, effect.BaseValue);
    }

    private static byte[] BuildRow(IReadOnlyDictionary<int, uint> uints, IReadOnlyDictionary<int, int> ints)
    {
        int cols = Build114Format.Length;
        var widths = new int[cols];
        for (int i = 0; i < cols; i++)
        {
            widths[i] = Build114Format[i] switch
            {
                's' => 6,
                'h' => 2,
                'c' => 1,
                _ => 4,
            };
        }

        int entrySize = widths.Sum();
        const int headerSize = 44;
        const int rows = 1;
        int offsetIndex = headerSize;
        int offsetFormat = offsetIndex + rows * 4;
        int offsetEntry = offsetFormat + cols;
        int offsetStrings = offsetEntry + entrySize;
        var data = new byte[offsetStrings];
        WriteAscii(data, 0, "FTABLE");
        WriteU32(data, 8, 0x100000);
        WriteU32(data, 12, rows);
        WriteU32(data, 16, cols);
        WriteU32(data, 20, offsetIndex);
        WriteU32(data, 24, offsetFormat);
        WriteU32(data, 28, offsetEntry);
        WriteU32(data, 32, entrySize);
        WriteU32(data, 36, offsetStrings);
        WriteU32(data, 40, 0);
        WriteAscii(data, offsetFormat, Build114Format);

        int at = offsetEntry;
        for (int i = 0; i < cols; i++)
        {
            switch (Build114Format[i])
            {
                case 's':
                    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), 0);
                    BitConverter.GetBytes((short)0).CopyTo(data, at + 4);
                    break;
                case 'f':
                    BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at, 4), 0f);
                    break;
                case 'i':
                    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(at, 4), ints.GetValueOrDefault(i));
                    break;
                default:
                    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), uints.GetValueOrDefault(i));
                    break;
            }

            at += widths[i];
        }

        return data;
    }

    private static void WriteU32(byte[] data, int at, int value)
    {
        data[at] = (byte)value;
        data[at + 1] = (byte)(value >> 8);
        data[at + 2] = (byte)(value >> 16);
        data[at + 3] = (byte)(value >> 24);
    }

    private static void WriteAscii(byte[] data, int at, string text)
    {
        for (int i = 0; i < text.Length && at + i < data.Length; i++)
        {
            data[at + i] = (byte)text[i];
        }
    }
}
