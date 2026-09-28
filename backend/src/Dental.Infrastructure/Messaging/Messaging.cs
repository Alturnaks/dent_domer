using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using Dental.Application.Auth;
using Dental.Domain.Audit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dental.Infrastructure.Messaging;

/// <summary>Провайдер отправки сообщений пациентам (WhatsApp/SMS). Конкретный провайдер подключается позже.</summary>
public interface IMessageProvider
{
    MessageChannel Channel { get; }
    Task<MessageSendResult> SendAsync(string recipient, string text, CancellationToken ct);
}

public sealed record MessageSendResult(bool Success, string? ProviderMessageId, string? Error);

/// <summary>Dev-провайдер: пишет сообщение в лог. Используется для всех каналов, пока не подключён реальный.</summary>
public sealed class ConsoleMessageProvider(ILogger<ConsoleMessageProvider> logger, MessageChannel channel = MessageChannel.Whatsapp) : IMessageProvider
{
    public MessageChannel Channel => channel;

    public Task<MessageSendResult> SendAsync(string recipient, string text, CancellationToken ct)
    {
        logger.LogInformation("[{Channel}] → {Recipient}: {Text}", channel, MaskPhone(recipient), text);
        return Task.FromResult(new MessageSendResult(true, $"console-{Guid.NewGuid():N}", null));
    }

    internal static string MaskPhone(string phone) => phone.Length <= 4 ? "***" : new string('*', phone.Length - 4) + phone[^4..];
}

/// <summary>Заглушка WhatsApp Business API: интерфейс готов, интеграция — позже (SPEC §18).</summary>
public sealed class WhatsAppProvider(ILogger<WhatsAppProvider> logger) : IMessageProvider
{
    public MessageChannel Channel => MessageChannel.Whatsapp;

    public Task<MessageSendResult> SendAsync(string recipient, string text, CancellationToken ct)
    {
        logger.LogWarning("WhatsApp provider is not configured; message to {Recipient} not sent", ConsoleMessageProvider.MaskPhone(recipient));
        return Task.FromResult(new MessageSendResult(false, null, "WHATSAPP_NOT_CONFIGURED"));
    }
}

/// <summary>Заглушка SMS-провайдера.</summary>
public sealed class SmsProvider(ILogger<SmsProvider> logger) : IMessageProvider
{
    public MessageChannel Channel => MessageChannel.Sms;

    public Task<MessageSendResult> SendAsync(string recipient, string text, CancellationToken ct)
    {
        logger.LogWarning("SMS provider is not configured; message to {Recipient} not sent", ConsoleMessageProvider.MaskPhone(recipient));
        return Task.FromResult(new MessageSendResult(false, null, "SMS_NOT_CONFIGURED"));
    }
}

/// <summary>Отправка email через SMTP (Mailpit в dev).</summary>
public sealed class SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string htmlBody, IReadOnlyList<(string FileName, byte[] Content, string ContentType)>? attachments, CancellationToken ct)
    {
        var o = options.Value;
        using var message = new MailMessage(o.From, to) { Subject = subject, Body = htmlBody, IsBodyHtml = true };
        var streams = new List<MemoryStream>();
        try
        {
            foreach (var (name, content, type) in attachments ?? [])
            {
                var ms = new MemoryStream(content);
                streams.Add(ms);
                message.Attachments.Add(new Attachment(ms, name, type));
            }
            using var client = new SmtpClient(o.Host, o.Port) { EnableSsl = o.EnableSsl, DeliveryMethod = SmtpDeliveryMethod.Network };
            if (!string.IsNullOrEmpty(o.User)) client.Credentials = new NetworkCredential(o.User, o.Password);
            await client.SendMailAsync(message, ct);
            logger.LogInformation("Email '{Subject}' sent to {To}", subject, to);
        }
        finally
        {
            foreach (var s in streams) await s.DisposeAsync();
        }
    }
}

/// <summary>Канал доставки отчётов (email сейчас, Telegram — позже без переделки).</summary>
public interface IReportDeliveryChannel
{
    MessageChannel Channel { get; }
    Task DeliverAsync(Guid userId, string subject, string html, IReadOnlyList<(string FileName, byte[] Content, string ContentType)> files, CancellationToken ct);
}

internal static class ContentTypes
{
    public const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string Pdf = MediaTypeNames.Application.Pdf;
}
