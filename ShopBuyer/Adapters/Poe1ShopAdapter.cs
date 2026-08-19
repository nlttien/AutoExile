using System;
using System.Collections.Generic;
using System.Linq;
using ExileCore;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.Shared.Enums;
using AutoExile.ShopBuyer.Models;
using AutoExile.ShopBuyer.Utils;
using Vector2 = System.Numerics.Vector2;

namespace AutoExile.ShopBuyer.Adapters
{
    public class Poe1ShopAdapter : IShopAdapter
    {
        public string AdapterName => "Path of Exile 1";

        public bool IsShopOpen(GameController gc)
        {
            try
            {
                if (gc == null) return false;
                var ingameUi = gc.IngameState?.IngameUi ?? gc.Game?.IngameState?.IngameUi;
                if (ingameUi == null) return false;

                var purchaseWindow = ingameUi.PurchaseWindow;
                if (purchaseWindow != null && purchaseWindow.IsValid && purchaseWindow.IsVisible)
                    return true;

                var purchaseHideout = ingameUi.PurchaseWindowHideout;
                if (purchaseHideout != null && purchaseHideout.IsValid && purchaseHideout.IsVisible)
                    return true;

                var npcDialog = ingameUi.NpcDialog;
                if (npcDialog != null && npcDialog.IsValid && npcDialog.IsVisible)
                    return true;

                var sellWindow = ingameUi.SellWindow;
                if (sellWindow != null && sellWindow.IsValid && sellWindow.IsVisible)
                    return true;
            }
            catch { }

            return false;
        }

        public List<ShopItemInfo> GetAvailableItems(GameController gc)
        {
            var result = new List<ShopItemInfo>();
            if (gc == null) return result;

            try
            {
                var ingameUi = gc.IngameState?.IngameUi ?? gc.Game?.IngameState?.IngameUi;
                if (ingameUi == null) return result;

                var purchaseWindow = (ingameUi.PurchaseWindow?.IsVisible == true ? ingameUi.PurchaseWindow : ingameUi.PurchaseWindowHideout);
                if (purchaseWindow == null || !purchaseWindow.IsValid || !purchaseWindow.IsVisible) return result;

                var tabContainer = purchaseWindow.TabContainer;
                IList<NormalInventoryItem>? items = null;

                if (tabContainer != null && tabContainer.IsValid)
                {
                    var visibleStash = tabContainer.VisibleStash;
                    if (visibleStash != null && visibleStash.IsValid)
                    {
                        items = visibleStash.VisibleInventoryItems;
                    }
                }

                if (items == null) return result;

                var currentTabIndex = GetCurrentTabIndex(gc);

                foreach (var invItem in items)
                {
                    if (invItem == null || !invItem.IsValid || !invItem.IsVisible) continue;

                    var clientRect = invItem.GetClientRect();
                    if (clientRect.Width <= 0 || clientRect.Height <= 0) continue;

                    var itemEntity = invItem.Item;
                    var itemInfo = new ShopItemInfo
                    {
                        InventoryItem = invItem,
                        ScreenRect = clientRect,
                        ClickPosition = new Vector2(clientRect.Center.X, clientRect.Center.Y),
                        TabIndex = currentTabIndex,
                        Width = Math.Max(1, invItem.ItemWidth),
                        Height = Math.Max(1, invItem.ItemHeight)
                    };

                    if (itemEntity != null && itemEntity.IsValid)
                    {
                        itemInfo.ItemPath = itemEntity.Path ?? string.Empty;

                        var baseItemType = gc.Files?.BaseItemTypes?.Translate(itemEntity.Path);
                        if (baseItemType != null)
                        {
                            itemInfo.BaseName = baseItemType.BaseName ?? string.Empty;
                        }

                        var mods = itemEntity.GetComponent<ExileCore.PoEMemory.Components.Mods>();
                        if (mods != null)
                        {
                            itemInfo.ItemLevel = mods.ItemLevel;
                            itemInfo.Rarity = mods.ItemRarity;
                            itemInfo.Name = !string.IsNullOrWhiteSpace(mods.UniqueName) ? mods.UniqueName : itemInfo.BaseName;
                        }
                        else
                        {
                            itemInfo.Name = itemInfo.BaseName;
                        }

                        var qualityComp = itemEntity.GetComponent<ExileCore.PoEMemory.Components.Quality>();
                        if (qualityComp != null)
                        {
                            itemInfo.Quality = qualityComp.ItemQuality;
                        }

                        var socketsComp = itemEntity.GetComponent<ExileCore.PoEMemory.Components.Sockets>();
                        if (socketsComp != null)
                        {
                            itemInfo.Sockets = socketsComp.NumberOfSockets;
                            itemInfo.Links = socketsComp.LargestLinkSize;
                            itemInfo.IsRgb = socketsComp.IsRGB;
                        }
                    }

                    result.Add(itemInfo);
                }
            }
            catch { }

            return result;
        }

        public int GetTabCount(GameController gc)
        {
            try
            {
                if (gc == null) return 1;
                var ingameUi = gc.IngameState?.IngameUi ?? gc.Game?.IngameState?.IngameUi;
                if (ingameUi == null) return 1;

                var purchaseWindow = (ingameUi.PurchaseWindow?.IsVisible == true ? ingameUi.PurchaseWindow : ingameUi.PurchaseWindowHideout);
                if (purchaseWindow == null || !purchaseWindow.IsValid || !purchaseWindow.IsVisible) return 1;

                var tabList = purchaseWindow.TabContainer?.TabSwitchBar;
                if (tabList != null && tabList.IsValid && tabList.Children != null && tabList.Children.Count > 0)
                {
                    return tabList.Children.Count;
                }

                return 1;
            }
            catch
            {
                return 1;
            }
        }

        public int GetCurrentTabIndex(GameController gc)
        {
            return 0;
        }

        public bool SwitchToTab(GameController gc, int tabIndex)
        {
            try
            {
                if (gc == null) return false;
                var ingameUi = gc.IngameState?.IngameUi ?? gc.Game?.IngameState?.IngameUi;
                if (ingameUi == null) return false;

                var purchaseWindow = (ingameUi.PurchaseWindow?.IsVisible == true ? ingameUi.PurchaseWindow : ingameUi.PurchaseWindowHideout);
                if (purchaseWindow == null || !purchaseWindow.IsValid || !purchaseWindow.IsVisible) return false;

                var tabList = purchaseWindow.TabContainer?.TabSwitchBar;
                if (tabList != null && tabList.IsValid && tabList.Children != null && tabIndex < tabList.Children.Count)
                {
                    var targetTabButton = tabList.Children[tabIndex];
                    if (targetTabButton != null && targetTabButton.IsValid)
                    {
                        var rect = targetTabButton.GetClientRect();
                        MouseHelper.MoveMouseWithJitter(rect);
                        MouseHelper.LeftClick();
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }
    }
}
