namespace Hlight.ResourceBag.Samples.EnumKeyed
{
    /// <summary>Domain enum — one value per currency. ToString() becomes the persistence key.</summary>
    public enum CurrencyId
    {
        Coin,
        Gem,
        Heart,
    }
}
