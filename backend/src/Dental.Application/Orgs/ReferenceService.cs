using Dental.Application.Common;
using Dental.Domain.Audit;
using Dental.Domain.Cash;
using Dental.Domain.Inventory;
using Dental.Domain.Patients;
using Dental.Domain.Scheduling;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Dental.Application.Orgs;

public enum ReferenceKind { LeadSources, CancelReasons, WriteoffReasons, ExpenseCategories }

public sealed record MessageTemplateDto(Guid Id, string Type, MessageChannel Channel, string Text, bool IsActive);
public sealed record MessageTemplateRequest(string Text, bool? IsActive);

public sealed class MessageTemplateRequestValidator : AbstractValidator<MessageTemplateRequest>
{
    public MessageTemplateRequestValidator() => RuleFor(x => x.Text).NotEmpty().MaximumLength(1000);
}

/// <summary>Простые справочники: источники, причины отмен/списаний, категории расходов, шаблоны сообщений.</summary>
public sealed class ReferenceService(IAppDbContext db, TimeProvider clock)
{
    public async Task<IReadOnlyList<NamedRefDto>> ListAsync(ReferenceKind kind, CancellationToken ct) => kind switch
    {
        ReferenceKind.LeadSources => await db.LeadSources.AsNoTracking().NotDeleted().OrderBy(x => x.Name)
            .Select(x => new NamedRefDto(x.Id, x.Name, null)).ToListAsync(ct),
        ReferenceKind.CancelReasons => (await db.CancelReasons.AsNoTracking().NotDeleted().OrderBy(x => x.Name).ToListAsync(ct))
            .Select(x => new NamedRefDto(x.Id, x.Name, x.Type.ToString())).ToList(),
        ReferenceKind.WriteoffReasons => (await db.WriteoffReasons.AsNoTracking().NotDeleted().OrderBy(x => x.Name).ToListAsync(ct))
            .Select(x => new NamedRefDto(x.Id, x.Name, x.Type.ToString())).ToList(),
        ReferenceKind.ExpenseCategories => (await db.ExpenseCategories.AsNoTracking().NotDeleted().OrderBy(x => x.Name).ToListAsync(ct))
            .Select(x => new NamedRefDto(x.Id, x.Name, x.Type.ToString())).ToList(),
        _ => [],
    };

    public async Task<NamedRefDto> CreateAsync(ReferenceKind kind, NamedRefRequest r, CancellationToken ct)
    {
        var name = r.Name.Trim();
        NamedRefDto result;
        switch (kind)
        {
            case ReferenceKind.LeadSources:
                var ls = new LeadSource { Name = name };
                db.LeadSources.Add(ls);
                result = new(ls.Id, ls.Name, null);
                break;
            case ReferenceKind.CancelReasons:
                var cr = new CancelReason { Name = name, Type = ParseEnum(r.Type, CancelReasonType.Cancel) };
                db.CancelReasons.Add(cr);
                result = new(cr.Id, cr.Name, cr.Type.ToString());
                break;
            case ReferenceKind.WriteoffReasons:
                var wr = new WriteoffReason { Name = name, Type = ParseEnum(r.Type, WriteoffReasonType.Other) };
                db.WriteoffReasons.Add(wr);
                result = new(wr.Id, wr.Name, wr.Type.ToString());
                break;
            default:
                var ec = new ExpenseCategory { Name = name, Type = ParseEnum(r.Type, ExpenseCategoryType.Other) };
                db.ExpenseCategories.Add(ec);
                result = new(ec.Id, ec.Name, ec.Type.ToString());
                break;
        }
        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task<NamedRefDto> UpdateAsync(ReferenceKind kind, Guid id, NamedRefRequest r, CancellationToken ct)
    {
        var name = r.Name.Trim();
        NamedRefDto result;
        switch (kind)
        {
            case ReferenceKind.LeadSources:
                var ls = await db.LeadSources.GetOrThrowAsync(id, "Источник", ct);
                ls.Name = name;
                result = new(ls.Id, ls.Name, null);
                break;
            case ReferenceKind.CancelReasons:
                var cr = await db.CancelReasons.GetOrThrowAsync(id, "Причина", ct);
                cr.Name = name;
                cr.Type = ParseEnum(r.Type, cr.Type);
                result = new(cr.Id, cr.Name, cr.Type.ToString());
                break;
            case ReferenceKind.WriteoffReasons:
                var wr = await db.WriteoffReasons.GetOrThrowAsync(id, "Причина", ct);
                wr.Name = name;
                wr.Type = ParseEnum(r.Type, wr.Type);
                result = new(wr.Id, wr.Name, wr.Type.ToString());
                break;
            default:
                var ec = await db.ExpenseCategories.GetOrThrowAsync(id, "Категория", ct);
                ec.Name = name;
                ec.Type = ParseEnum(r.Type, ec.Type);
                result = new(ec.Id, ec.Name, ec.Type.ToString());
                break;
        }
        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task DeleteAsync(ReferenceKind kind, Guid id, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        switch (kind)
        {
            case ReferenceKind.LeadSources: (await db.LeadSources.GetOrThrowAsync(id, "Источник", ct)).DeletedAt = now; break;
            case ReferenceKind.CancelReasons: (await db.CancelReasons.GetOrThrowAsync(id, "Причина", ct)).DeletedAt = now; break;
            case ReferenceKind.WriteoffReasons: (await db.WriteoffReasons.GetOrThrowAsync(id, "Причина", ct)).DeletedAt = now; break;
            default: (await db.ExpenseCategories.GetOrThrowAsync(id, "Категория", ct)).DeletedAt = now; break;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<MessageTemplateDto>> ListTemplatesAsync(CancellationToken ct) =>
        (await db.MessageTemplates.AsNoTracking().OrderBy(t => t.Type).ToListAsync(ct))
        .Select(t => new MessageTemplateDto(t.Id, t.Type, t.Channel, t.Text, t.IsActive)).ToList();

    public async Task<MessageTemplateDto> UpdateTemplateAsync(Guid id, MessageTemplateRequest r, CancellationToken ct)
    {
        var t = await db.MessageTemplates.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Шаблон");
        t.Text = r.Text;
        if (r.IsActive is not null) t.IsActive = r.IsActive.Value;
        await db.SaveChangesAsync(ct);
        return new MessageTemplateDto(t.Id, t.Type, t.Channel, t.Text, t.IsActive);
    }

    private static T ParseEnum<T>(string? value, T fallback) where T : struct, Enum =>
        !string.IsNullOrWhiteSpace(value) && Enum.TryParse<T>(value, true, out var v) ? v : fallback;
}
