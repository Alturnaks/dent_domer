namespace Dental.Staff;

/// <summary>Должность сотрудника (SPEC §5). Кладовщик/кассир упразднены — их права у администратора.</summary>
public enum StaffPosition
{
    Owner = 0,
    SeniorAdmin = 1,
    Admin = 2,
    Doctor = 3,
    Assistant = 4,
    Other = 5
}
