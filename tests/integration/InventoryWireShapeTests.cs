namespace Ubiquitous.Integration.Tests;

using System.Text.Json;
using System.Text.Json.Nodes;
using Ubiquitous.Data;
using Ubiquitous.Protocol.Web;

public sealed class InventoryWireShapeTests
{
    private static readonly StorageSlotPayload BagThree = new("bag", 3);
    private static readonly StorageSlotPayload BagSeven = new("bag", 7);
    private static readonly StorageSlotPayload WornRanged = new("equipment", EquipmentSlots.Ranged);

    private static readonly ItemPrototypePayload BowPrototype =
        new(90001, 2, 16, 0, 13, 1, 120, 30, []);

    private static readonly InventoryItemPayload SampleItem =
        new("item-90001-1", 90001, 1, BagThree, BowPrototype);

    public static IReadOnlyList<(string Type, object Payload)> Messages { get; } =
    [
        (WebMessageTypes.InventoryUpdated, new InventoryUpdatedPayload(16, 24, [SampleItem])),
        (WebMessageTypes.InventoryItemAdded, SampleItem),
        (WebMessageTypes.InventoryItemRemoved, new InventoryItemRemovedPayload("item-90001-1")),
        (WebMessageTypes.InventoryItemMoved,
            new InventoryItemMovedPayload("item-90001-1", BagThree, BagSeven)),
        (WebMessageTypes.InventoryItemsSwapped, new InventoryItemsSwappedPayload(
            new InventorySwapSidePayload(BagThree, "item-90001-1"),
            new InventorySwapSidePayload(WornRanged))),
        (WebMessageTypes.InventoryEquipResult, new InventoryEquipResultPayload(ItemErrors.CantEquip)),
        (WebMessageTypes.InventoryStackChanged,
            new InventoryStackChangedPayload("item-90001-1", BagThree, 4)),
        (WebMessageTypes.EntityAppearanceChanged, new EntityAppearanceChangedPayload(
            WebNetworkId.FromString("player:char-a"),
            EquipmentSlots.Build((EquipmentSlots.MainHand, 90002), (EquipmentSlots.Ranged, 90001)))),
        (WebMessageTypes.InventoryBankUpdated, new InventoryBankUpdatedPayload(24, [SampleItem])),
        (WebMessageTypes.InventoryCapacityChanged, new InventoryCapacityChangedPayload("bag", 20)),
        (WebMessageTypes.InventorySorted, new InventorySortedPayload(true, "bag")),
        (WebMessageTypes.InventoryBankOpen, new InventoryBankOpenPayload(true)),
    ];

    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "inventory-messages.json");

    private static string Canonical()
    {
        var root = new JsonObject();
        foreach (var (type, payload) in Messages) root[type] = PayloadOf(payload);
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonNode PayloadOf(object payload) =>
        JsonNode.Parse(WebMessageJson.Serialize(WebMessageEnvelope.Create(WebMessageTypes.SessionPong, 0, payload)))!["payload"]!.DeepClone();

    [Fact]
    public void TheCommittedFixtureIsWhatThisServerActuallySends()
    {
        var canonical = Canonical();
        if (Environment.GetEnvironmentVariable("UBIQUITOUS_WRITE_WIRE_FIXTURES") == "1")
        {
            var source = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "fixtures", "inventory-messages.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            File.WriteAllText(source, canonical + "\n");
        }

        Assert.True(File.Exists(FixturePath),
            $"Missing {FixturePath}. Regenerate with UBIQUITOUS_WRITE_WIRE_FIXTURES=1.");
        Assert.Equal(canonical.ReplaceLineEndings("\n"), File.ReadAllText(FixturePath).ReplaceLineEndings("\n").TrimEnd('\n'));
    }

    [Fact]
    public void NoSingleItemMessageWrapsTheItemInANestedMember()
    {
        foreach (var (type, payload) in Messages)
        {
            Assert.False(PayloadOf(payload).AsObject().ContainsKey("item"),
                $"{type} wraps its item in an \"item\" member; the item block is flat.");
        }
    }

    [Fact]
    public void TheSnapshotsRowsAreTheSameBlockAsASingleItemPush()
    {
        var pushed = PayloadOf(SampleItem).ToJsonString();
        var snapshot = PayloadOf(new InventoryUpdatedPayload(16, 24, [SampleItem]))
            .AsObject()["items"]!.AsArray()[0]!.ToJsonString();

        Assert.Equal(pushed, snapshot);
    }

    [Fact]
    public void EverySlotOnTheWireIsTheSameTwoFieldObject()
    {
        var seen = 0;
        foreach (var (_, payload) in Messages)
        {
            foreach (var slot in SlotsIn(PayloadOf(payload)))
            {
                seen++;
                Assert.Equal(["area", "index"], slot.Select(field => field.Key).Order());
                Assert.Equal(JsonValueKind.String, slot["area"]!.GetValueKind());
                Assert.Equal(JsonValueKind.Number, slot["index"]!.GetValueKind());
            }
        }

        Assert.True(seen > 0, "No slot objects were found to check.");
    }

    [Fact]
    public void AppearanceIsSixteenPositionalSlotsAndNotAListOfWornItems()
    {
        var appearance = Messages.Single(message => message.Type == WebMessageTypes.EntityAppearanceChanged);
        var equipment = PayloadOf(appearance.Payload).AsObject()["equipment"]!.AsArray();

        Assert.Equal(EquipmentSlots.Count, equipment.Count);
        Assert.Equal(90002, (int)equipment[EquipmentSlots.MainHand]!);
        Assert.Equal(90001, (int)equipment[EquipmentSlots.Ranged]!);
        Assert.Equal(0, (int)equipment[EquipmentSlots.Head]!);
        Assert.Equal(2, equipment.Count(entry => (int)entry! != 0));
    }

    [Fact]
    public void EveryMessageReadsBackAsWhatWasSent()
    {
        foreach (var (type, payload) in Messages)
        {
            var sent = WebMessageJson.Deserialize(WebMessageJson.Serialize(WebMessageEnvelope.Create(type, 0, payload)));
            var read = typeof(WebMessageJson).GetMethod(nameof(WebMessageJson.DeserializePayload))!
                .MakeGenericMethod(payload.GetType()).Invoke(null, [sent.Payload]);
            Assert.Equal(PayloadOf(payload).ToJsonString(), PayloadOf(read!).ToJsonString());
        }
    }

    [Fact]
    public void EveryItemCarriesTheRowThatSaysWhereItIsWorn()
    {
        foreach (var (type, payload) in Messages)
        {
            foreach (var item in ItemsIn(PayloadOf(payload)))
            {
                var prototype = Assert.IsType<JsonObject>(item["details"]);
                Assert.True(prototype.ContainsKey("inventoryType"),
                    $"{type} carries an item with no inventoryType; nothing can be equipped without it.");
                Assert.True(prototype.ContainsKey("requiredLevel"));
                Assert.True(prototype.ContainsKey("spells"));
            }
        }
    }

    private static IEnumerable<JsonObject> ItemsIn(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                if (o.ContainsKey("id") && o.ContainsKey("entry") && o.ContainsKey("count")) yield return o;
                foreach (var (_, value) in o)
                    foreach (var nested in ItemsIn(value)) yield return nested;
                break;
            case JsonArray a:
                foreach (var entry in a)
                    foreach (var nested in ItemsIn(entry)) yield return nested;
                break;
        }
    }

    private static IEnumerable<JsonObject> SlotsIn(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var (key, value) in o)
                {
                    if (key is "slot" or "from" or "to" && value is JsonObject slot) yield return slot;
                    foreach (var nested in SlotsIn(value)) yield return nested;
                }

                break;
            case JsonArray a:
                foreach (var entry in a)
                    foreach (var nested in SlotsIn(entry)) yield return nested;
                break;
        }
    }
}
