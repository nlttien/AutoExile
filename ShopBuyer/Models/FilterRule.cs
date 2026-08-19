using ExileCore.Shared.Enums;

namespace AutoExile.ShopBuyer.Models
{
    public class FilterRule
    {
        public bool Enabled { get; set; } = true;
        public string Name { get; set; } = "Default Rule";

        // Item Identity
        public string BaseNameFilter { get; set; } = string.Empty; // Comma-separated whitelist, e.g. "Amethyst Ring, Heavy Belt"
        public bool MatchNormal { get; set; } = true;
        public bool MatchMagic { get; set; } = true;
        public bool MatchRare { get; set; } = true;
        public bool MatchUnique { get; set; } = true;

        // Item Stats
        public int MinItemLevel { get; set; } = 0;
        public int MinQuality { get; set; } = 0;
        public int MinSockets { get; set; } = 0;
        public int MinLinks { get; set; } = 0;
        public bool RequireRgbSockets { get; set; } = false;

        // Price restrictions
        public bool CheckMaxPrice { get; set; } = false;
        public int MaxGoldCost { get; set; } = 50000;
        public int MaxOrbCost { get; set; } = 10;
    }
}
