namespace AutoExile.ShopBuyer.Models
{
    public class CurrencyCost
    {
        public string CurrencyName { get; set; } = string.Empty;
        public string CurrencyType
        {
            get => CurrencyName;
            set => CurrencyName = value;
        }
        public int Amount { get; set; } = 0;
        public int GoldAmount { get; set; } = 0;
        public bool IsGold { get; set; } = false;

        public override string ToString()
        {
            var main = IsGold ? $"{Amount:N0} Gold" : $"{Amount}x {CurrencyName}";
            return GoldAmount > 0 ? $"{main}, {GoldAmount:N0} Gold" : main;
        }
    }
}
