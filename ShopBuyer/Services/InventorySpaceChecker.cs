using System;
using ExileCore;
using ExileCore.PoEMemory.Elements.InventoryElements;

namespace AutoExile.ShopBuyer.Services
{
    public static class InventorySpaceChecker
    {
        private const int InvColumns = 12;
        private const int InvRows = 5;

        public static bool HasSpaceForItem(GameController gc, int itemWidth, int itemHeight)
        {
            try
            {
                var ingameUi = gc?.Game?.IngameState?.IngameUi ?? gc?.IngameState?.IngameUi;
                if (ingameUi == null) return true;

                var invPanel = ingameUi.InventoryPanel;
                if (invPanel == null || !invPanel.IsValid || !invPanel.IsVisible)
                {
                    return true;
                }

                var invElement = invPanel[ExileCore.Shared.Enums.InventoryIndex.PlayerInventory];
                if (invElement == null || !invElement.IsValid) return true;

                var items = invElement.VisibleInventoryItems;
                if (items == null) return true;

                var grid = new bool[InvColumns, InvRows];

                foreach (var invItem in items)
                {
                    if (invItem == null || !invItem.IsValid) continue;

                    var slotX = invItem.InventPosX;
                    var slotY = invItem.InventPosY;
                    var w = Math.Max(1, invItem.ItemWidth);
                    var h = Math.Max(1, invItem.ItemHeight);

                    for (var x = slotX; x < slotX + w && x < InvColumns; x++)
                    {
                        for (var y = slotY; y < slotY + h && y < InvRows; y++)
                        {
                            if (x >= 0 && y >= 0)
                            {
                                grid[x, y] = true;
                            }
                        }
                    }
                }

                var targetW = Math.Max(1, itemWidth);
                var targetH = Math.Max(1, itemHeight);

                for (var x = 0; x <= InvColumns - targetW; x++)
                {
                    for (var y = 0; y <= InvRows - targetH; y++)
                    {
                        var canFit = true;
                        for (var dx = 0; dx < targetW; dx++)
                        {
                            for (var dy = 0; dy < targetH; dy++)
                            {
                                if (grid[x + dx, y + dy])
                                {
                                    canFit = false;
                                    break;
                                }
                            }
                            if (!canFit) break;
                        }

                        if (canFit) return true;
                    }
                }

                return false;
            }
            catch
            {
                return true;
            }
        }

        public static int GetFreeSlotsCount(GameController gc)
        {
            try
            {
                var ingameUi = gc?.Game?.IngameState?.IngameUi ?? gc?.IngameState?.IngameUi;
                if (ingameUi == null) return 60;

                var invPanel = ingameUi.InventoryPanel;
                if (invPanel == null || !invPanel.IsValid || !invPanel.IsVisible) return 60;

                var invElement = invPanel[ExileCore.Shared.Enums.InventoryIndex.PlayerInventory];
                if (invElement == null || !invElement.IsValid) return 60;

                var items = invElement.VisibleInventoryItems;
                if (items == null) return 60;

                var occupiedSlots = 0;
                foreach (var invItem in items)
                {
                    if (invItem == null || !invItem.IsValid) continue;
                    var w = Math.Max(1, invItem.ItemWidth);
                    var h = Math.Max(1, invItem.ItemHeight);
                    occupiedSlots += w * h;
                }

                return Math.Max(0, 60 - occupiedSlots);
            }
            catch
            {
                return 60;
            }
        }
    }
}
