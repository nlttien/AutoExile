using System;
using System.Collections.Generic;
using System.Linq;
using ExileCore.Shared.Enums;
using AutoExile.ShopBuyer.Models;

namespace AutoExile.ShopBuyer.Services
{
    public static class ItemFilterEngine
    {
        public static bool MatchesRule(ShopItemInfo item, FilterRule rule)
        {
            if (item == null || rule == null || !rule.Enabled) return false;

            // 1. Check Rarity
            switch (item.Rarity)
            {
                case ItemRarity.Normal when !rule.MatchNormal:
                case ItemRarity.Magic when !rule.MatchMagic:
                case ItemRarity.Rare when !rule.MatchRare:
                case ItemRarity.Unique when !rule.MatchUnique:
                    return false;
            }

            // 2. Check Item Level
            if (rule.MinItemLevel > 0 && item.ItemLevel < rule.MinItemLevel)
            {
                return false;
            }

            // 3. Check Quality
            if (rule.MinQuality > 0 && item.Quality < rule.MinQuality)
            {
                return false;
            }

            // 4. Check Sockets
            if (rule.MinSockets > 0 && item.Sockets < rule.MinSockets)
            {
                return false;
            }

            // 5. Check Links
            if (rule.MinLinks > 0 && item.Links < rule.MinLinks)
            {
                return false;
            }

            // 6. Check RGB (Chromatic)
            if (rule.RequireRgbSockets && !item.IsRgb)
            {
                return false;
            }

            // 7. Base Name / Full Name Matching
            if (!string.IsNullOrWhiteSpace(rule.BaseNameFilter))
            {
                var filters = rule.BaseNameFilter.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                var itemName = item.DisplayName ?? string.Empty;
                var itemBase = item.BaseName ?? string.Empty;
                var itemPath = item.ItemPath ?? string.Empty;

                var matches = filters.Any(f =>
                {
                    var trimmed = f.Trim();
                    return itemName.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                           itemBase.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                           itemPath.Contains(trimmed.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) ||
                           itemPath.Contains(trimmed, StringComparison.OrdinalIgnoreCase);
                });

                if (!matches) return false;
            }

            return true;
        }

        public static bool MatchesAnyRule(ShopItemInfo item, IEnumerable<FilterRule> rules)
        {
            if (item == null || rules == null) return false;
            return rules.Any(rule => MatchesRule(item, rule));
        }
    }
}
