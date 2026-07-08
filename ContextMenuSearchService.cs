using System;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text.SeStringHandling;
using Lumina.Excel.Sheets;

namespace DalamudRecipeHelper;

public sealed class ContextMenuSearchService : IDisposable
{
    private static readonly string[] MarketboardAddonNames =
    [
        "ItemSearch",
        "ItemSearchResult",
    ];

    private readonly FileLogService fileLog;
    private readonly Action<string> openSearch;

    public ContextMenuSearchService(
        FileLogService fileLog,
        Action<string> openSearch)
    {
        this.fileLog = fileLog;
        this.openSearch = openSearch;
        Plugin.ContextMenu.OnMenuOpened += this.OnMenuOpened;
    }

    public void Dispose() =>
        Plugin.ContextMenu.OnMenuOpened -= this.OnMenuOpened;

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (!this.TryGetSearchItemId(args, out var itemId))
            return;

        var itemName = Plugin.DataManager.GetExcelSheet<Item>()?.GetRow(itemId).Name.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(itemName))
            itemName = itemId.ToString();

        var capturedSearchText = itemName;
        args.AddMenuItem(new MenuItem
        {
            Name = new SeStringBuilder().AddText("Search in Recipe Helper").Build(),
            UseDefaultPrefix = true,
            OnClicked = _ =>
            {
                this.fileLog.Info(
                    "ContextMenu",
                    $"Opened Recipe Helper from inventory item {itemId} with search '{capturedSearchText}'.");
                this.openSearch(capturedSearchText);
            },
        });
    }

    private bool TryGetSearchItemId(IMenuOpenedArgs args, out uint itemId)
    {
        itemId = 0;

        if (args.MenuType == ContextMenuType.Inventory &&
            args.Target is MenuTargetInventory inventoryTarget &&
            inventoryTarget.TargetItem is { } inventoryItem)
        {
            itemId = NormalizeItemId(inventoryItem.ItemId);
            return itemId != 0;
        }

        if (args.MenuType != ContextMenuType.Default ||
            !IsMarketboardAddon(args.AddonName))
            return false;

        itemId = NormalizeItemId(Plugin.GameGui.HoveredItem);
        if (itemId == 0)
        {
            this.fileLog.Info(
                "ContextMenu",
                $"Marketboard context menu opened from '{args.AddonName}' without a hovered item.");
            return false;
        }

        return true;
    }

    private static uint NormalizeItemId(uint itemId)
    {
        const uint highQualityOffset = 1_000_000;
        return itemId > highQualityOffset ? itemId - highQualityOffset : itemId;
    }

    private static uint NormalizeItemId(ulong itemId) =>
        itemId > uint.MaxValue
            ? 0
            : NormalizeItemId((uint)itemId);

    private static bool IsMarketboardAddon(string? addonName) =>
        !string.IsNullOrWhiteSpace(addonName) &&
        MarketboardAddonNames.Contains(addonName, StringComparer.Ordinal);
}
