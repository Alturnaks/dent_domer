namespace Dental.Domain.Common;

/// <summary>Нарушение бизнес-правила. Код — из ErrorCodes; превращается в единый формат ошибки.</summary>
public class DomainException : Exception
{
    public DomainException(string code, string message, IReadOnlyDictionary<string, object?>? details = null, int status = 422)
        : base(message)
    {
        Code = code;
        Details = details;
        Status = status;
    }

    public DomainException() : this("DOMAIN_ERROR", "Domain error") { }

    public DomainException(string message) : this("DOMAIN_ERROR", message) { }

    public DomainException(string message, Exception inner) : base(message, inner)
    {
        Code = "DOMAIN_ERROR";
        Status = 422;
    }

    public string Code { get; } = "DOMAIN_ERROR";
    public int Status { get; }
    public IReadOnlyDictionary<string, object?>? Details { get; }
}
