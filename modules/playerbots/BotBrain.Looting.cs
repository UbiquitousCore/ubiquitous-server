using Ubiquitous.Data;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Modules.Playerbots;

public enum LootStyle
{
    // Takes everything a corpse holds.
    Hoarder,
    // Leaves grey trash it cannot use.
    Ordinary,
    // Takes only coin, quest items, decent gear its class wears and things worth selling.
    Picky,
}

// Players loot differently: some sweep a corpse clean, some leave a bad sword
// for someone else. With full bags a bot makes room for what it wants by
// throwing away the least it holds, never something a quest needs.
public sealed partial class BotBrain
{
    private const int PickyWorthAtLeast = 10;
    private const int DecentQuality = 2;

    public LootStyle Looting { get; } = settings.LootStyle ?? random.NextDouble() switch
    {
        < 0.3 => LootStyle.Hoarder,
        < 0.75 => LootStyle.Ordinary,
        _ => LootStyle.Picky,
    };

    private bool Wants(BotView view, LootRow row)
    {
        if (knowledge.Item(row.Entry) is not { } item) return Looting == LootStyle.Hoarder;
        if (NeededForAQuest(view, row.Entry)) return true;
        return Looting switch
        {
            LootStyle.Hoarder => true,
            LootStyle.Ordinary => item.Quality is not 0 || knowledge.CanWear(item, view.Class, view.Level),
            _ => (item.SellPrice ?? 0) * row.Count >= PickyWorthAtLeast
                || (item.Quality >= DecentQuality && knowledge.CanWear(item, view.Class, int.MaxValue)),
        };
    }

    private bool NeededForAQuest(BotView view, int entry) =>
        knowledge.Item(entry)?.QuestId is not null
        || view.Quests.Values.Any(quest => !quest.Ready && knowledge.ObjectiveItems(quest.Id).Contains((uint)entry));

    // What keeping an item is worth to this bot: a quest needs it, it beats what is worn, or its price.
    private float Keeping(BotView view, int entry, int count)
    {
        if (NeededForAQuest(view, entry)) return float.MaxValue;
        if (knowledge.Item(entry) is not { } item) return 0f;
        var upgrade = knowledge.CanWear(item, view.Class, view.Level)
            && knowledge.SlotsFor(item).Any(slot => BotKnowledge.Worth(item, view.Class)
                > BotKnowledge.Worth(view.Items.Values.FirstOrDefault(on => on.Area == InventoryAreas.Equipment && on.Index == slot)
                    is { } worn ? knowledge.Item(worn.Entry) : null, view.Class) + WorthSwapping);
        return upgrade ? 1_000_000f : (item.SellPrice ?? 0) * count;
    }

    // One row a turn: take what is wanted, making room by throwing away something worth less.
    private BotDecision? LootRows(BotView view, LootWindow window, DateTimeOffset now)
    {
        foreach (var row in window.Rows.Where(row => !_triedSlots.Contains(row.Slot)))
        {
            if (!Wants(view, row))
            {
                _triedSlots.Add(row.Slot);
                continue;
            }

            if (view.BagSlots > 0 && view.FreeBagSlots <= 0)
            {
                var worth = Keeping(view, row.Entry, row.Count);
                var cheapest = view.Items.Values
                    .Where(item => item.Area == InventoryAreas.Bag && !Shunned($"bin|{item.Id}", now))
                    .Select(item => (Item: item, Worth: Keeping(view, item.Entry, Math.Max(1, item.Count))))
                    .Where(pair => pair.Worth < worth)
                    .OrderBy(pair => pair.Worth)
                    .FirstOrDefault();
                if (cheapest.Item is null)
                {
                    _triedSlots.Add(row.Slot);
                    continue;
                }

                Shun($"bin|{cheapest.Item.Id}", now, RetryGear);
                return new SendMessage(WebMessageTypes.InventoryDestroy, new InventoryDestroyPayload(
                    new StorageSlotPayload(cheapest.Item.Area, cheapest.Item.Index), cheapest.Item.Id, Math.Max(1, cheapest.Item.Count)));
            }

            _triedSlots.Add(row.Slot);
            return new SendMessage(WebMessageTypes.LootTake, new { networkId = window.CorpseId, slot = row.Slot });
        }

        return null;
    }
}
