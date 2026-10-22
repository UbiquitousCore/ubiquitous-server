using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class CombatServiceTests
{
    private static readonly HashSet<string> Visible = new(["Creature:1"], StringComparer.Ordinal);
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AShieldedCreature_TakesOnlyTheShareItsShieldLetsThrough_UntilTheShieldIsSpent()
    {
        var combat = new CombatService();
        combat.EnterAsCharacter(classId: 1, level: 10, playerId: "player:char-a");
        combat.ShieldUnit("Creature:1", percent: 50, amount: 30);

        var halved = combat.DamageTarget("player:char-a", "Creature:1", 40, Now);
        Assert.Equal(20, halved.Damage);
        Assert.Equal(20, halved.Absorbed);

        var spent = combat.DamageTarget("player:char-a", "Creature:1", 40, Now);
        Assert.Equal(30, spent.Damage);
        Assert.Equal(10, spent.Absorbed);

        var bare = combat.DamageTarget("player:char-a", "Creature:1", 40, Now);
        Assert.Equal(40, bare.Damage);
    }

    [Fact]
    public void Start_MarksNewAttackerInCombat()
    {
        var result = new CombatService().Start("player:char-a", "Creature:1", Visible, Now);

        Assert.NotNull(result);
        Assert.Null(result.StoppedTargetId);
        Assert.True(result.EnteredCombat);
    }

    [Fact]
    public void DamageTarget_TakesItOffThePlayersOwnHealthRatherThanACreaturesDefault()
    {
        var combat = new CombatService();
        combat.EnterAsCharacter(classId: 1, level: 60, playerId: "player:char-a");
        int full = combat.PlayerHealth;
        Assert.True(full > 3000, $"a level 60 warrior should have thousands of health, not {full}");

        var swing = combat.DamageTarget("Creature:1", "player:char-a", 381, Now);

        Assert.Equal(381, swing.Damage);
        Assert.Equal(full - 381, swing.FinalHealth);
        Assert.Equal(full - 381, combat.PlayerHealth);
        Assert.False(swing.Killed);
    }

    [Fact]
    public void DamageTarget_OnABodyWalkingHome_LandsAsAnEvadeForNothingAndOpensNoFight()
    {
        var combat = new CombatService { Evading = id => id == "Creature:1" };
        combat.EnterAsCharacter(classId: 1, level: 10, playerId: "player:char-a");

        var swing = combat.DamageTarget("player:char-a", "Creature:1", 50, Now);

        Assert.Equal(StrikeResult.Evade, swing.Result);
        Assert.Equal(0, swing.Damage);
        Assert.Equal(swing.MaxHealth, swing.FinalHealth);
        Assert.Null(combat.CurrentTargetOf("player:char-a"));
        Assert.Empty(combat.DrainCombatEntries());
        Assert.Null(combat.Start("player:char-a", "Creature:1", Visible, Now));
    }

    [Fact]
    public void Tick_ASwingAlreadyOnABodyThatTurnsForHome_EvadesAndNothingSwingsBack()
    {
        var evading = false;
        var combat = new CombatService(rollUnit: () => 0.99) { Evading = _ => evading };
        combat.EnterAsCharacter(classId: 1, level: 10, playerId: "player:char-a");
        Assert.NotNull(combat.Start("player:char-a", "Creature:1", Visible, Now));
        evading = true;
        var health = combat.PlayerHealth;

        var swings = combat.Tick(Now + TimeSpan.FromSeconds(4));

        var blow = Assert.Single(swings, s => s.AttackerId == "player:char-a");
        Assert.Equal(StrikeResult.Evade, blow.Result);
        Assert.Equal(0, blow.Damage);
        Assert.DoesNotContain(swings, s => s.AttackerId == "Creature:1");
        Assert.Equal(health, combat.PlayerHealth);
    }

    [Fact]
    public void TakePlayerStruck_AnswersForABlowThatHurt_OnceAndOnlyOnce()
    {
        var combat = new CombatService();
        combat.EnterAsCharacter(classId: 1, level: 60, playerId: "player:char-a");
        combat.AbsorbIncoming = (_, _) => 0;
        Assert.False(combat.TakePlayerStruck());

        combat.DamageTarget("Creature:1", "player:char-a", 0, Now);
        Assert.False(combat.TakePlayerStruck());

        combat.DamageTarget("Creature:1", "player:char-a", 40, Now);
        Assert.True(combat.TakePlayerStruck());
        Assert.False(combat.TakePlayerStruck());

        combat.DamageTarget("player:char-a", "Creature:1", 40, Now);
        Assert.False(combat.TakePlayerStruck());
    }

    [Fact]
    public void DamageTarget_StillKillsThePlayerOnceTheirOwnHealthIsGone()
    {
        var combat = new CombatService();
        combat.EnterAsCharacter(classId: 1, level: 60, playerId: "player:char-a");

        var swing = combat.DamageTarget("Creature:1", "player:char-a", combat.PlayerHealth + 1, Now);

        Assert.Equal(0, swing.FinalHealth);
        Assert.True(swing.Killed);
    }

    [Fact]
    public void SetPlayerWeapon_MakesTheWeaponDecideTheSwingRatherThanAFlatNumber()
    {
        int Swing(int? low, int? high)
        {
            var combat = new CombatService(rollDamage: (_, max) => max, rollUnit: () => 1.0);
            combat.SetPlayerWeapon(low, high);
            combat.Start("player:char-a", "Creature:1", Visible, Now);
            return combat.Tick(Now + CombatService.SwingInterval).Single(s => s.TargetId == "Creature:1").Damage;
        }

        int bareHanded = Swing(null, null);
        int withSword = Swing(252, 319);

        Assert.Equal(CombatService.SwingDamage, bareHanded);
        Assert.Equal(319, withSword);
    }

    [Fact]
    public void SetPlayerOffHandWeapon_SwingsPartWayThroughTheMainHandIntervalForItsOwnDamage()
    {
        var combat = new CombatService(rollDamage: (_, max) => max, rollUnit: () => 1.0);
        combat.SetPlayerWeapon(50, 70);
        combat.SetPlayerOffHandWeapon(3, 4);
        combat.Start("player:char-a", "Creature:1", Visible, Now);

        var main = Assert.Single(combat.Tick(Now), s => s.TargetId == "Creature:1");
        Assert.False(main.OffHand);

        var half = Now + CombatService.SwingInterval * CombatService.OffHandDelayFraction;
        Assert.DoesNotContain(combat.Tick(half - TimeSpan.FromMilliseconds(1)), s => s.TargetId == "Creature:1");
        var off = Assert.Single(combat.Tick(half), s => s.TargetId == "Creature:1");
        Assert.True(off.OffHand);
        Assert.True(off.Damage < main.Damage, $"the off hand's {off.Damage} should be its own weapon's, not the main hand's {main.Damage}");

        Assert.DoesNotContain(combat.Tick(half + TimeSpan.FromMilliseconds(10)), s => s.TargetId == "Creature:1");
    }

    [Fact]
    public void AnEmptyOffHand_NeverSwings()
    {
        var combat = new CombatService(rollDamage: (_, max) => max, rollUnit: () => 1.0);
        combat.SetPlayerWeapon(50, 70);
        combat.Start("player:char-a", "Creature:1", Visible, Now);
        combat.Tick(Now);

        Assert.DoesNotContain(combat.Tick(Now + CombatService.SwingInterval * CombatService.OffHandDelayFraction), s => s.TargetId == "Creature:1");
    }

    [Fact]
    public void AStoppedFight_CancelsTheOffHandSwingItHadComing()
    {
        var combat = new CombatService(rollDamage: (_, max) => max, rollUnit: () => 1.0);
        combat.SetPlayerWeapon(50, 70);
        combat.SetPlayerOffHandWeapon(3, 4);
        combat.Start("player:char-a", "Creature:1", Visible, Now);
        combat.Tick(Now);
        combat.Disengage("player:char-a");

        Assert.DoesNotContain(combat.Tick(Now + CombatService.SwingInterval * CombatService.OffHandDelayFraction), s => s.TargetId == "Creature:1");
    }

    [Fact]
    public void EnterAsCharacter_BringsBackTheHealthTheCharacterWentOutWith()
    {
        var combat = new CombatService();
        combat.EnterAsCharacter(classId: 1, level: 60, playerId: "player:char-a", health: 900);

        Assert.Equal(900, combat.PlayerHealth);

        var fresh = new CombatService();
        fresh.EnterAsCharacter(classId: 1, level: 60, playerId: "player:char-a");
        Assert.True(fresh.PlayerHealth > 3000);

        var capped = new CombatService();
        capped.EnterAsCharacter(classId: 1, level: 1, playerId: "player:char-a", health: 99999);
        Assert.Equal(capped.EffectiveMaxHealth(0), capped.PlayerHealth);
    }

    [Fact]
    public void Start_RejectsInvisibleTarget()
    {
        Assert.Null(new CombatService().Start("player:char-a", "Creature:9", Visible, Now));
    }

    private static readonly HashSet<string> VisiblePlayer = new(["player:char-b"], StringComparer.Ordinal);

    [Fact]
    public void Start_RejectsAPlayerTargetWithNoPvpFlagPredicateAtAll()
    {
        Assert.Null(new CombatService().Start("player:char-a", "player:char-b", VisiblePlayer, Now));
    }

    [Fact]
    public void Start_RejectsAPlayerTargetWhenPvpFlagPredicateReturnsFalse()
    {
        var result = new CombatService().Start("player:char-a", "player:char-b", VisiblePlayer, Now,
            isPvpFlagged: (_, _) => false);

        Assert.Null(result);
    }

    [Fact]
    public void Start_AllowsAPlayerTargetWhenPvpFlagPredicateReturnsTrue()
    {
        var result = new CombatService().Start("player:char-a", "player:char-b", VisiblePlayer, Now,
            isPvpFlagged: (_, _) => true);

        Assert.NotNull(result);
        Assert.True(result.EnteredCombat);
    }

    [Fact]
    public void Start_AllowsAPlayerTargetWhenEitherDuelOrPvpFlagPredicateReturnsTrue()
    {
        var result = new CombatService().Start("player:char-a", "player:char-b", VisiblePlayer, Now,
            isDuelOpponent: (_, _) => false, isPvpFlagged: (_, _) => true);

        Assert.NotNull(result);
    }

    [Fact]
    public void Start_RepeatingCurrentTargetChangesNothing()
    {
        var combat = new CombatService();
        combat.Start("player:char-a", "Creature:1", Visible, Now);

        Assert.Null(combat.Start("player:char-a", "Creature:1", Visible, Now));
    }

    [Fact]
    public void Start_SwitchingTargetsStopsTheOldOne()
    {
        var combat = new CombatService();
        combat.Start("player:char-a", "Creature:1", Visible, Now);

        var result = combat.Start("player:char-a", "Creature:2",
            new HashSet<string>(["Creature:1", "Creature:2"], StringComparer.Ordinal), Now);

        Assert.NotNull(result);
        Assert.Equal("Creature:1", result.StoppedTargetId);
        Assert.False(result.EnteredCombat);
    }

    [Fact]
    public void Tick_SwingsImmediatelyOnStartingAFightThenSettlesIntoTheInterval()
    {
        var combat = new CombatService();
        combat.Start("player:char-a", "Creature:1", Visible, Now);

        var opener = combat.Tick(Now);
        Assert.Single(opener);
        Assert.Equal("player:char-a", opener[0].AttackerId);

        Assert.Empty(combat.Tick(Now + CombatService.SwingInterval - TimeSpan.FromMilliseconds(1)));
        Assert.Equal(2, combat.Tick(Now + CombatService.SwingInterval).Count);
    }

    [Fact]
    public void Tick_DamagesUntilDeathThenStops()
    {
        var combat = new CombatService();
        combat.Start("player:char-a", "Creature:1", Visible, Now);

        CombatSwing? last = null;
        for (var swing = 0; swing < 20; swing++)
            foreach (var hit in combat.Tick(Now + CombatService.SwingInterval * (swing + 1)))
                last = hit;

        Assert.NotNull(last);
        Assert.True(last.Killed);
        Assert.Equal(0, last.FinalHealth);
        Assert.True(combat.IsDead("Creature:1"));
        Assert.Empty(combat.Tick(Now + CombatService.SwingInterval * 21));
    }

    [Fact]
    public void Start_RejectsDeadTarget()
    {
        var combat = new CombatService();
        combat.Start("player:char-a", "Creature:1", Visible, Now);
        for (var swing = 0; swing < 20; swing++)
            combat.Tick(Now + CombatService.SwingInterval * (swing + 1));

        Assert.Null(combat.Start("player:char-a", "Creature:1", Visible, Now + CombatService.SwingInterval * 21));
    }

    [Fact]
    public void Tick_VictimSwingsBackAtItsAttacker()
    {
        var combat = new CombatService();
        combat.Start("player:char-a", "Creature:1", Visible, Now);

        var swings = combat.Tick(Now + CombatService.SwingInterval);

        Assert.Equal(2, swings.Count);
        var retaliation = swings.Single(swing => swing.TargetId == "player:char-a");
        Assert.Equal("Creature:1", retaliation.AttackerId);
        Assert.Equal(CombatService.RetaliationDamage, retaliation.Damage);
        Assert.False(retaliation.Killed);
    }

    [Fact]
    public void Tick_AStunnedCreatureHoldsItsBlowUntilItCanActAgain()
    {
        var stunnedUntil = Now + CombatService.SwingInterval * 3;
        var combat = new CombatService { CannotAct = (unitId, at) => unitId == "Creature:1" && at < stunnedUntil };
        combat.Start("player:char-a", "Creature:1", Visible, Now);

        var whileStunned = combat.Tick(Now + CombatService.SwingInterval);
        var afterwards = combat.Tick(stunnedUntil);

        Assert.DoesNotContain(whileStunned, swing => swing.AttackerId == "Creature:1");
        Assert.Contains(whileStunned, swing => swing.AttackerId == "player:char-a");
        Assert.Contains(afterwards, swing => swing.AttackerId == "Creature:1");
    }

    [Fact]
    public void Tick_SwitchingTargetsLeavesTheOldTargetFighting()
    {
        var combat = new CombatService(rollUnit: () => 1.0);
        combat.EnterAsCharacter(classId: 1, level: 60, playerId: "player:char-a");
        var both = new HashSet<string>(["Creature:1", "Creature:2"], StringComparer.Ordinal);
        combat.Start("player:char-a", "Creature:1", both, Now);
        combat.Start("player:char-a", "Creature:2", both, Now);
        Assert.Equal("player:char-a", combat.RetaliationTarget("Creature:1"));

        var swings = combat.Tick(Now + CombatService.SwingInterval);

        Assert.Contains(swings, swing => swing.AttackerId == "Creature:1" && swing.TargetId == "player:char-a");
        Assert.Contains(swings, swing => swing.AttackerId == "Creature:2");
    }

    [Fact]
    public void TickRegen_StaysSilentWhileFightingOrFull()
    {
        var combat = new CombatService();

        Assert.Null(combat.TickRegen(Now));
        combat.Start("player:char-a", "Creature:1", Visible, Now);
        Assert.Null(combat.TickRegen(Now + CombatService.SwingInterval));
    }

    [Fact]
    public void TickRegen_HealsAfterTheFightOnItsOwnSlowerClock()
    {
        var combat = new CombatService(rollUnit: () => 1.0);
        combat.Start("player:char-a", "Creature:1", Visible, Now);
        var at = Now;
        for (var swing = 0; swing < 9; swing++)
        {
            at += CombatService.SwingInterval;
            combat.Tick(at);
        }

        Assert.Null(combat.TickRegen(at));
        Assert.Null(combat.TickRegen(at + CombatService.SwingInterval));
        var first = combat.TickRegen(at + CombatService.RegenInterval);
        Assert.NotNull(first);
        Assert.Equal(62, first.FinalHealth);
        var healed = first;
        for (var step = 0; step < 30; step++)
        {
            at += CombatService.RegenInterval;
            healed = combat.TickRegen(at) ?? healed;
        }

        Assert.Equal(CombatService.PlayerMaxHealth, healed.FinalHealth);
        Assert.Null(combat.TickRegen(at + CombatService.RegenInterval));
    }

    [Fact]
    public void Tick_AnAttackerThatSparesThePlayer_LeavesThemOnOneHealthRatherThanDead()
    {
        var combat = new CombatService { SparedBy = attacker => attacker == "Creature:1" };
        combat.EnterAsCharacter(classId: 1, level: 1, health: 2);
        var visible = new HashSet<string>(["Creature:1"], StringComparer.Ordinal);
        var at = Now;
        combat.Start("player:char-a", "Creature:1", visible, at);
        CombatSwing? last = null;
        for (var swing = 0; swing < 200 && last?.Spared != true; swing++)
        {
            at += CombatService.SwingInterval;
            last = combat.Tick(at).LastOrDefault(one => one.TargetId == "player:char-a") ?? last;
        }

        Assert.True(last?.Spared);
        Assert.False(last!.Killed);
        Assert.Equal(1, last.FinalHealth);
        Assert.False(combat.IsPlayerDead);
    }

    [Fact]
    public void Tick_ACreaturesBlowOnAShieldedPlayer_TakesOnlyWhatTheShieldLetsThrough()
    {
        static CombatSwing FirstBlowBack(Func<int, DateTimeOffset, int>? absorb)
        {
            var combat = new CombatService(rollDamage: (min, _) => min, rollUnit: () => 1.0) { AbsorbIncoming = absorb };
            combat.EnterAsCharacter(classId: 1, level: 60, playerId: "player:char-a");
            var visible = new HashSet<string>(["Creature:1"], StringComparer.Ordinal);
            combat.Start("player:char-a", "Creature:1", visible, Now);
            for (var at = Now; at < Now + TimeSpan.FromMinutes(1); at += CombatService.SwingInterval)
                if (combat.Tick(at).FirstOrDefault(one => one.TargetId == "player:char-a") is { } back) return back;
            throw new InvalidOperationException("the creature never swung back");
        }

        var bare = FirstBlowBack(null);
        var shielded = FirstBlowBack((damage, _) => Math.Max(0, damage - 3));
        Assert.True(bare.Damage > 3);
        Assert.Equal(bare.Damage - 3, shielded.Damage);
        Assert.Equal(bare.FinalHealth + 3, shielded.FinalHealth);
        Assert.Equal(0, bare.Absorbed);
        Assert.Equal(3, shielded.Absorbed);

        var swallowed = FirstBlowBack((_, _) => 0);
        Assert.Equal(0, swallowed.Damage);
        Assert.Equal(bare.Damage, swallowed.Absorbed);
    }

    [Fact]
    public void ABlowFromAnotherPlayer_OnAShieldedPlayer_TakesOnlyWhatTheShieldLetsThrough()
    {
        var combat = new CombatService { AbsorbIncoming = (damage, _) => damage / 2 };
        combat.EnterAsCharacter(classId: 1, level: 60, playerId: "player:char-a", health: 30);

        var hit = combat.DamageTarget("player:char-b", "player:char-a", 12, Now);
        Assert.Equal(6, hit.Damage);
        Assert.Equal(6, hit.Absorbed);
        Assert.Equal(24, combat.PlayerHealth);
    }

    [Fact]
    public void ABlowOnAnotherPlayer_TouchesNoHealthHere_AndNothingSwingsBack()
    {
        var combat = new CombatService(rollUnit: () => 1.0);
        combat.EnterAsCharacter(classId: 1, level: 1, playerId: "player:char-a");
        var before = combat.PlayerHealth;
        var visible = new HashSet<string>(["player:char-b"], StringComparer.Ordinal);
        combat.Start("player:char-a", "player:char-b", visible, Now, isDuelOpponent: (_, _) => true);

        var swings = new List<CombatSwing>();
        for (var at = Now; at < Now + CombatService.SwingInterval * 20; at += CombatService.SwingInterval)
            swings.AddRange(combat.Tick(at));

        Assert.NotEmpty(swings);
        Assert.All(swings, swing =>
        {
            Assert.Equal("player:char-a", swing.AttackerId);
            Assert.True(swing.OtherPlayer);
            Assert.False(swing.Killed);
        });
        Assert.Equal(before, combat.PlayerHealth);
        Assert.False(combat.IsPlayerDead);
        Assert.Equal("player:char-b", combat.CurrentTargetOf("player:char-a"));
    }

    [Fact]
    public void ABlowFromAnotherPlayer_ComesOffThisPlayer_WithoutStartingAFightOnTheirBehalf()
    {
        var combat = new CombatService { SparedBy = attacker => attacker == "player:char-b" };
        combat.EnterAsCharacter(classId: 1, level: 60, playerId: "player:char-a", health: 30);

        var hit = combat.DamageTarget("player:char-b", "player:char-a", 12, Now);
        Assert.Equal(18, hit.FinalHealth);
        Assert.Equal(18, combat.PlayerHealth);
        Assert.Null(combat.CurrentTargetOf("player:char-b"));
        Assert.Empty(combat.Tick(Now + CombatService.SwingInterval));

        var last = combat.DamageTarget("player:char-b", "player:char-a", 500, Now);
        Assert.True(last.Spared);
        Assert.False(last.Killed);
        Assert.Equal(1, combat.PlayerHealth);
        Assert.False(combat.IsPlayerDead);
    }

    [Fact]
    public void Tick_SustainedFightsKillThePlayerAndBlockNewAttacks()
    {
        var combat = new CombatService();
        var visible = new HashSet<string>(["Creature:1", "Creature:2", "Creature:3"], StringComparer.Ordinal);
        var at = Now;
        foreach (var target in visible)
        {
            combat.Start("player:char-a", target, visible, at);
            for (var swing = 0; swing < 10 && !combat.IsPlayerDead; swing++)
            {
                at += CombatService.SwingInterval;
                combat.Tick(at);
            }

            if (combat.IsPlayerDead) break;
        }

        Assert.True(combat.IsPlayerDead);
        Assert.Null(combat.Start("player:char-a", "Creature:3", visible, at));
        Assert.Empty(combat.Tick(at + CombatService.SwingInterval));
    }

    [Fact]
    public void HealPlayer_RestoresHealthCappedAtMax()
    {
        var combat = new CombatService();
        combat.Start("player:char-a", "Creature:1", Visible, Now);
        combat.Tick(Now + CombatService.SwingInterval);

        var healed = combat.HealPlayer(3);

        Assert.NotNull(healed);
        Assert.Equal(3, healed.Applied);
        Assert.Equal(CombatService.PlayerMaxHealth - 2, healed.FinalHealth);

        var overhealed = combat.HealPlayer(1000);
        Assert.Equal(2, overhealed!.Applied);
        Assert.Equal(CombatService.PlayerMaxHealth, overhealed.FinalHealth);
    }

    [Fact]
    public void HealPlayer_ReturnsNullForADeadPlayer()
    {
        var combat = new CombatService();
        var visible = new HashSet<string>(["Creature:1", "Creature:2", "Creature:3"], StringComparer.Ordinal);
        var at = Now;
        foreach (var target in visible)
        {
            combat.Start("player:char-a", target, visible, at);
            for (var swing = 0; swing < 10 && !combat.IsPlayerDead; swing++)
            {
                at += CombatService.SwingInterval;
                combat.Tick(at);
            }

            if (combat.IsPlayerDead) break;
        }

        Assert.True(combat.IsPlayerDead);
        Assert.Null(combat.HealPlayer(50));
    }

    [Fact]
    public void Revive_ReturnsNullForAPlayerWhoIsNotActuallyDead()
    {
        var combat = new CombatService();

        Assert.Null(combat.Revive());
    }

    [Fact]
    public void Revive_FullyHealsADeadPlayerAndClearsTheDeadFlag()
    {
        var combat = new CombatService();
        var visible = new HashSet<string>(["Creature:1", "Creature:2", "Creature:3"], StringComparer.Ordinal);
        var at = Now;
        foreach (var target in visible)
        {
            combat.Start("player:char-a", target, visible, at);
            for (var swing = 0; swing < 10 && !combat.IsPlayerDead; swing++)
            {
                at += CombatService.SwingInterval;
                combat.Tick(at);
            }

            if (combat.IsPlayerDead) break;
        }

        Assert.True(combat.IsPlayerDead);

        var revived = combat.Revive();

        Assert.NotNull(revived);
        Assert.Equal(CombatService.PlayerMaxHealth, revived.FinalHealth);
        Assert.Equal(CombatService.PlayerMaxHealth, revived.Applied);
        Assert.False(combat.IsPlayerDead);
        Assert.NotNull(combat.HealPlayer(1));
        var freshTarget = new HashSet<string>(["Creature:99"], StringComparer.Ordinal);
        Assert.NotNull(combat.Start("player:char-a", "Creature:99", freshTarget, at));
    }

    [Fact]
    public void Revive_HonoursAnActiveStaminaBonusTheSameWayHealPlayerAlreadyDoes()
    {
        var combat = new CombatService();
        var visible = new HashSet<string>(["Creature:1", "Creature:2", "Creature:3"], StringComparer.Ordinal);
        var at = Now;
        foreach (var target in visible)
        {
            combat.Start("player:char-a", target, visible, at);
            for (var swing = 0; swing < 10 && !combat.IsPlayerDead; swing++)
            {
                at += CombatService.SwingInterval;
                combat.Tick(at);
            }

            if (combat.IsPlayerDead) break;
        }

        var revived = combat.Revive(staminaBonus: 50);

        Assert.Equal(combat.EffectiveMaxHealth(50), revived!.FinalHealth);
    }

    [Fact]
    public void Disengage_DropsThePostureAndNamesTheAbandonedTarget()
    {
        var combat = new CombatService();
        combat.Start("player:char-a", "Creature:1", Visible, Now);

        Assert.Equal("Creature:1", combat.Disengage("player:char-a"));
        Assert.Empty(combat.Tick(Now + TimeSpan.FromSeconds(4)));
    }

    [Fact]
    public void Disengage_SaysNothingWhenThereWasNoFight()
    {
        Assert.Null(new CombatService().Disengage("player:char-a"));
    }

    [Fact]
    public void CurrentTargetOf_NamesWhoIsCurrentlyBeingFought()
    {
        var combat = new CombatService();
        combat.Start("player:char-a", "Creature:1", Visible, Now);

        Assert.Equal("Creature:1", combat.CurrentTargetOf("player:char-a"));
    }

    [Fact]
    public void CurrentTargetOf_IsNullWithNoFightGoing()
    {
        Assert.Null(new CombatService().CurrentTargetOf("player:char-a"));
    }

    [Fact]
    public void CurrentTargetOf_IsNullOnceDisengaged()
    {
        var combat = new CombatService();
        combat.Start("player:char-a", "Creature:1", Visible, Now);
        combat.Disengage("player:char-a");

        Assert.Null(combat.CurrentTargetOf("player:char-a"));
    }

    [Fact]
    public void Tick_UsesTheTargetsOwnHealthPoolWhenKnown()
    {
        var combat = new CombatService(rollUnit: () => 1.0);
        var stats = new CreatureCombatStats(30, 5, 5, CombatService.SwingInterval);
        combat.Start("player:char-a", "Creature:1", Visible, Now, stats);

        var first = combat.Tick(Now + CombatService.SwingInterval)
            .Single(s => s.TargetId == "Creature:1");
        Assert.Equal(30, first.MaxHealth);
        Assert.Equal(18, first.FinalHealth);
        var second = combat.Tick(Now + CombatService.SwingInterval * 2)
            .Single(s => s.TargetId == "Creature:1");
        Assert.Equal(6, second.FinalHealth);
        var third = combat.Tick(Now + CombatService.SwingInterval * 3)
            .Single(s => s.TargetId == "Creature:1");
        Assert.Equal(0, third.FinalHealth);
        Assert.True(third.Killed);
    }

    [Fact]
    public void Tick_RollsRetaliationDamageWithinTheTargetsOwnRange()
    {
        var combat = new CombatService(rollDamage: (min, max) => max);
        var stats = new CreatureCombatStats(CombatService.CreatureMaxHealth, 3, 9, CombatService.SwingInterval);
        combat.Start("player:char-a", "Creature:1", Visible, Now, stats);

        var swing = combat.Tick(Now + CombatService.SwingInterval).Last(s => s.AttackerId == "Creature:1");

        Assert.Equal(9, swing.Damage);
    }

    [Fact]
    public void Tick_NeverSchedulesARetaliationSwingForATrainingDummy()
    {
        var combat = new CombatService(rollDamage: (min, max) => max);
        var stats = new CreatureCombatStats(
            CombatService.CreatureMaxHealth, 9, 9, CombatService.SwingInterval, IsTrainingDummy: true);
        combat.Start("player:char-a", "Creature:1", Visible, Now, stats);

        var swings = combat.Tick(Now + CombatService.SwingInterval);

        Assert.DoesNotContain(swings, s => s.AttackerId == "Creature:1");
        Assert.Contains(swings, s => s.AttackerId == "player:char-a" && s.TargetId == "Creature:1");
    }

    [Fact]
    public void Tick_NeverSchedulesRetaliationForATrainingDummyReachedBySwitchingTargets()
    {
        var combat = new CombatService(rollDamage: (min, max) => max);
        var everyone = new HashSet<string>(["Creature:1", "Creature:2"], StringComparer.Ordinal);
        combat.Start("player:char-a", "Creature:1", everyone, Now);
        var dummyStats = new CreatureCombatStats(
            CombatService.CreatureMaxHealth, 9, 9, CombatService.SwingInterval, IsTrainingDummy: true);

        combat.Start("player:char-a", "Creature:2", everyone, Now, dummyStats);
        var swings = combat.Tick(Now + CombatService.SwingInterval);

        Assert.DoesNotContain(swings, s => s.AttackerId == "Creature:2");
    }

    [Fact]
    public void RollAmount_UsesTheSameInjectableRollAsMeleeDamage()
    {
        var combat = new CombatService(rollDamage: (min, max) => max);

        Assert.Equal(30, combat.RollAmount(20, 30));
    }

    [Fact]
    public void RollAmount_ReturnsTheValueAsIsForADegenerateRange()
    {
        var combat = new CombatService(rollDamage: (_, _) => throw new InvalidOperationException());

        Assert.Equal(20, combat.RollAmount(20, 20));
    }

    [Fact]
    public void Tick_UsesTheTargetsOwnSwingIntervalForRetaliation()
    {
        var combat = new CombatService();
        var fastInterval = TimeSpan.FromMilliseconds(500);
        var stats = new CreatureCombatStats(CombatService.CreatureMaxHealth, 5, 5, fastInterval);
        combat.Start("player:char-a", "Creature:1", Visible, Now, stats);

        var swings = combat.Tick(Now + TimeSpan.FromMilliseconds(600));

        Assert.Contains(swings, s => s.AttackerId == "Creature:1");
    }

    [Fact]
    public void Start_FallsBackToSyntheticNumbersWhenTheTargetHasNoKnownStats()
    {
        var combat = new CombatService(rollUnit: () => 1.0);
        combat.Start("player:char-a", "Creature:1", Visible, Now);

        var swing = combat.Tick(Now + CombatService.SwingInterval).Single(s => s.TargetId == "Creature:1");

        Assert.Equal(CombatService.CreatureMaxHealth, swing.MaxHealth);
        Assert.Equal(CombatService.CreatureMaxHealth - CombatService.SwingDamage, swing.FinalHealth);
    }

    [Fact]
    public void DamageTarget_AppliesDamageFromAnySource()
    {
        var combat = new CombatService();

        var swing = combat.DamageTarget("player:char-a", "Creature:1", 20, Now);

        Assert.Equal("player:char-a", swing.AttackerId);
        Assert.Equal("Creature:1", swing.TargetId);
        Assert.Equal(20, swing.Damage);
        Assert.Equal(CombatService.CreatureMaxHealth - 20, swing.FinalHealth);
        Assert.False(swing.Killed);
    }

    [Fact]
    public void DamageTarget_KillsAndClearsThatTargetsOwnRetaliation()
    {
        var combat = new CombatService();
        combat.Start("player:char-a", "Creature:1", Visible, Now);

        var swing = combat.DamageTarget("player:char-a", "Creature:1", CombatService.CreatureMaxHealth, Now);

        Assert.True(swing.Killed);
        Assert.True(combat.IsDead("Creature:1"));
        Assert.Null(combat.Disengage("player:char-a"));
    }

    [Fact]
    public void DamageTarget_NeverReportsASecondKillOnABodyAlreadyDown()
    {
        var combat = new CombatService();
        combat.Start("player:char-a", "Creature:1", Visible, Now);
        Assert.True(combat.DamageTarget("player:char-a", "Creature:1", CombatService.CreatureMaxHealth, Now).Killed);

        var again = combat.DamageTarget("player:char-a", "Creature:1", 40, Now);

        Assert.False(again.Killed);
        Assert.Equal(0, again.Damage);
        Assert.Equal(0, again.FinalHealth);
    }

    [Fact]
    public void DamageTarget_DoesNotOpenAFightWithABodyAlreadyDown()
    {
        var combat = new CombatService();
        combat.Start("player:char-a", "Creature:1", Visible, Now);
        combat.DamageTarget("player:char-a", "Creature:1", CombatService.CreatureMaxHealth, Now);

        combat.DamageTarget("player:char-a", "Creature:1", 40, Now);

        Assert.Null(combat.CurrentTargetOf("player:char-a"));
    }

    [Fact]
    public void Tick_ReportsThePlayersOwnDeathExactlyOnce()
    {
        var combat = new CombatService(rollUnit: () => 1.0);
        var lethal = new CreatureCombatStats(
            CombatService.CreatureMaxHealth, 1_000_000, 1_000_000, CombatService.SwingInterval);
        combat.Start("player:char-a", "Creature:1", Visible, Now, lethal);

        var kills = 0;
        var at = Now;
        for (var tick = 0; tick < 50; tick++)
        {
            at += CombatService.SwingInterval;
            kills += combat.Tick(at).Count(swing => swing.TargetId == "player:char-a" && swing.Killed);
        }

        Assert.Equal(1, kills);
        Assert.True(combat.IsPlayerDead);
    }

    [Fact]
    public void DamageTarget_LeavesAnUnrelatedMeleeTargetAlone()
    {
        var combat = new CombatService();
        var visible = new HashSet<string>(["Creature:1", "Creature:2"], StringComparer.Ordinal);
        combat.Start("player:char-a", "Creature:1", visible, Now);

        combat.DamageTarget("player:char-a", "Creature:2", CombatService.CreatureMaxHealth, Now);

        Assert.Equal("Creature:1", combat.Disengage("player:char-a"));
    }

    [Fact]
    public void Tick_ArmorReducesRetaliationDamageTheAttackerActuallyTakes()
    {
        var combat = new CombatService();
        combat.Start("player:char-a", "Creature:1", Visible, Now);

        var swings = combat.Tick(Now + CombatService.SwingInterval, playerArmor: 600);

        var retaliation = swings.Single(swing => swing.TargetId == "player:char-a");
        Assert.Equal(
            CombatService.ApplyArmorMitigation(CombatService.RetaliationDamage, 600),
            retaliation.Damage);
        Assert.True(retaliation.Damage < CombatService.RetaliationDamage);
    }

    [Fact]
    public void ApplyArmorMitigation_ReducesDamageByAFlatPercentPerPointCappedAtMax()
    {
        Assert.Equal(90, CombatService.ApplyArmorMitigation(100, armor: 100));

        int atCap = (int)Math.Round(1000 * (1.0 - CombatService.MaxArmorMitigation));
        Assert.Equal(atCap, CombatService.ApplyArmorMitigation(1000, armor: 100_000));
    }

    [Fact]
    public void ApplyArmorMitigation_NeverReducesAPositiveHitToZero()
    {
        Assert.Equal(1, CombatService.ApplyArmorMitigation(1, armor: 100_000));
    }

    [Fact]
    public void ApplyArmorMitigation_NegativeArmorIncreasesDamageInstead()
    {
        Assert.True(CombatService.ApplyArmorMitigation(100, armor: -100) > 100);
    }

    [Fact]
    public void ApplyArmorMitigation_ZeroArmorLeavesDamageUnchanged()
    {
        Assert.Equal(50, CombatService.ApplyArmorMitigation(50, armor: 0));
    }

    [Fact]
    public void Tick_StrengthIncreasesThePlayersOwnOutgoingMeleeDamage()
    {
        var combat = new CombatService(rollUnit: () => 1.0);
        combat.Start("player:char-a", "Creature:1", Visible, Now);

        var swings = combat.Tick(Now + CombatService.SwingInterval, playerStrength: 100);

        var meleeHit = swings.Single(swing => swing.AttackerId == "player:char-a");
        Assert.Equal(
            CombatService.ApplyStrengthBonus(CombatService.SwingDamage, 100),
            meleeHit.Damage);
        Assert.True(meleeHit.Damage > CombatService.SwingDamage);
    }

    [Fact]
    public void Tick_AttackPowerIsZeroAndAddsNothingBeforeACharacterIsChosen()
    {
        var combat = new CombatService(rollUnit: () => 1.0);
        combat.Start("player:char-a", "Creature:1", Visible, Now);

        var swings = combat.Tick(Now + CombatService.SwingInterval, playerStrength: 100, playerAgility: 100);

        var meleeHit = swings.Single(swing => swing.AttackerId == "player:char-a");
        Assert.Equal(CombatService.ApplyStrengthBonus(CombatService.SwingDamage, 100), meleeHit.Damage);
    }

    [Fact]
    public void Tick_AttackPowerFromAChosenCharacterAddsOnTopOfStrengthsOwnBonus()
    {
        var combat = new CombatService(rollUnit: () => 1.0);
        combat.EnterAsCharacter(classId: 3, level: 4);
        combat.Start("player:char-a", "Creature:1", Visible, Now);

        var swings = combat.Tick(Now + CombatService.SwingInterval, playerStrength: 100, playerAgility: 50);

        var meleeHit = swings.Single(swing => swing.AttackerId == "player:char-a");
        var attackPower = PlayerStatCurves.AttackPower(classId: 3, level: 4, strength: 100, agility: 50);
        var expectedDamage = CombatService.ApplyAttackPowerBonus(
            CombatService.ApplyStrengthBonus(CombatService.SwingDamage, 100), attackPower);
        Assert.Equal(expectedDamage, meleeHit.Damage);
        Assert.True(meleeHit.Damage > CombatService.ApplyStrengthBonus(CombatService.SwingDamage, 100));
    }

    [Fact]
    public void Tick_LeavesRetaliationDamageUnaffectedByThePlayersOwnStrength()
    {
        var combat = new CombatService();
        combat.Start("player:char-a", "Creature:1", Visible, Now);

        var swings = combat.Tick(Now + CombatService.SwingInterval, playerStrength: 100);

        var retaliation = swings.Single(swing => swing.TargetId == "player:char-a");
        Assert.Equal(CombatService.RetaliationDamage, retaliation.Damage);
    }

    [Fact]
    public void ApplyStrengthBonus_AddsAFlatAmountPerPoint()
    {
        Assert.Equal(22, CombatService.ApplyStrengthBonus(12, strength: 100));
    }

    [Fact]
    public void ApplyStrengthBonus_NeverReducesDamageToZeroOrBelow()
    {
        Assert.Equal(1, CombatService.ApplyStrengthBonus(1, strength: -100_000));
    }

    [Fact]
    public void ApplyStrengthBonus_ZeroStrengthLeavesDamageUnchanged()
    {
        Assert.Equal(12, CombatService.ApplyStrengthBonus(12, strength: 0));
    }

    [Fact]
    public void ApplyHealingPowerBonus_AddsAFlatAmountPerPoint()
    {
        Assert.Equal(62, CombatService.ApplyHealingPowerBonus(12, healingPower: 100));
    }

    [Fact]
    public void ApplyHealingPowerBonus_FloorsAtZeroRatherThanOneUnlikeAMeleeSwing()
    {
        Assert.Equal(0, CombatService.ApplyHealingPowerBonus(1, healingPower: -100_000));
    }

    [Fact]
    public void ApplyHealingPowerBonus_ZeroHealingPowerLeavesTheHealUnchanged()
    {
        Assert.Equal(12, CombatService.ApplyHealingPowerBonus(12, healingPower: 0));
    }

    [Fact]
    public void ApplySpellPowerBonus_AddsAFlatAmountPerPoint()
    {
        Assert.Equal(22, CombatService.ApplySpellPowerBonus(12, spellPower: 100));
    }

    [Fact]
    public void ApplySpellPowerBonus_NeverReducesDamageToZeroOrBelow()
    {
        Assert.Equal(1, CombatService.ApplySpellPowerBonus(1, spellPower: -100_000));
    }

    [Fact]
    public void ApplySpellPowerBonus_ZeroSpellPowerLeavesDamageUnchanged()
    {
        Assert.Equal(12, CombatService.ApplySpellPowerBonus(12, spellPower: 0));
    }

    [Fact]
    public void ApplyAttackPowerBonus_AddsAFlatAmountPerPoint()
    {
        Assert.Equal(27, CombatService.ApplyAttackPowerBonus(22, attackPower: 100));
    }

    [Fact]
    public void ApplyAttackPowerBonus_ZeroAttackPowerLeavesDamageUnchanged()
    {
        Assert.Equal(22, CombatService.ApplyAttackPowerBonus(22, attackPower: 0));
    }

    [Fact]
    public void HealPlayer_CapsAtTheStaminaBoostedMaxInsteadOfTheBaseOne()
    {
        var combat = new CombatService();

        var healed = combat.HealPlayer(1000, staminaBonus: 50);

        Assert.Equal(combat.EffectiveMaxHealth(50), healed!.FinalHealth);
        Assert.True(healed.FinalHealth > CombatService.PlayerMaxHealth);
    }

    [Fact]
    public void TickRegen_HealsTowardTheStaminaBoostedMax()
    {
        var combat = new CombatService();
        combat.Start("player:char-a", "Creature:1", Visible, Now);
        combat.Tick(Now + CombatService.SwingInterval);
        combat.Disengage("player:char-a");

        var regen = combat.TickRegen(Now + CombatService.RegenInterval, staminaBonus: 50);

        Assert.NotNull(regen);
        Assert.Equal(combat.EffectiveMaxHealth(50), regen.MaxHealth);
    }

    [Fact]
    public void TickRegen_HealthRegenRatingIncreasesTheAmountRestored()
    {
        var withoutRating = new CombatService();
        withoutRating.Start("player:char-a", "Creature:1", Visible, Now);
        withoutRating.Tick(Now + CombatService.SwingInterval);
        withoutRating.Disengage("player:char-a");
        var baseline = withoutRating.TickRegen(Now + CombatService.RegenInterval, staminaBonus: 50);

        var withRating = new CombatService();
        withRating.Start("player:char-a", "Creature:1", Visible, Now);
        withRating.Tick(Now + CombatService.SwingInterval);
        withRating.Disengage("player:char-a");
        var boosted = withRating.TickRegen(Now + CombatService.RegenInterval, staminaBonus: 50, healthRegenRating: 10);

        Assert.NotNull(baseline);
        Assert.NotNull(boosted);
        Assert.Equal(
            baseline.FinalHealth + 10 * CombatService.HealthRegenPerRating,
            boosted.FinalHealth);
    }

    [Fact]
    public void TickRegen_UsesTheEnteredCharactersOwnClassBase()
    {
        var classOne = new CombatService();
        classOne.EnterAsCharacter(classId: 1, level: 10, playerId: "player:char-a", health: 1);
        var classTwo = new CombatService();
        classTwo.EnterAsCharacter(classId: 2, level: 10, playerId: "player:char-a", health: 1);

        var oneRegen = classOne.TickRegen(Now + CombatService.RegenInterval);
        var twoRegen = classTwo.TickRegen(Now + CombatService.RegenInterval);

        Assert.NotNull(oneRegen);
        Assert.NotNull(twoRegen);
        Assert.Equal(Math.Min(oneRegen.MaxHealth, 1 + CombatService.RegenAmount(1, classOne.OwnAttributes.Stamina, 0)),
            oneRegen.FinalHealth);
        Assert.Equal(Math.Min(twoRegen.MaxHealth, 1 + CombatService.RegenAmount(2, classTwo.OwnAttributes.Stamina, 0)),
            twoRegen.FinalHealth);
        Assert.NotEqual(oneRegen.FinalHealth, twoRegen.FinalHealth);
    }

    [Fact]
    public void RegenAmount_IsAClassShareOfAllTheCharactersStaminaPlusAClassConstant()
    {
        Assert.Equal(1209, CombatService.RegenAmount(PlayerClassIds.Warrior, 600, 0));
        Assert.Equal(726, CombatService.RegenAmount(PlayerClassIds.Mage, 600, 0));
        Assert.Equal(1082, CombatService.RegenAmount(PlayerClassIds.Ranger, 600, 0));
        Assert.Equal(905, CombatService.RegenAmount(PlayerClassIds.Monk, 600, 0));
        Assert.Equal(1209 + 7 * CombatService.HealthRegenPerRating,
            CombatService.RegenAmount(PlayerClassIds.Warrior, 600, 7));
    }

    [Fact]
    public void TickRegen_UnrecognizedClassFallsBackToThePreCharacterAmount()
    {
        var combat = new CombatService();
        combat.Start("player:char-a", "Creature:1", Visible, Now);
        combat.Tick(Now + CombatService.SwingInterval);
        combat.Disengage("player:char-a");
        var beforeRegen = combat.PlayerHealth;

        var regen = combat.TickRegen(Now + CombatService.RegenInterval);

        Assert.NotNull(regen);
        Assert.Equal(beforeRegen + CombatService.RegenPerInterval, regen.FinalHealth);
    }

    [Fact]
    public void TickRegen_AddsAShareOfTheActiveStaminaBonusOnTopOfTheClassBase()
    {
        var combat = new CombatService();
        combat.EnterAsCharacter(classId: 1, level: 10, playerId: "player:char-a", health: 1);

        var regen = combat.TickRegen(Now + CombatService.RegenInterval, staminaBonus: 50);

        Assert.NotNull(regen);
        Assert.Equal(
            Math.Min(regen.MaxHealth, 1 + CombatService.RegenAmount(1, combat.OwnAttributes.Stamina + 50, 0)),
            regen.FinalHealth);
    }

    [Fact]
    public void TickRegen_StaysSilentWhileInActiveDuelEvenWithNoTargetSet()
    {
        var combat = new CombatService { InActiveDuel = () => true };
        combat.EnterAsCharacter(classId: 1, level: 10, playerId: "player:char-a", health: 1);

        Assert.Null(combat.TickRegen(Now + CombatService.RegenInterval));
    }

    [Fact]
    public void TickRegen_ResumesOnceTheDuelIsNoLongerActive()
    {
        var inDuel = true;
        var combat = new CombatService { InActiveDuel = () => inDuel };
        combat.EnterAsCharacter(classId: 1, level: 10, playerId: "player:char-a", health: 1);
        Assert.Null(combat.TickRegen(Now + CombatService.RegenInterval));

        inDuel = false;
        var regen = combat.TickRegen(Now + CombatService.RegenInterval);

        Assert.NotNull(regen);
    }

    [Fact]
    public void Tick_ReportsTheStaminaBoostedMaxOnARetaliationSwing()
    {
        var combat = new CombatService();
        combat.Start("player:char-a", "Creature:1", Visible, Now);

        var swings = combat.Tick(Now + CombatService.SwingInterval, playerStamina: 50);

        var retaliation = swings.Single(swing => swing.TargetId == "player:char-a");
        Assert.Equal(combat.EffectiveMaxHealth(50), retaliation.MaxHealth);
    }

    [Fact]
    public void EffectiveMaxHealth_AddsTenPerPointOfStamina()
    {
        var combat = new CombatService();

        Assert.Equal(CombatService.PlayerMaxHealth + 500, combat.EffectiveMaxHealth(50));
    }

    [Fact]
    public void EffectiveMaxHealth_NeverDropsToZeroOrBelow()
    {
        Assert.Equal(1, new CombatService().EffectiveMaxHealth(-100_000));
    }

    [Fact]
    public void EffectiveMaxHealth_ZeroStaminaLeavesThePreCharacterFallbackUnchanged()
    {
        Assert.Equal(CombatService.PlayerMaxHealth, new CombatService().EffectiveMaxHealth(0));
    }

    [Theory]
    [InlineData(1, 60, 4461)]
    [InlineData(2, 28, 741)]
    [InlineData(3, 44, 2135)]
    [InlineData(4, 9, 395)]
    public void EffectiveMaxHealth_CountsTheStaminaACharacterGainsByLevelling(int classId, int level, int expected)
    {
        var combat = new CombatService();
        combat.EnterAsCharacter(classId, level,
            levelOneAttributes: StartingLoadouts.For(PlayerRaceIds.Human, classId)!.Attributes);

        Assert.Equal(expected, combat.EffectiveMaxHealth(0));
    }

    [Fact]
    public void EffectiveMaxHealth_UsesThePerClassCurveOnceACharacterIsChosen()
    {
        var combat = new CombatService();
        combat.EnterAsCharacter(classId: 1, level: 1);

        Assert.Equal(11, combat.EffectiveMaxHealth(0));
    }

    [Fact]
    public void EnterAsCharacter_FullyHealsToThePerClassBase()
    {
        var combat = new CombatService();
        combat.Start("player:char-a", "Creature:1", Visible, Now);
        combat.Tick(Now + CombatService.SwingInterval);

        combat.EnterAsCharacter(classId: 2, level: 20);

        Assert.Equal(combat.EffectiveMaxHealth(0), combat.HealPlayer(0)!.FinalHealth);
    }

    [Fact]
    public void Tick_ADodgedRetaliationDealsNoDamageAndReportsTheDodgeResult()
    {
        var combat = new CombatService(rollUnit: () => 0.0);
        combat.Start("player:char-a", "Creature:1", Visible, Now);

        var swings = combat.Tick(Now + CombatService.SwingInterval, playerAgility: 100);

        var retaliation = swings.Single(swing => swing.TargetId == "player:char-a");
        Assert.Equal(0, retaliation.Damage);
        Assert.Equal(StrikeResult.Dodge, retaliation.Result);
        Assert.Equal(combat.EffectiveMaxHealth(0), retaliation.FinalHealth);
    }

    [Fact]
    public void Tick_AFailedDodgeRollLandsTheHitNormally()
    {
        var combat = new CombatService(rollUnit: () => 1.0);
        combat.Start("player:char-a", "Creature:1", Visible, Now);

        var swings = combat.Tick(Now + CombatService.SwingInterval, playerAgility: 100);

        var retaliation = swings.Single(swing => swing.TargetId == "player:char-a");
        Assert.Equal(CombatService.RetaliationDamage, retaliation.Damage);
        Assert.Equal(StrikeResult.Hit, retaliation.Result);
    }

    [Fact]
    public void Tick_ZeroAgilityNeverDodges()
    {
        var combat = new CombatService(rollUnit: () => 0.0);
        combat.Start("player:char-a", "Creature:1", Visible, Now);

        var swings = combat.Tick(Now + CombatService.SwingInterval, playerAgility: 0);

        var retaliation = swings.Single(swing => swing.TargetId == "player:char-a");
        Assert.Equal(StrikeResult.Hit, retaliation.Result);
    }

    [Fact]
    public void Tick_OutgoingMeleeSwingsAreNeverSubjectToTheDefendersDodgeRoll()
    {
        var combat = new CombatService(rollUnit: () => 0.0);
        combat.Start("player:char-a", "Creature:1", Visible, Now);

        var swings = combat.Tick(Now + CombatService.SwingInterval, playerAgility: 100, playerHitRating: 50);

        var outgoing = swings.Single(swing => swing.AttackerId == "player:char-a");
        Assert.Equal(StrikeResult.Hit, outgoing.Result);
        Assert.Equal(CombatService.SwingDamage, outgoing.Damage);
    }

    [Fact]
    public void DodgeChance_IsAFlatAmountPerPointCappedAtMax()
    {
        Assert.Equal(100 * CombatService.DodgeChancePerAgility, CombatService.DodgeChance(100));

        Assert.Equal(CombatService.MaxDodgeChance, CombatService.DodgeChance(100_000));
    }

    [Fact]
    public void DodgeChance_NegativeAgilityNeverGoesBelowZero()
    {
        Assert.Equal(0, CombatService.DodgeChance(-100_000));
    }

    [Fact]
    public void DodgeChance_DodgeRatingAddsOnTopOfAgilityBeforeTheSharedCapApplies()
    {
        Assert.Equal(0.1, CombatService.DodgeChance(50, dodgeRating: 50), 3);
    }

    [Fact]
    public void DodgeChance_DodgeRatingAloneWorksTheSameWayAgilityAloneDoes()
    {
        Assert.Equal(100 * CombatService.DodgeChancePerDodgeRating, CombatService.DodgeChance(0, dodgeRating: 100));
    }

    [Fact]
    public void DodgeChance_NegativeDodgeRatingNeverCancelsAgilitysOwnPositiveContribution()
    {
        Assert.Equal(0.05, CombatService.DodgeChance(50, dodgeRating: -100_000), 3);
    }

    [Fact]
    public void DodgeChance_CombinedAgilityAndDodgeRatingStillClampAtTheSharedMax()
    {
        Assert.Equal(CombatService.MaxDodgeChance, CombatService.DodgeChance(100_000, dodgeRating: 100_000));
    }

    [Fact]
    public void Tick_ACriticalOutgoingSwingDoublesDamageAndReportsTheCriticalResult()
    {
        var combat = new CombatService(rollUnit: () => 0.0);
        combat.Start("player:char-a", "Creature:1", Visible, Now);

        var swings = combat.Tick(Now + CombatService.SwingInterval, playerCritRating: 100, playerHitRating: 50);

        var outgoing = swings.Single(swing => swing.AttackerId == "player:char-a");
        Assert.Equal(
            (int)Math.Round(CombatService.SwingDamage * CombatService.CriticalDamageMultiplier),
            outgoing.Damage);
        Assert.Equal(StrikeResult.Critical, outgoing.Result);
    }

    [Fact]
    public void Tick_AFailedCritRollLandsANormalHit()
    {
        var combat = new CombatService(rollUnit: () => 1.0);
        combat.Start("player:char-a", "Creature:1", Visible, Now);

        var swings = combat.Tick(Now + CombatService.SwingInterval, playerCritRating: 100);

        var outgoing = swings.Single(swing => swing.AttackerId == "player:char-a");
        Assert.Equal(CombatService.SwingDamage, outgoing.Damage);
        Assert.Equal(StrikeResult.Hit, outgoing.Result);
    }

    [Fact]
    public void Tick_ZeroCritRatingNeverCrits()
    {
        var combat = new CombatService(rollUnit: () => 0.0);
        combat.Start("player:char-a", "Creature:1", Visible, Now);

        var swings = combat.Tick(Now + CombatService.SwingInterval, playerCritRating: 0, playerHitRating: 50);

        var outgoing = swings.Single(swing => swing.AttackerId == "player:char-a");
        Assert.Equal(StrikeResult.Hit, outgoing.Result);
    }

    [Fact]
    public void Tick_RetaliationSwingsAreNeverSubjectToTheAttackersCritRoll()
    {
        var combat = new CombatService(rollUnit: () => 0.0);
        combat.Start("player:char-a", "Creature:1", Visible, Now);

        var swings = combat.Tick(Now + CombatService.SwingInterval, playerCritRating: 100);

        var retaliation = swings.Single(swing => swing.TargetId == "player:char-a");
        Assert.Equal(StrikeResult.Hit, retaliation.Result);
        Assert.Equal(CombatService.RetaliationDamage, retaliation.Damage);
    }

    [Fact]
    public void CritChance_IsAFlatAmountPerPointCappedAtMax()
    {
        Assert.Equal(100 * CombatService.CritChancePerRating, CombatService.CritChance(100));

        Assert.Equal(CombatService.MaxCritChance, CombatService.CritChance(100_000));
    }

    [Fact]
    public void CritChance_NegativeRatingNeverGoesBelowZero()
    {
        Assert.Equal(0, CombatService.CritChance(-100_000));
    }

    [Theory]
    [InlineData(0, 2000)]
    [InlineData(100, 1000)]
    [InlineData(-50, 4000)]
    public void EffectiveSwingInterval_DividesByOnePlusPercentOverOneHundred(int percent, int expectedMs)
    {
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), CombatService.EffectiveSwingInterval(percent));
    }

    [Fact]
    public void EffectiveSwingInterval_FloorsWellAboveZeroRatherThanInvertOrDivideByZero()
    {
        Assert.True(CombatService.EffectiveSwingInterval(-100) > TimeSpan.Zero);
        Assert.True(CombatService.EffectiveSwingInterval(-500) > TimeSpan.Zero);
    }

    [Fact]
    public void Tick_AMeleeAttackSpeedBuffShortensTheNextScheduledSwing()
    {
        var combat = new CombatService(rollUnit: () => 1.0);
        combat.Start("player:char-a", "Creature:1", Visible, Now);

        combat.Tick(Now + CombatService.SwingInterval, playerMeleeAttackSpeedPercent: 100);
        var swings = combat.Tick(Now + CombatService.SwingInterval + CombatService.EffectiveSwingInterval(100));

        Assert.Contains(swings, s => s.AttackerId == "player:char-a");
    }

    [Fact]
    public void Tick_AParriedRetaliationDealsNoDamageAndReportsTheParryResult()
    {
        var combat = new CombatService(rollUnit: () => 0.0);
        combat.Start("player:char-a", "Creature:1", Visible, Now);

        var swings = combat.Tick(Now + CombatService.SwingInterval, playerParryRating: 100);

        var retaliation = swings.Single(swing => swing.TargetId == "player:char-a");
        Assert.Equal(0, retaliation.Damage);
        Assert.Equal(StrikeResult.Parry, retaliation.Result);
    }

    [Fact]
    public void Tick_ABlockedRetaliationReducesDamageAndReportsTheBlockResult()
    {
        var combat = new CombatService(rollUnit: () => 0.0);
        combat.Start("player:char-a", "Creature:1", Visible, Now);

        var swings = combat.Tick(Now + CombatService.SwingInterval, playerBlockRating: 100);

        var retaliation = swings.Single(swing => swing.TargetId == "player:char-a");
        Assert.Equal(
            CombatService.ApplyArmorMitigation(
                (int)Math.Round(
                    CombatService.RetaliationDamage * (1 - CombatService.BlockDamageReduction),
                    MidpointRounding.AwayFromZero),
                0),
            retaliation.Damage);
        Assert.True(retaliation.Damage < CombatService.RetaliationDamage);
        Assert.Equal(StrikeResult.Block, retaliation.Result);
    }

    [Fact]
    public void Tick_DodgeTakesPriorityOverParryAndBlockWhenAllThreeWouldSucceed()
    {
        var combat = new CombatService(rollUnit: () => 0.0);
        combat.Start("player:char-a", "Creature:1", Visible, Now);

        var swings = combat.Tick(
            Now + CombatService.SwingInterval, playerAgility: 100, playerParryRating: 100, playerBlockRating: 100);

        var retaliation = swings.Single(swing => swing.TargetId == "player:char-a");
        Assert.Equal(StrikeResult.Dodge, retaliation.Result);
    }

    [Fact]
    public void Tick_ParryTakesPriorityOverBlockWhenBothWouldSucceed()
    {
        var combat = new CombatService(rollUnit: () => 0.0);
        combat.Start("player:char-a", "Creature:1", Visible, Now);

        var swings = combat.Tick(
            Now + CombatService.SwingInterval, playerParryRating: 100, playerBlockRating: 100);

        var retaliation = swings.Single(swing => swing.TargetId == "player:char-a");
        Assert.Equal(StrikeResult.Parry, retaliation.Result);
    }

    [Fact]
    public void Tick_ZeroRatingsNeverParryOrBlock()
    {
        var combat = new CombatService(rollUnit: () => 0.0);
        combat.Start("player:char-a", "Creature:1", Visible, Now);

        var swings = combat.Tick(Now + CombatService.SwingInterval);

        var retaliation = swings.Single(swing => swing.TargetId == "player:char-a");
        Assert.Equal(StrikeResult.Hit, retaliation.Result);
        Assert.Equal(CombatService.RetaliationDamage, retaliation.Damage);
    }

    [Fact]
    public void ParryChance_IsAFlatAmountPerPointCappedAtMax()
    {
        Assert.Equal(100 * CombatService.ParryChancePerRating, CombatService.ParryChance(100));
        Assert.Equal(CombatService.MaxParryChance, CombatService.ParryChance(100_000));
    }

    [Fact]
    public void ParryChance_NegativeRatingNeverGoesBelowZero()
    {
        Assert.Equal(0, CombatService.ParryChance(-100_000));
    }

    [Fact]
    public void BlockChance_IsAFlatAmountPerPointCappedAtMax()
    {
        Assert.Equal(100 * CombatService.BlockChancePerRating, CombatService.BlockChance(100));
        Assert.Equal(CombatService.MaxBlockChance, CombatService.BlockChance(100_000));
    }

    [Fact]
    public void BlockChance_NegativeRatingNeverGoesBelowZero()
    {
        Assert.Equal(0, CombatService.BlockChance(-100_000));
    }

    private static readonly TimeSpan RangedInterval = CombatService.SwingInterval;

    private static RangedAttackContext Context(
        double distance, bool hasRangedWeapon = true, bool isMoving = false) =>
        new(InMeleeRange: distance <= 3.0, Distance: distance,
            HasRangedWeapon: hasRangedWeapon, RangedInterval: RangedInterval, IsMoving: isMoving);

    private static CombatService StartedFight(out string playerId)
    {
        playerId = "player:char-a";
        var combat = new CombatService(rollUnit: () => 1.0);
        combat.Start(playerId, "Creature:1", Visible, Now);
        return combat;
    }

    [Fact]
    public void Start_AlwaysBeginsInMeleeModeWhateverTheWeapon()
    {
        var combat = StartedFight(out var playerId);

        Assert.Equal(CombatService.AttackStance.Melee, combat.StanceOf(playerId));
    }

    [Fact]
    public void StanceOf_IsNoneForSomeoneWithNoFight()
    {
        Assert.Equal(CombatService.AttackStance.None, new CombatService().StanceOf("player:char-a"));
    }

    [Fact]
    public void UpdateStance_ShootsOnceOutOfMeleeReach()
    {
        var combat = StartedFight(out var playerId);

        var change = combat.UpdateStance(playerId, Context(distance: 12), Now);

        Assert.NotNull(change);
        Assert.Equal(CombatService.AttackStance.Melee, change.From);
        Assert.Equal(CombatService.AttackStance.Ranged, change.To);
        Assert.Equal("Creature:1", change.TargetId);
        Assert.Equal(CombatService.AttackStance.Ranged, combat.StanceOf(playerId));
    }

    [Fact]
    public void UpdateStance_StaysMeleeWithNoRangedWeapon()
    {
        var combat = StartedFight(out var playerId);

        Assert.Null(combat.UpdateStance(playerId, Context(distance: 12, hasRangedWeapon: false), Now));
        Assert.Equal(CombatService.AttackStance.Melee, combat.StanceOf(playerId));
    }

    [Fact]
    public void UpdateStance_StaysMeleeInsideMeleeReach()
    {
        var combat = StartedFight(out var playerId);

        Assert.Null(combat.UpdateStance(playerId, Context(distance: 2), Now));
        Assert.Equal(CombatService.AttackStance.Melee, combat.StanceOf(playerId));
    }

    [Fact]
    public void UpdateStance_ClosesToMeleeOnWalkingIn()
    {
        var combat = StartedFight(out var playerId);
        combat.UpdateStance(playerId, Context(distance: 12), Now);

        var change = combat.UpdateStance(playerId, Context(distance: 2), Now);

        Assert.NotNull(change);
        Assert.Equal(CombatService.AttackStance.Ranged, change.From);
        Assert.Equal(CombatService.AttackStance.Melee, change.To);
    }

    [Fact]
    public void UpdateStance_ClosesToMeleeWhenTheRangedWeaponGoesAway()
    {
        var combat = StartedFight(out var playerId);
        combat.UpdateStance(playerId, Context(distance: 12), Now);

        var change = combat.UpdateStance(playerId, Context(distance: 12, hasRangedWeapon: false), Now);

        Assert.NotNull(change);
        Assert.Equal(CombatService.AttackStance.Melee, change.To);
    }

    [Fact]
    public void UpdateStance_ReportsNothingWhenTheStanceIsUnchanged()
    {
        var combat = StartedFight(out var playerId);
        combat.UpdateStance(playerId, Context(distance: 12), Now);

        Assert.Null(combat.UpdateStance(playerId, Context(distance: 14), Now));
    }

    [Fact]
    public void UpdateStance_IgnoresAnAttackerWithNoFight()
    {
        Assert.Null(new CombatService().UpdateStance("player:char-a", Context(distance: 12), Now));
    }

    [Fact]
    public void Tick_MeleeSwingIsWithheldOutOfReachAndLandsOnReturn()
    {
        var combat = StartedFight(out var playerId);
        combat.UpdateStance(playerId, Context(distance: 12, hasRangedWeapon: false), Now);

        var due = Now + CombatService.SwingInterval;
        Assert.DoesNotContain(combat.Tick(due), swing => swing.AttackerId == playerId);

        combat.UpdateStance(playerId, Context(distance: 2, hasRangedWeapon: false), due);
        Assert.Contains(combat.Tick(due), swing => swing.AttackerId == playerId);
    }

    [Fact]
    public void Tick_RangedModeStopsTheMeleeSwingLoop()
    {
        var combat = StartedFight(out var playerId);
        combat.UpdateStance(playerId, Context(distance: 12), Now);

        var swings = combat.Tick(Now + CombatService.SwingInterval);

        Assert.DoesNotContain(swings, swing => swing.AttackerId == playerId);
    }

    [Fact]
    public void TickRangedShot_WaitsAWholeIntervalAfterTheSwitch()
    {
        var combat = StartedFight(out var playerId);
        combat.UpdateStance(playerId, Context(distance: 12), Now);

        Assert.Null(combat.TickRangedShot(playerId, Context(distance: 12), Now));
        Assert.NotNull(combat.TickRangedShot(playerId, Context(distance: 12), Now + RangedInterval));
    }

    [Fact]
    public void TickRangedShot_CarriesTheVisualSpeedAndFlightTime()
    {
        var combat = StartedFight(out var playerId);
        combat.UpdateStance(playerId, Context(distance: 15), Now);

        var shot = combat.TickRangedShot(playerId, Context(distance: 15), Now + RangedInterval);

        Assert.NotNull(shot);
        Assert.Equal(CombatService.RangedAttackSpellVisualId, shot.SpellVisualId);
        Assert.Equal(CombatService.RangedProjectileSpeed, shot.Speed);
        Assert.Equal(
            TimeSpan.FromSeconds(15 / CombatService.RangedProjectileSpeed), shot.FlightTime);
    }

    [Fact]
    public void TickRangedShot_RefusesBeyondTheMaximumRange()
    {
        var combat = StartedFight(out var playerId);
        var far = CombatService.MaxRangedAttackDistance + 1;
        combat.UpdateStance(playerId, Context(distance: far), Now);

        Assert.Null(combat.TickRangedShot(playerId, Context(distance: far), Now + RangedInterval));
    }

    [Fact]
    public void TickRangedShot_HoldsFireWhileMoving()
    {
        var combat = StartedFight(out var playerId);
        combat.UpdateStance(playerId, Context(distance: 12), Now);
        var due = Now + RangedInterval;

        Assert.Null(combat.TickRangedShot(playerId, Context(distance: 12, isMoving: true), due));
        Assert.Null(combat.TickRangedShot(playerId, Context(distance: 12), due));
        Assert.NotNull(combat.TickRangedShot(
            playerId, Context(distance: 12), due + CombatService.RangedDrawTime));
    }

    [Fact]
    public void TickRangedShot_KeepsPushingTheShotOutForAsLongAsTheArcherMoves()
    {
        var combat = StartedFight(out var playerId);
        combat.UpdateStance(playerId, Context(distance: 12), Now);
        var lastStep = Now + RangedInterval + TimeSpan.FromSeconds(3);

        Assert.Null(combat.TickRangedShot(playerId, Context(distance: 12, isMoving: true), Now + RangedInterval));
        Assert.Null(combat.TickRangedShot(playerId, Context(distance: 12, isMoving: true), lastStep));

        Assert.Null(combat.TickRangedShot(
            playerId, Context(distance: 12), lastStep + CombatService.RangedDrawTime - TimeSpan.FromMilliseconds(1)));
        Assert.NotNull(combat.TickRangedShot(playerId, Context(distance: 12), lastStep + CombatService.RangedDrawTime));
    }

    [Fact]
    public void TickRangedShot_NeverDrawsLongerThanTheWeaponInterval()
    {
        var combat = StartedFight(out var playerId);
        var quick = TimeSpan.FromMilliseconds(200);
        RangedAttackContext Quick(bool moving) => new(
            InMeleeRange: false, Distance: 12, HasRangedWeapon: true, RangedInterval: quick, IsMoving: moving);
        combat.UpdateStance(playerId, Quick(false), Now);

        Assert.Null(combat.TickRangedShot(playerId, Quick(true), Now + quick));

        Assert.NotNull(combat.TickRangedShot(playerId, Quick(false), Now + quick + quick));
    }

    [Fact]
    public void TickRangedShot_NeverFiresInMelee()
    {
        var combat = StartedFight(out var playerId);

        Assert.Null(combat.TickRangedShot(playerId, Context(distance: 12), Now + RangedInterval));
    }

    [Fact]
    public void TickRangedShot_StopsAtTheInFlightCap()
    {
        var combat = StartedFight(out var playerId);
        var context = new RangedAttackContext(
            InMeleeRange: false, Distance: CombatService.MaxRangedAttackDistance,
            HasRangedWeapon: true, RangedInterval: TimeSpan.FromMilliseconds(1), IsMoving: false);
        combat.UpdateStance(playerId, context, Now);

        var fired = 0;
        for (var i = 1; i <= CombatService.MaxRangedProjectilesInFlight + 4; i++)
        {
            if (combat.TickRangedShot(playerId, context, Now + TimeSpan.FromMilliseconds(i)) is not null)
                fired++;
        }

        Assert.Equal(CombatService.MaxRangedProjectilesInFlight, fired);
    }

    [Fact]
    public void Tick_ARangedShotDamagesOnlyOnceItsFlightIsOver()
    {
        var combat = StartedFight(out var playerId);
        combat.UpdateStance(playerId, Context(distance: 15), Now);
        var launchedAt = Now + RangedInterval;
        var shot = combat.TickRangedShot(playerId, Context(distance: 15), launchedAt);

        Assert.NotNull(shot);
        Assert.DoesNotContain(combat.Tick(launchedAt), swing => swing.AttackerId == playerId);

        var landing = combat.Tick(launchedAt + shot.FlightTime)
            .Single(swing => swing.AttackerId == playerId);
        Assert.True(landing.Ranged);
        Assert.Equal("Creature:1", landing.TargetId);
        Assert.True(landing.Damage > 0);
    }

    [Fact]
    public void Tick_ARangedShotIsDroppedWhenItsTargetDiesMidFlight()
    {
        var combat = StartedFight(out var playerId);
        combat.UpdateStance(playerId, Context(distance: 15), Now);
        var launchedAt = Now + RangedInterval;
        var shot = combat.TickRangedShot(playerId, Context(distance: 15), launchedAt)!;
        combat.DamageTarget(playerId, "Creature:1", CombatService.CreatureMaxHealth, launchedAt);

        Assert.DoesNotContain(
            combat.Tick(launchedAt + shot.FlightTime), swing => swing.Ranged);
    }

    [Fact]
    public void LooseSpellShot_FliesForTheDistanceOverTheProjectileSpeed()
    {
        var combat = StartedFight(out var playerId);

        var shot = combat.LooseSpellShot(playerId, "Creature:1", distance: 15, Now);

        Assert.Equal(CombatService.RangedProjectileSpeed, shot.Speed);
        Assert.Equal(TimeSpan.FromSeconds(15 / CombatService.RangedProjectileSpeed), shot.FlightTime);
    }

    [Fact]
    public void LooseSpellShot_NeedsNeitherRangedModeNorTheSwingTimer()
    {
        var combat = StartedFight(out var playerId);
        Assert.Equal(CombatService.AttackStance.Melee, combat.StanceOf(playerId));

        var shot = combat.LooseSpellShot(playerId, "Creature:1", distance: 15, Now);

        var landing = combat.Tick(Now + shot.FlightTime).Single(swing => swing.AttackerId == playerId && swing.Ranged);
        Assert.True(landing.Ranged);
        Assert.True(landing.Damage > 0);
    }

    [Fact]
    public void LooseSpellShot_IgnoresTheAutoAttackInFlightCap()
    {
        var playerId = "player:char-a";
        var combat = new CombatService(rollUnit: () => 1.0);
        combat.Start(playerId, "Creature:1", Visible, Now,
            targetStats: new CreatureCombatStats(MaxHealth: 1_000_000, 1, 1, CombatService.SwingInterval));

        for (var i = 0; i < CombatService.MaxRangedProjectilesInFlight + 3; i++)
        {
            combat.LooseSpellShot(playerId, "Creature:1", distance: 15, Now);
        }

        var flight = TimeSpan.FromSeconds(15 / CombatService.RangedProjectileSpeed);
        Assert.Equal(
            CombatService.MaxRangedProjectilesInFlight + 3,
            combat.Tick(Now + flight).Count(swing => swing.Ranged));
    }

    [Fact]
    public void LooseSpellShot_CarriesTheEffectsOwnPercentAndFlatBonusIntoTheHit()
    {
        var plain = StartedFight(out var playerId);
        var plainShot = plain.LooseSpellShot(playerId, "Creature:1", distance: 15, Now);
        var baseline = plain.Tick(Now + plainShot.FlightTime).Single(swing => swing.Ranged).Damage;

        var combat = StartedFight(out playerId);
        var shot = combat.LooseSpellShot(
            playerId, "Creature:1", distance: 15, Now, damagePercent: 100, damageBonus: 15);

        var landing = combat.Tick(Now + shot.FlightTime).Single(swing => swing.Ranged);
        Assert.Equal(baseline + 15, landing.Damage);
    }

    [Fact]
    public void StrikeSpellBlow_LandsOnTheCastWithNoFlightAtAll()
    {
        var combat = StartedFight(out var playerId);

        var blow = combat.StrikeSpellBlow(playerId, "Creature:1", Now);

        Assert.True(blow.Damage > 0);
        Assert.False(blow.Ranged);
        Assert.DoesNotContain(combat.Tick(Now + TimeSpan.FromSeconds(5)), swing => swing.Ranged);
    }

    [Fact]
    public void StrikeSpellBlow_CarriesTheEffectsOwnPercentAndFlatBonusIntoTheHit()
    {
        var baselineCombat = StartedFight(out var playerId);
        var baseline = baselineCombat.StrikeSpellBlow(playerId, "Creature:1", Now).Damage;

        var combat = StartedFight(out playerId);
        var blow = combat.StrikeSpellBlow(
            playerId, "Creature:1", Now, damagePercent: 110, damageBonus: 10);

        Assert.Equal(CombatService.ApplyWeaponDamageModifiers(baseline, 110, 10), blow.Damage);
    }

    [Fact]
    public void StrikeSpellBlow_NeedsNoFightInProgressAndStartsOneItself()
    {
        var combat = new CombatService(rollUnit: () => 1.0);

        var blow = combat.StrikeSpellBlow("player:char-a", "Creature:1", Now);

        Assert.True(blow.Damage > 0);
        Assert.Equal(CombatService.AttackStance.Melee, combat.StanceOf("player:char-a"));
        Assert.Equal("Creature:1", combat.CurrentTargetOf("player:char-a"));
        Assert.NotEmpty(combat.Tick(Now + CombatService.SwingInterval));
    }

    [Fact]
    public void StrikeSpellBlow_LeavesAFightAlreadyRunningOnItsOwnTarget()
    {
        var combat = StartedFight(out var playerId);

        combat.StrikeSpellBlow(playerId, "Creature:2", Now);

        Assert.Equal("Creature:1", combat.CurrentTargetOf(playerId));
    }

    [Fact]
    public void StrikeSpellBlow_AMissCarriesNoDamageToScale()
    {
        var combat = new CombatService(rollUnit: () => 0.0);
        combat.Start("player:char-a", "Creature:1", Visible, Now);

        var blow = combat.StrikeSpellBlow(
            "player:char-a", "Creature:1", Now, damagePercent: 110, damageBonus: 10);

        Assert.Equal(StrikeResult.Miss, blow.Result);
        Assert.Equal(0, blow.Damage);
    }

    [Fact]
    public void StrikeSpellBlow_SharesTheSameHealthPoolAndDeathHandlingAsASwing()
    {
        var combat = new CombatService(rollUnit: () => 1.0);
        combat.Start("player:char-a", "Creature:1", Visible, Now,
            targetStats: new CreatureCombatStats(MaxHealth: 1, 1, 1, CombatService.SwingInterval));

        var blow = combat.StrikeSpellBlow("player:char-a", "Creature:1", Now);

        Assert.True(blow.Killed);
        Assert.Equal(0, blow.FinalHealth);
        Assert.True(combat.IsDead("Creature:1"));
    }

    private static readonly CreatureCombatStats BigCreature = new(
        MaxHealth: 99999, 9, 15, CombatService.SwingInterval);

    [Fact]
    public void DamageTarget_UsesTheResolvedMaxHealthForATargetNoFightEverStarted()
    {
        var combat = new CombatService();
        combat.UseTargetStats(_ => BigCreature);

        var blow = combat.DamageTarget("player:char-a", "Creature:1", 40, Now);

        Assert.Equal(99999, blow.MaxHealth);
        Assert.Equal(99959, blow.FinalHealth);
        Assert.False(blow.Killed);
    }

    [Fact]
    public void DamageTarget_ResolvesATrainingDummyForAFightThatOpenedWithASpell()
    {
        var combat = new CombatService(rollDamage: (min, max) => max);
        combat.UseTargetStats(_ => BigCreature with { IsTrainingDummy = true });

        combat.DamageTarget("player:char-a", "Creature:1", 40, Now);
        var swings = combat.Tick(Now + CombatService.SwingInterval);

        Assert.DoesNotContain(swings, s => s.AttackerId == "Creature:1");
    }

    [Fact]
    public void DamageTarget_KeepsResolvedStatsEvenAfterTheResolverStopsAnsweringForThatTarget()
    {
        var combat = new CombatService();
        var visible = true;
        combat.UseTargetStats(_ => visible ? BigCreature : null);

        combat.DamageTarget("player:char-a", "Creature:1", 40, Now);
        visible = false;
        var later = combat.DamageTarget("player:char-a", "Creature:1", 40, Now);

        Assert.Equal(99999, later.MaxHealth);
        Assert.Equal(99919, later.FinalHealth);
    }

    private static CombatService WithTarget(CreatureCombatStats stats)
    {
        var combat = new CombatService(rollDamage: (min, max) => max);
        combat.UseTargetStats(_ => stats);
        return combat;
    }

    [Fact]
    public void DamageTarget_TakesNoHealthFromAFlaggedDummyButStillReportsTheDamage()
    {
        var combat = WithTarget(new CreatureCombatStats(
            83, 9, 14, CombatService.SwingInterval, IsTrainingDummy: true, IgnoresHealthLoss: true));

        var blow = combat.DamageTarget("player:char-a", "Creature:1", 500, Now);

        Assert.Equal(500, blow.Damage);
        Assert.Equal(83, blow.FinalHealth);
        Assert.Equal(83, blow.MaxHealth);
        Assert.False(blow.Killed);
        Assert.False(combat.IsDead("Creature:1"));
    }

    [Fact]
    public void DamageTarget_NeverKillsAFlaggedDummyHoweverManyBlowsLand()
    {
        var combat = WithTarget(new CreatureCombatStats(
            83, 9, 14, CombatService.SwingInterval, IsTrainingDummy: true, IgnoresHealthLoss: true));

        for (var blow = 0; blow < 50; blow++)
            combat.DamageTarget("player:char-a", "Creature:1", 83, Now);

        Assert.False(combat.IsDead("Creature:1"));
        Assert.Equal(83, combat.DamageTarget("player:char-a", "Creature:1", 83, Now).FinalHealth);
    }

    [Fact]
    public void DamageTarget_KillsADummyThatNeverFightsBackButCarriesNoFlag()
    {
        var combat = WithTarget(new CreatureCombatStats(
            40, 9, 15, CombatService.SwingInterval, IsTrainingDummy: true, IgnoresHealthLoss: false));

        var blow = combat.DamageTarget("player:char-a", "Creature:1", 40, Now);

        Assert.True(blow.Killed);
        Assert.Equal(0, blow.FinalHealth);
        Assert.True(combat.IsDead("Creature:1"));
    }

    [Fact]
    public void DamageTarget_PutsTheCasterIntoCombatOnTheTargetItDamaged()
    {
        var combat = WithTarget(BigCreature);

        combat.DamageTarget("player:char-a", "Creature:1", 10, Now);

        Assert.Equal("Creature:1", combat.CurrentTargetOf("player:char-a"));
        Assert.Equal(CombatService.AttackStance.Melee, combat.StanceOf("player:char-a"));
    }

    [Fact]
    public void DamageTarget_EnrolsTheCreatureSoItSwingsBack()
    {
        var combat = WithTarget(BigCreature);

        combat.DamageTarget("player:char-a", "Creature:1", 10, Now);
        var swings = combat.Tick(Now + CombatService.SwingInterval);

        Assert.Contains(swings, s => s.AttackerId == "Creature:1" && s.TargetId == "player:char-a");
    }

    [Fact]
    public void DamageTarget_ReportsTheEntryOnceAndThenClearsIt()
    {
        var combat = WithTarget(BigCreature);

        combat.DamageTarget("player:char-a", "Creature:1", 10, Now);
        var entries = combat.DrainCombatEntries();

        var entry = Assert.Single(entries);
        Assert.Equal("player:char-a", entry.AttackerId);
        Assert.Equal("Creature:1", entry.TargetId);
        Assert.True(entry.EnteredCombat);
        Assert.Empty(combat.DrainCombatEntries());
    }

    [Fact]
    public void DamageTarget_ReportsNoEntryForAFightThatIsAlreadyRunning()
    {
        var combat = StartedFight(out var playerId);
        combat.DrainCombatEntries();

        combat.DamageTarget(playerId, "Creature:2", 10, Now);

        Assert.Empty(combat.DrainCombatEntries());
        Assert.Equal("Creature:1", combat.CurrentTargetOf(playerId));
    }

    [Fact]
    public void DamageTarget_StillEntersCombatAgainstAFlaggedDummyThatLosesNoHealth()
    {
        var combat = WithTarget(new CreatureCombatStats(
            83, 9, 14, CombatService.SwingInterval, IsTrainingDummy: true, IgnoresHealthLoss: true));

        combat.DamageTarget("player:char-a", "Creature:1", 500, Now);

        Assert.Equal("Creature:1", combat.CurrentTargetOf("player:char-a"));
        Assert.Single(combat.DrainCombatEntries());
        Assert.DoesNotContain(
            combat.Tick(Now + CombatService.SwingInterval), s => s.AttackerId == "Creature:1");
    }

    [Fact]
    public void EnterCombatOnCast_OpensTheFightWithNoDamageAtAll()
    {
        var combat = WithTarget(BigCreature);

        combat.EnterCombatOnCast("player:char-a", "Creature:1", Now);

        Assert.Equal("Creature:1", combat.CurrentTargetOf("player:char-a"));
        Assert.Single(combat.DrainCombatEntries());
        Assert.Contains(combat.Tick(Now + CombatService.SwingInterval), s => s.AttackerId == "Creature:1");
    }

    [Fact]
    public void DamageTarget_OpensNoFightWithTheCasterThemselves()
    {
        var combat = WithTarget(BigCreature);

        combat.DamageTarget("player:char-a", "player:char-a", 10, Now);

        Assert.Null(combat.CurrentTargetOf("player:char-a"));
        Assert.Empty(combat.DrainCombatEntries());
    }

    [Fact]
    public void DamageTarget_OpensNoFightOnBehalfOfACreature()
    {
        var combat = WithTarget(BigCreature);

        combat.DamageTarget("Creature:5", "Creature:1", 10, Now);

        Assert.Null(combat.CurrentTargetOf("Creature:5"));
        Assert.Empty(combat.DrainCombatEntries());
    }

    [Fact]
    public void ApplyWeaponDamageModifiers_ScalesThenAdds()
    {
        Assert.Equal(65, CombatService.ApplyWeaponDamageModifiers(100, 50, 15));
        Assert.Equal(115, CombatService.ApplyWeaponDamageModifiers(100, 0, 15));
        Assert.Equal(0, CombatService.ApplyWeaponDamageModifiers(10, 100, -50));
    }

    [Fact]
    public void Disengage_LeavesTheAttackerWithNoStance()
    {
        var combat = StartedFight(out var playerId);
        combat.UpdateStance(playerId, Context(distance: 12), Now);

        combat.Disengage(playerId);

        Assert.Equal(CombatService.AttackStance.None, combat.StanceOf(playerId));
    }

    [Fact]
    public void Tick_AKillLeavesTheAttackerWithNoStance()
    {
        var combat = StartedFight(out var playerId);
        combat.DamageTarget(playerId, "Creature:1", CombatService.CreatureMaxHealth, Now);

        Assert.Equal(CombatService.AttackStance.None, combat.StanceOf(playerId));
    }

    [Fact]
    public void TickRegen_ComesBackWhileOtherCreaturesAreStillFightingEachOther()
    {
        var combat = new CombatService(rollUnit: () => 1.0);
        combat.Start("player:char-a", "Creature:1", Visible, Now);
        var at = Now;
        for (var swing = 0; swing < 9; swing++)
        {
            at += CombatService.SwingInterval;
            combat.Tick(at);
        }

        combat.Start("Creature:8", "Creature:9",
            new HashSet<string>(["Creature:8", "Creature:9"], StringComparer.Ordinal), at, null);

        Assert.NotNull(combat.TickRegen(at + CombatService.RegenInterval));
    }

    [Fact]
    public void TickRegen_HoldsWhileThisPlayerIsTheOneFighting()
    {
        var combat = new CombatService(rollUnit: () => 1.0);
        combat.Start("player:char-a", "Creature:1", Visible, Now);
        var at = Now + CombatService.SwingInterval;
        combat.Tick(at);

        Assert.Null(combat.TickRegen(at + CombatService.RegenInterval));
    }

    [Fact]
    public void ApplyArmorMitigation_TakesTheSameBiteOutOfAnAbilityAsOutOfASwing()
    {
        const int raw = 300;
        const int armor = 200;

        var mitigated = CombatService.ApplyArmorMitigation(raw, armor);

        Assert.True(mitigated < raw, "armour should take something off");
        Assert.True(mitigated >= 1, "and never take everything off");
        Assert.Equal(raw, CombatService.ApplyArmorMitigation(raw, 0));
    }
}
