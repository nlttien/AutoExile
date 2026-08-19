using System.Collections.Generic;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.Shared.Enums;
using SharpDX;
using Vector2 = System.Numerics.Vector2;

namespace AutoExile.ShopBuyer.Models
{
    public class ShopItemInfo
    {
        public string Name { get; set; } = string.Empty;
        public string BaseName { get; set; } = string.Empty;
        public string ItemPath { get; set; } = string.Empty;
        public ItemRarity Rarity { get; set; } = ItemRarity.Normal;
        public int ItemLevel { get; set; } = 1;
        public int Quality { get; set; } = 0;
        public int Sockets { get; set; } = 0;
        public int Links { get; set; } = 0;
        public bool IsRgb { get; set; } = false;

        public CurrencyCost? Cost { get; set; }
        public string CostString { get; set; } = string.Empty;

        public int TabIndex { get; set; } = 0;
        public string TabName { get; set; } = string.Empty;

        public int SlotX { get; set; } = 0;
        public int SlotY { get; set; } = 0;
        public int Width { get; set; } = 1;
        public int Height { get; set; } = 1;

        public SharpDX.RectangleF ScreenRect { get; set; }
        public Vector2 ClickPosition { get; set; }

        public NormalInventoryItem? InventoryItem { get; set; }

        public string DisplayName => string.IsNullOrWhiteSpace(Name) || Name == BaseName
            ? BaseName
            : $"{Name} ({BaseName})";
    }
}
