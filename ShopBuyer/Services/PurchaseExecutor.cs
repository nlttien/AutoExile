using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ExileCore;
using ExileCore.PoEMemory.Elements.InventoryElements;
using AutoExile.ShopBuyer.Adapters;
using AutoExile.ShopBuyer.Models;
using AutoExile.ShopBuyer.Utils;
using Vector2 = System.Numerics.Vector2;

namespace AutoExile.ShopBuyer.Services
{
    public class PurchaseExecutor
    {
        private readonly GameController _gc;
        private readonly ShopAdapterFactory _adapterFactory = new();

        public static readonly List<string> RecentPurchases = new();

        public bool IsRunning { get; set; }
        public bool RequestStop { get; set; }

        public PurchaseExecutor(GameController gc)
        {
            _gc = gc;
        }

        public (int count, List<string> items) ExecutePurchase(BotContext ctx, List<FilterRule> rules, bool scanAllTabs = false)
        {
            if (IsRunning) return (0, new List<string>());

            IsRunning = true;
            RequestStop = false;
            var totalPurchasedCount = 0;
            var purchasedDetails = new List<string>();

            try
            {
                var adapter = _adapterFactory.GetAdapter(_gc);
                if (!adapter.IsShopOpen(_gc))
                {
                    return (0, purchasedDetails);
                }

                var tabCount = scanAllTabs ? adapter.GetTabCount(_gc) : 1;
                var startTab = scanAllTabs ? 0 : adapter.GetCurrentTabIndex(_gc);

                for (var tab = startTab; tab < startTab + tabCount; tab++)
                {
                    if (RequestStop || !adapter.IsShopOpen(_gc)) break;

                    if (scanAllTabs && tabCount > 1)
                    {
                        adapter.SwitchToTab(_gc, tab);
                        Thread.Sleep(200);
                    }

                    var currentItems = adapter.GetAvailableItems(_gc);
                    if (currentItems == null || currentItems.Count == 0) continue;

                    var candidateItems = currentItems
                        .Where(i => i != null && ItemFilterEngine.MatchesAnyRule(i, rules))
                        .OrderBy(i => i.ScreenRect.Top)
                        .ThenBy(i => i.ScreenRect.Left)
                        .ToList();

                    if (candidateItems.Count == 0) continue;

                    foreach (var item in candidateItems)
                    {
                        if (RequestStop || !adapter.IsShopOpen(_gc)) break;

                        // Check inventory space
                        if (!InventorySpaceChecker.HasSpaceForItem(_gc, item.Width, item.Height))
                        {
                            ctx.Log("[AutoBuy] Inventory full — cannot buy more items");
                            RequestStop = true;
                            break;
                        }

                        // Jitter mouse & Ctrl+Click to purchase
                        MouseHelper.MoveMouseWithJitter(item.ScreenRect, 4f);
                        Thread.Sleep(MouseHelper.GetRandomDelay(80, 120));

                        MouseHelper.CtrlLeftClick();
                        totalPurchasedCount++;

                        var displayName = item.DisplayName;
                        purchasedDetails.Add(displayName);
                        RecentPurchases.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Mua thành công: {displayName}");
                        if (RecentPurchases.Count > 50) RecentPurchases.RemoveAt(RecentPurchases.Count - 1);

                        ctx.Log($"[AutoBuy] Bought item: {displayName}");
                        Thread.Sleep(MouseHelper.GetRandomDelay(120, 180));
                    }
                }
            }
            catch (Exception ex)
            {
                ctx.Log($"[AutoBuy] Error during purchase: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
            }

            return (totalPurchasedCount, purchasedDetails);
        }
    }
}
