using System;

namespace Dental.Branches;

/// <summary>
/// Рабочий день филиала. DayOfWeek: 0 = воскресенье … 6 = суббота (как System.DayOfWeek).
/// Время — локальное время организации (настройка Dental.Org.Timezone).
/// Используется и как value object в домене (сериализуется в jsonb), и как DTO.
/// </summary>
public class WorkingDay
{
    public int DayOfWeek { get; set; }
    public bool IsWorking { get; set; }
    public TimeOnly Open { get; set; }
    public TimeOnly Close { get; set; }
}
