using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using ExileCore;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.Shared.Enums;
using AutoExile.ShopBuyer.Adapters;
using AutoExile.ShopBuyer.Models;
using AutoExile.ShopBuyer.Services;
using AutoExile.ShopBuyer.Utils;
using AutoExile.Systems;
using SharpDX;
using Vector2 = System.Numerics.Vector2;

namespace AutoExile.Modes
{
    public enum AutoBuyMarketSource
    {
        InGameShop = 0,
        WebTrade = 1
    }

    public class AutoBuyMode : IBotMode
    {
        public string Name => "AutoBuy";
        public string Status { get; private set; } = "Idle";

        private readonly ShopAdapterFactory _adapterFactory = new();
        private PurchaseExecutor? _purchaseExecutor;
        private TradeBridgeService? _tradeBridge;

        private bool _isPaused;
        private DateTime _lastScanTime = DateTime.MinValue;
        private DateTime _lastBridgePoll = DateTime.MinValue;
        private bool _hasScannedCurrentShop;
        private List<ShopItemInfo> _cachedMatchingItems = new();
        private List<ShopItemInfo> _cachedAllItems = new();

        public TradeBridgeService TradeBridge => _tradeBridge ??= new TradeBridgeService();

        public bool IsPaused
        {
            get => _isPaused;
            set
            {
                _isPaused = value;
                if (_isPaused)
                {
                    TradeBridge.WriteBridgeData("STOPPED");
                }
                else
                {
                    TradeBridge.WriteBridgeData("WAITING_IN_GAME");
                }
            }
        }

        public void OnEnter(BotContext ctx)
        {
            _purchaseExecutor = new PurchaseExecutor(ctx.Game);
            _tradeBridge = new TradeBridgeService();
            _isPaused = false;
            _hasScannedCurrentShop = false;
            _cachedMatchingItems.Clear();
            _cachedAllItems.Clear();
            Status = "Entered AutoBuy Mode";
            ctx.Log("[AutoBuy] Mode activated");
        }

        public void OnExit()
        {
            _cachedMatchingItems.Clear();
            _cachedAllItems.Clear();
            Status = "Exited AutoBuy Mode";
        }

        public void OnEnterZone(BotContext ctx)
        {
            _hasScannedCurrentShop = false;
            _cachedMatchingItems.Clear();
            _cachedAllItems.Clear();

            var currentArea = ctx.Game?.Area?.CurrentArea?.Name ?? "";
            ctx.Log($"[AutoBuy] Entered zone: {currentArea}");
        }

        public void Tick(BotContext ctx)
        {
            var gc = ctx.Game;
            if (gc?.Player == null) return;

            _purchaseExecutor ??= new PurchaseExecutor(gc);
            _tradeBridge ??= new TradeBridgeService();

            // 1. Check Hotkeys
            var settings = ctx.Settings.AutoBuy;
            if (settings.PauseHotkey.PressedOnce())
            {
                IsPaused = !IsPaused;
                ctx.Log($"[AutoBuy] {(IsPaused ? "PAUSED" : "RESUMED")} by hotkey F7");
            }

            if (settings.TestDepositHotkey.PressedOnce())
            {
                ctx.Log("[AutoBuy] Test Deposit (F6) triggered");
                PerformStashDeposit(ctx);
            }

            if (IsPaused)
            {
                Status = "Paused (Press F7 to resume)";
                return;
            }

            var marketSource = (AutoBuyMarketSource)settings.MarketSource.Value;

            if (marketSource == AutoBuyMarketSource.InGameShop)
            {
                TickInGameShop(ctx, gc, settings);
            }
            else
            {
                TickWebTrade(ctx, gc, settings);
            }
        }

        // ── 1. In-Game Shop Mode ──

        private void TickInGameShop(BotContext ctx, GameController gc, BotSettings.AutoBuySettings settings)
        {
            var adapter = _adapterFactory.GetAdapter(gc);
            var isShopOpen = adapter.IsShopOpen(gc);

            if (!isShopOpen)
            {
                _hasScannedCurrentShop = false;
                Status = "Waiting for Shop / NPC dialog to open";
                return;
            }

            // Update item cache for highlight
            if ((DateTime.Now - _lastScanTime).TotalMilliseconds >= 250)
            {
                _lastScanTime = DateTime.Now;
                UpdateShopItemsCache(gc, settings, adapter);
            }

            if (settings.HighlightOnly.Value)
            {
                Status = $"Preview Mode: Found {_cachedMatchingItems.Count} matching items";
                return;
            }

            if (!_hasScannedCurrentShop && _cachedMatchingItems.Count > 0)
            {
                _hasScannedCurrentShop = true;
                Status = $"Buying {_cachedMatchingItems.Count} items in shop...";

                var rules = GetActiveRules(settings);
                var (boughtCount, boughtItems) = _purchaseExecutor!.ExecutePurchase(ctx, rules, settings.ScanAllTabs.Value);

                Status = $"Bought {boughtCount} items from shop";

                // If inventory full, deposit
                if (InventorySpaceChecker.GetFreeSlotsCount(gc) <= 2)
                {
                    PerformStashDeposit(ctx);
                }
            }
        }

        // ── 2. Web Trade Mode ──

        private void TickWebTrade(BotContext ctx, GameController gc, BotSettings.AutoBuySettings settings)
        {
            if ((DateTime.Now - _lastBridgePoll).TotalMilliseconds < 500)
            {
                return;
            }
            _lastBridgePoll = DateTime.Now;

            var bridgeData = _tradeBridge!.ReadBridgeData();
            var bridgeStatus = bridgeData.status;

            var adapter = _adapterFactory.GetAdapter(gc);
            var isShopOpen = adapter.IsShopOpen(gc);

            if (isShopOpen)
            {
                UpdateShopItemsCache(gc, settings, adapter);

                if (_cachedMatchingItems.Count > 0 && !_hasScannedCurrentShop)
                {
                    _hasScannedCurrentShop = true;
                    Status = $"[WebTrade] Buying {_cachedMatchingItems.Count} items in seller shop...";

                    var rules = GetActiveRules(settings);
                    var (boughtCount, boughtItems) = _purchaseExecutor!.ExecutePurchase(ctx, rules, settings.ScanAllTabs.Value);

                    // Check if inventory full
                    if (InventorySpaceChecker.GetFreeSlotsCount(gc) <= 2)
                    {
                        Status = "[WebTrade] Inventory full — returning to Hideout to deposit...";
                        _tradeBridge.WriteBridgeData("DEPOSITING");
                        PerformStashDeposit(ctx);
                    }

                    _tradeBridge.WriteBridgeData("COMPLETED", boughtCount, boughtItems);
                    Status = $"[WebTrade] Finished purchase ({boughtCount} items) — Waiting for next web trade";
                    return;
                }
            }
            else
            {
                _hasScannedCurrentShop = false;
            }

            if (bridgeStatus == "TRAVELING")
            {
                Status = "Web Trade: Traveling to seller Hideout... Waiting for shop open";
            }
            else if (bridgeStatus == "DEPOSITING")
            {
                Status = "Web Trade: Full inventory — Depositing to Stash...";
                PerformStashDeposit(ctx);
                _tradeBridge.WriteBridgeData("WAITING_IN_GAME");
            }
            else
            {
                Status = $"Web Trade: {bridgeStatus} (Ready for next trade)";
            }
        }

        private void UpdateShopItemsCache(GameController gc, BotSettings.AutoBuySettings settings, IShopAdapter adapter)
        {
            try
            {
                _cachedAllItems = adapter.GetAvailableItems(gc) ?? new List<ShopItemInfo>();
                var rules = GetActiveRules(settings);
                _cachedMatchingItems = _cachedAllItems
                    .Where(i => i != null && ItemFilterEngine.MatchesAnyRule(i, rules))
                    .ToList();
            }
            catch
            {
                _cachedAllItems.Clear();
                _cachedMatchingItems.Clear();
            }
        }

        private static List<FilterRule> GetActiveRules(BotSettings.AutoBuySettings settings)
        {
            var rule = new FilterRule
            {
                Enabled = true,
                Name = "AutoBuy Main Rule",
                BaseNameFilter = settings.WhitelistBaseNames.Value ?? "",
                MatchNormal = settings.MatchNormal.Value,
                MatchMagic = settings.MatchMagic.Value,
                MatchRare = settings.MatchRare.Value,
                MatchUnique = settings.MatchUnique.Value,
                MinItemLevel = settings.MinItemLevel.Value,
                MinQuality = settings.MinQuality.Value,
                MinSockets = settings.MinSockets.Value,
                MinLinks = settings.MinLinks.Value,
                RequireRgbSockets = settings.RequireRgbSockets.Value,
                CheckMaxPrice = settings.CheckMaxPrice.Value,
                MaxOrbCost = settings.MaxChaosPrice.Value,
                MaxGoldCost = settings.MaxGoldPrice.Value
            };

            return new List<FilterRule> { rule };
        }

        private void PerformStashDeposit(BotContext ctx)
        {
            try
            {
                var gc = ctx.Game;
                ctx.Log("[AutoBuy] Executing stash deposit...");

                // Close any open windows with Space
                Input.KeyDown(Keys.Space);
                Thread.Sleep(40);
                Input.KeyUp(Keys.Space);
                Thread.Sleep(200);

                // Press F2 to teleport back to hideout if macro bound
                Input.KeyDown(Keys.F2);
                Thread.Sleep(50);
                Input.KeyUp(Keys.F2);
                Thread.Sleep(1500);

                // Execute stash deposit via StashSystem
                ctx.Stash.Tick(ctx.Game, ctx.Navigation);
            }
            catch (Exception ex)
            {
                ctx.Log($"[AutoBuy] Deposit error: {ex.Message}");
            }
        }

        public void Render(BotContext ctx)
        {
            var gc = ctx.Game;
            var g = ctx.Graphics;
            if (gc?.Player == null || g == null) return;

            var settings = ctx.Settings.AutoBuy;

            // Highlight matching shop items
            if (_cachedMatchingItems.Count > 0)
            {
                foreach (var item in _cachedMatchingItems)
                {
                    if (item.ScreenRect.Width <= 0 || item.ScreenRect.Height <= 0) continue;

                    g.DrawFrame(item.ScreenRect, SharpDX.Color.LimeGreen, 2);
                    g.DrawText(item.DisplayName, new Vector2(item.ScreenRect.Left, item.ScreenRect.Top - 14), SharpDX.Color.Yellow);
                }
            }

            // HUD Overlay
            float hudX = 20, hudY = 320, lineH = 18;
            var modeName = ((AutoBuyMarketSource)settings.MarketSource.Value).ToString();
            g.DrawText($"[AutoBuy: {modeName}] {(IsPaused ? "PAUSED" : "ACTIVE")}", new Vector2(hudX, hudY), IsPaused ? SharpDX.Color.OrangeRed : SharpDX.Color.LimeGreen);
            hudY += lineH;

            g.DrawText($"Status: {Status}", new Vector2(hudX, hudY), SharpDX.Color.White);
            hudY += lineH;

            var freeSlots = InventorySpaceChecker.GetFreeSlotsCount(gc);
            g.DrawText($"Inventory Free: {freeSlots} / 60 slots", new Vector2(hudX, hudY), freeSlots < 6 ? SharpDX.Color.Yellow : SharpDX.Color.Gray);
            hudY += lineH;

            if (PurchaseExecutor.RecentPurchases.Count > 0)
            {
                g.DrawText($"Last item: {PurchaseExecutor.RecentPurchases[0]}", new Vector2(hudX, hudY), SharpDX.Color.Cyan);
            }
        }
    }
}
