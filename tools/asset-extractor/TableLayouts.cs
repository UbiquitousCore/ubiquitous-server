namespace Ubiquitous.AssetExtractor;

// Every table the extractor reads by column, so a build can be checked
// against all of them at once.
public static class TableLayouts
{
    public static readonly IReadOnlyList<ColumnLayout> All =
    [
        MapInfoColumns.Layout,
        ZoneColumns.Layout,
        StaticObjectColumns.Layout,
        StaticObjectProtoColumns.Layout,
        StaticObjectDisplayColumns.Layout,
        CreatureSpawnColumns.Layout,
        CreatureAppearanceColumns.Layout,
        CreatureProtoColumns.Layout,
        SpellNewColumns.Layout,
        SpellEffectColumns.Layout,
        ItemColumns.Layout,
        QuestColumns.Layout,
        QuestBindingColumns.CreatureStarter,
        QuestBindingColumns.CreatureFinisher,
        QuestBindingColumns.StaticObjectStarter,
        QuestBindingColumns.StaticObjectFinisher,
        QuestBindingColumns.StaticObjectItem,
        GossipMenuColumns.Layout,
        TeleportStopColumns.Layout,
        TravelRouteColumns.Layout,
        MapGraveyardColumns.Layout,
        StorageUpgradeColumns.Layout,
        TalentColumns.Layout,
        TalentColumns.ClassLayout,
        VendorColumns.ListLayout,
        VendorColumns.ItemLayout,
        LootColumns.Layout,
        LootColumns.GroupLayout,
        LootColumns.ItemLayout,
        DungeonRewardColumns.Layout,
    ];
}
