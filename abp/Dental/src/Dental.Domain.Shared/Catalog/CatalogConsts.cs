namespace Dental.Catalog;

public static class CatalogConsts
{
    public const int MaxCategoryNameLength = 200;
    public const int MaxServiceCodeLength = 40;
    public const int MaxServiceNameLength = 300;
    public const int MinDurationMin = 5;
    public const int MaxDurationMin = 600;
    public const int MaxPriceListNameLength = 200;
    public const double MinBulkPercent = -90;
    public const double MaxBulkPercent = 500;
    /// <summary>Округление при массовом изменении цен по умолчанию — 100 ₸ (в тиынах).</summary>
    public const long DefaultRoundTo = 10_000;
}
