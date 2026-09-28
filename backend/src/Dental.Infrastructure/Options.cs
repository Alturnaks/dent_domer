using System.ComponentModel.DataAnnotations;

namespace Dental.Infrastructure;

public sealed class DatabaseOptions
{
    public const string Section = "Database";

    /// <summary>Соединение приложения (роль без BYPASSRLS).</summary>
    [Required] public string ConnectionString { get; set; } = "";

    /// <summary>Соединение роли-владельца: миграции, seed, системные выборки в обход RLS.</summary>
    [Required] public string OwnerConnectionString { get; set; } = "";
}

public sealed class RedisOptions
{
    public const string Section = "Redis";
    /// <summary>Пусто = in-memory кэш (для тестов и локального запуска без Redis).</summary>
    public string? ConnectionString { get; set; }
}

public sealed class SmtpOptions
{
    public const string Section = "Smtp";
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    public string From { get; set; } = "noreply@dental.local";
    public string? User { get; set; }
    public string? Password { get; set; }
    public bool EnableSsl { get; set; }
}

public sealed class StorageOptions
{
    public const string Section = "Storage";
    public string ServiceUrl { get; set; } = "http://localhost:9000";
    public string PublicUrl { get; set; } = "http://localhost:9000";
    public string Bucket { get; set; } = "dental";
    public string AccessKey { get; set; } = "minioadmin";
    public string SecretKey { get; set; } = "minioadmin";
    /// <summary>Регион хранения ПДн (законодательство РК) — вынесен в конфигурацию.</summary>
    public string Region { get; set; } = "kz-almaty-1";
    public long MaxFileBytes { get; set; } = 20 * 1024 * 1024;
}

public sealed class AppOptions
{
    public const string Section = "App";
    /// <summary>Адрес фронтенда (CORS, ссылки в письмах).</summary>
    public string FrontendUrl { get; set; } = "http://localhost:3000";
}
