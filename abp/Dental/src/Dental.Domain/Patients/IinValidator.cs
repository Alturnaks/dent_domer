using System.Linq;

namespace Dental.Patients;

/// <summary>ИИН РК: 12 цифр, контрольная сумма (две последовательности весов).</summary>
public static class IinValidator
{
    private static readonly int[] W1 = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];
    private static readonly int[] W2 = [3, 4, 5, 6, 7, 8, 9, 10, 11, 1, 2];

    public static bool IsValid(string? iin)
    {
        if (string.IsNullOrWhiteSpace(iin) || iin.Length != 12 || !iin.All(char.IsDigit))
        {
            return false;
        }
        var sum = CheckDigit(iin);
        return sum != 10 && sum == iin[11] - '0';
    }

    /// <summary>Контрольная цифра по первым 11 цифрам (10 — недопустимый ИИН).</summary>
    public static int CheckDigit(string first11)
    {
        var d = first11.Take(11).Select(c => c - '0').ToArray();
        var sum = Enumerable.Range(0, 11).Sum(i => d[i] * W1[i]) % 11;
        if (sum == 10)
        {
            sum = Enumerable.Range(0, 11).Sum(i => d[i] * W2[i]) % 11;
        }
        return sum;
    }
}
