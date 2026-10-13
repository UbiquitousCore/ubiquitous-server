using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Modules.Playerbots;

// Wearing what is better, selling what is not, and spending talent points.
public sealed partial class BotBrain
{
    private const float WorthSwapping = 0.5f;
    private const int SellWhenFreeSlotsAtMost = 3;
    private const int RoomWanted = 6;
    private static readonly TimeSpan RetrySort = TimeSpan.FromSeconds(30);
    private bool _wantRoom;
    private bool _bagsOutOfStep;
    private DateTimeOffset _nextSort;
    private static readonly TimeSpan RetryGear = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RetryTalent = TimeSpan.FromMinutes(10);
    private int? _favouriteTree;

    private BotDecision? Equip(BotView view, DateTimeOffset now)
    {
        if (view.InCombat || view.Class == 0) return null;
        var worn = view.Items.Values.Where(item => item.Area == InventoryAreas.Equipment).ToDictionary(item => item.Index);
        var best = view.Items.Values
            .Where(item => item.Area == InventoryAreas.Bag && !Shunned($"gear|{item.Id}", now))
            .Select(item => (Item: item, Definition: knowledge.Item(item.Entry)))
            .Where(pair => pair.Definition is not null && knowledge.CanWear(pair.Definition, view.Class, view.Level))
            .SelectMany(pair => knowledge.SlotsFor(pair.Definition!).Select(slot => (pair.Item, Slot: slot,
                Gain: BotKnowledge.Worth(pair.Definition, view.Class)
                    - BotKnowledge.Worth(worn.TryGetValue(slot, out var on) ? knowledge.Item(on.Entry) : null, view.Class))))
            .Where(choice => choice.Gain > WorthSwapping)
            .OrderByDescending(choice => choice.Gain)
            .FirstOrDefault();
        if (best.Item is null) return null;

        Shun($"gear|{best.Item.Id}", now, RetryGear);
        return new SendMessage(WebMessageTypes.InventoryEquip, new InventoryEquipPayload(
            new StorageSlotPayload(best.Item.Area, best.Item.Index), best.Item.Id, true,
            new StorageSlotPayload(InventoryAreas.Equipment, best.Slot)));
    }

    // With bags nearly full a bot takes what it will never wear to the nearest vendor it knows.
    private BotDecision? Sell(BotView view, DateTimeOffset now)
    {
        _wantRoom |= view.ItemRefusals.Contains(ItemErrors.InventoryFull);
        _bagsOutOfStep |= view.SellRefusals.Contains(ItemErrors.ItemNotInSlot);
        view.ItemRefusals.Clear();
        view.SellRefusals.Clear();

        // Sorting answers with the whole bag, which puts the bot's picture of it right again.
        if (_bagsOutOfStep && now >= _nextSort)
        {
            _bagsOutOfStep = false;
            _nextSort = now + RetrySort;
            return new SendMessage(WebMessageTypes.InventorySort, new InventorySortPayload(InventoryAreas.Bag));
        }

        if (view.FreeBagSlots >= RoomWanted) _wantRoom = false;
        if (view.BagSlots <= 0 || (view.FreeBagSlots > SellWhenFreeSlotsAtMost && !_wantRoom)) return null;
        var junk = view.Items.Values
            .Where(item => item.Area == InventoryAreas.Bag && !Shunned($"sell|{item.Id}", now) && IsJunk(view, item))
            .OrderBy(item => item.Index)
            .FirstOrDefault();
        if (junk is null) return null;

        var vendor = view.Vendors
            .Where(pair => pair.Value.MapId == view.MapId && !Shunned(pair.Key, now))
            .OrderBy(pair => Flat(view.Position, pair.Value.Position))
            .Select(pair => (Id: pair.Key, pair.Value.Position))
            .FirstOrDefault();
        if (vendor.Id is null) return null;
        if (!InReach(view.Position, vendor.Position) || !view.Units.ContainsKey(vendor.Id))
            return new WalkTo(vendor.Position, TalkReach * 0.6f, vendor.Id, InteractReach);

        Shun($"sell|{junk.Id}", now, RetryGear);
        return new SendMessage(WebMessageTypes.VendorSell, new
        {
            networkId = vendor.Id,
            from = new StorageSlotPayload(junk.Area, junk.Index),
            itemId = junk.Id,
        });
    }

    // Anything a vendor buys that the bot will not wear, use or hand in: drops,
    // materials, gear for another class or worse than what it has on.
    private bool IsJunk(BotView view, HeldItem item)
    {
        if (knowledge.Item(item.Entry) is not { } definition) return false;
        if (definition.QuestId is not null || definition.SellPrice is null or 0 || definition.OnUseSpellId is not null) return false;
        if (view.Quests.Values.Any(quest => knowledge.ObjectiveItems(quest.Id).Contains((uint)item.Entry))) return false;
        if (definition.Quality == 0 || knowledge.SlotsFor(definition).Count == 0) return true;
        if (!knowledge.CanWear(definition, view.Class, int.MaxValue)) return true;
        var worth = BotKnowledge.Worth(definition, view.Class);
        return knowledge.SlotsFor(definition).All(slot => view.Items.Values.Any(on => on.Area == InventoryAreas.Equipment
            && on.Index == slot && BotKnowledge.Worth(knowledge.Item(on.Entry), view.Class) >= worth));
    }

    // Points go down one tree, lowest tier first; a refused talent is left alone for a while.
    private BotDecision? SpendTalent(BotView view, DateTimeOffset now)
    {
        foreach (var refused in view.TalentRefusals) Shun($"talent|{refused}", now, RetryTalent);
        view.TalentRefusals.Clear();
        if (view.Class == 0 || view.InCombat) return null;
        var spent = view.TalentRanks.Values.Sum();
        if (TalentBook.PointsForLevel(view.Level) - spent <= 0) return null;

        var trees = knowledge.TalentTreesFor(view.Class).ToList();
        if (trees.Count == 0) return null;
        _favouriteTree ??= random.Next(trees.Count);
        foreach (var tree in trees.Skip(_favouriteTree.Value).Concat(trees.Take(_favouriteTree.Value)))
        {
            var inTree = tree.Talents.Sum(talent => view.TalentRanks.GetValueOrDefault(talent.Id));
            var next = tree.Talents
                .Where(talent => view.TalentRanks.GetValueOrDefault(talent.Id) < talent.MaxRank
                    && talent.PointsInTreeRequired <= inTree
                    && talent.Prerequisites.All(needed => needed == 0 || view.TalentRanks.GetValueOrDefault(needed) > 0)
                    && !Shunned($"talent|{talent.Id}", now))
                .OrderBy(talent => talent.Tier)
                .ThenBy(talent => talent.Column)
                .FirstOrDefault();
            if (next is null) continue;
            Shun($"talent|{next.Id}", now, TimeSpan.FromSeconds(2));
            return new SendMessage(WebMessageTypes.TalentLearn, new TalentLearnRequest(next.Id));
        }

        return null;
    }
}
