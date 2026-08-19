using ExileCore;

namespace AutoExile.ShopBuyer.Adapters
{
    public class ShopAdapterFactory
    {
        private readonly Poe1ShopAdapter _poe1Adapter = new();

        public IShopAdapter GetAdapter(GameController gc)
        {
            return _poe1Adapter;
        }
    }
}
