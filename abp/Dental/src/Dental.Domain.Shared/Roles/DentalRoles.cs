namespace Dental.Roles;

/// <summary>
/// Предустановленные роли арендатора (SPEC §5.4). Имя IdentityRole = русское название
/// (ABP-роль "admin" занята встроенным администратором арендатора, поэтому коды не используются как имена).
/// </summary>
public static class DentalRoles
{
    public const string Owner = "Владелец";
    public const string SeniorAdmin = "Старший администратор";
    public const string Admin = "Администратор";
    public const string Doctor = "Врач";

    /// <summary>Коды пресетов из старой системы → имя роли.</summary>
    public static string? FromLegacyCode(string code) => code switch
    {
        "owner" => Owner,
        "senior_admin" => SeniorAdmin,
        "admin" => Admin,
        "doctor" => Doctor,
        _ => null
    };
}
