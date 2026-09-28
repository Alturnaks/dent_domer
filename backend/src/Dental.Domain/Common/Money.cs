using System.Globalization;

namespace Dental.Domain.Common;

/// <summary>
/// Денежная сумма в минимальных единицах (тиынах). Никаких double/float.
/// </summary>
public readonly record struct Money(long Minor, string Currency = Money.DefaultCurrency) : IComparable<Money>
{
    public const string DefaultCurrency = "KZT";

    public static Money Zero(string currency = DefaultCurrency) => new(0, currency);

    public static Money FromMajor(decimal major, string currency = DefaultCurrency) =>
        new((long)decimal.Round(major * 100m, 0, MidpointRounding.AwayFromZero), currency);

    public decimal Major => Minor / 100m;

    public bool IsZero => Minor == 0;
    public bool IsNegative => Minor < 0;
    public bool IsPositive => Minor > 0;

    public static Money operator +(Money a, Money b) { EnsureSame(a, b); return new(checked(a.Minor + b.Minor), a.Currency); }
    public static Money operator -(Money a, Money b) { EnsureSame(a, b); return new(checked(a.Minor - b.Minor), a.Currency); }
    public static Money operator -(Money a) => new(-a.Minor, a.Currency);
    public static bool operator <(Money a, Money b) { EnsureSame(a, b); return a.Minor < b.Minor; }
    public static bool operator >(Money a, Money b) { EnsureSame(a, b); return a.Minor > b.Minor; }
    public static bool operator <=(Money a, Money b) { EnsureSame(a, b); return a.Minor <= b.Minor; }
    public static bool operator >=(Money a, Money b) { EnsureSame(a, b); return a.Minor >= b.Minor; }

    /// <summary>Умножение на количество с коммерческим округлением до тиына.</summary>
    public Money Multiply(decimal factor) =>
        new((long)decimal.Round(Minor * factor, 0, MidpointRounding.AwayFromZero), Currency);

    /// <summary>Процент от суммы (например, скидка 12.5%).</summary>
    public Money Percent(decimal percent) => Multiply(percent / 100m);

    public Money Add(Money other) => this + other;
    public Money Subtract(Money other) => this - other;
    public Money Negate() => -this;

    public int CompareTo(Money other)
    {
        EnsureSame(this, other);
        return Minor.CompareTo(other.Minor);
    }

    private static void EnsureSame(Money a, Money b)
    {
        if (!string.Equals(a.Currency, b.Currency, StringComparison.Ordinal))
        {
            throw new DomainException("CURRENCY_MISMATCH", $"Нельзя складывать {a.Currency} и {b.Currency}");
        }
    }

    /// <summary>Формат «12 500 ₸».</summary>
    public override string ToString()
    {
        var nfi = new NumberFormatInfo { NumberGroupSeparator = " ", NumberDecimalSeparator = ",", NumberGroupSizes = [3] };
        var format = Minor % 100 == 0 ? "#,0" : "#,0.00";
        var symbol = Currency == DefaultCurrency ? "₸" : Currency;
        return $"{Major.ToString(format, nfi)} {symbol}";
    }
}
