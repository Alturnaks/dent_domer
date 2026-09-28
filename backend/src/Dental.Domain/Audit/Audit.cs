using Dental.Domain.Common;

namespace Dental.Domain.Audit;

/// <summary>Журнал аудита (партиционирован по месяцам, PK = (id, created_at)).</summary>
public class AuditLog : ITenantEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OrganizationId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? BranchId { get; set; }
    public string EntityType { get; set; } = "";
    public Guid? EntityId { get; set; }
    public string Action { get; set; } = "";
    /// <summary>JSON: { field: { old, new } }.</summary>
    public string? Diff { get; set; }
    public string? Reason { get; set; }
    public string? Ip { get; set; }
    public string? UserAgent { get; set; }
    public bool IsSuspicious { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class Notification : TenantEntity
{
    public Guid UserId { get; set; }
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Body { get; set; }
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}

public enum MessageChannel { Whatsapp, Sms, Email, Telegram }

[Audited]
public class MessageTemplate : TenantEntity
{
    /// <summary>reminder_24h, reminder_2h, recall_6m, birthday.</summary>
    public string Type { get; set; } = "";
    public MessageChannel Channel { get; set; }
    public string Text { get; set; } = "";
    public bool IsActive { get; set; } = true;

    public string Render(IReadOnlyDictionary<string, string> vars)
    {
        var text = Text;
        foreach (var (k, v) in vars) text = text.Replace("{" + k + "}", v, StringComparison.Ordinal);
        return text;
    }
}

public enum OutgoingMessageStatus { Pending, Sent, Failed }

public class OutgoingMessage : TenantEntity
{
    public Guid? PatientId { get; set; }
    public Guid? AppointmentId { get; set; }
    public MessageChannel Channel { get; set; }
    public string? Recipient { get; set; }
    public string Text { get; set; } = "";
    public OutgoingMessageStatus Status { get; set; }
    public string? ProviderMessageId { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string? Error { get; set; }
    public string? Kind { get; set; }
}

[Audited]
public class ReportSubscription : TenantEntity
{
    public Guid UserId { get; set; }
    public string ReportCode { get; set; } = "";
    /// <summary>JSON параметров отчёта.</summary>
    public string Params { get; set; } = "{}";
    public string ScheduleCron { get; set; } = "0 21 * * *";
    public MessageChannel Channel { get; set; } = MessageChannel.Email;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastSentAt { get; set; }
}

/// <summary>Сохранённый фильтр отчёта.</summary>
public class SavedReportFilter : TenantEntity
{
    public Guid UserId { get; set; }
    public string ReportCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string Params { get; set; } = "{}";
}

/// <summary>Outbox: события, которые должны обработаться после коммита.</summary>
public class OutboxMessage : ITenantEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OrganizationId { get; set; }
    public string Type { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? Error { get; set; }
}

/// <summary>Идемпотентность POST-запросов (TTL 24 ч).</summary>
public class IdempotencyKey : ITenantEntity
{
    public Guid OrganizationId { get; set; }
    public string Key { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public int StatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
