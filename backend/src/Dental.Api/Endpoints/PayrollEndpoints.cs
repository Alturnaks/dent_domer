using Dental.Api.Auth;
using Dental.Api.Infrastructure;
using Dental.Application.Payroll;
using Dental.Application.Permissions;
using Dental.Domain.Payroll;
using Microsoft.AspNetCore.Mvc;

namespace Dental.Api.Endpoints;

public static class PayrollEndpoints
{
    public static IEndpointRouteBuilder MapPayrollEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1").WithTags("Payroll").RequireAuthorization();
        string[] view = [Perm.Payroll.ViewAll, Perm.Payroll.Manage];

        g.MapGet("/payroll-schemes", async ([FromQuery(Name = "membership_id")] Guid? membershipId, PayrollService s, CancellationToken ct) =>
                TypedResults.Ok(await s.ListSchemesAsync(membershipId, ct)))
            .RequirePermission(view).WithName("ListPayrollSchemes");
        g.MapPost("/payroll-schemes", async (PayrollSchemeRequest r, PayrollService s, CancellationToken ct) => TypedResults.Ok(await s.SaveSchemeAsync(r, ct)))
            .RequirePermission(Perm.Payroll.Manage).Validate<PayrollSchemeRequest>().WithName("SavePayrollScheme");

        g.MapGet("/payroll-periods", async ([FromQuery(Name = "branch_id")] Guid? branchId, PayrollPeriodStatus? status, PayrollService s, CancellationToken ct) =>
                TypedResults.Ok(await s.ListPeriodsAsync(branchId, status, ct)))
            .RequirePermission(view).WithName("ListPayrollPeriods");
        g.MapPost("/payroll-periods", async (CreatePayrollPeriodRequest r, PayrollService s, CancellationToken ct) => TypedResults.Ok(await s.CreatePeriodAsync(r, ct)))
            .RequirePermission(Perm.Payroll.Manage).Validate<CreatePayrollPeriodRequest>().WithName("CreatePayrollPeriod");
        g.MapGet("/payroll-periods/{id:guid}", async (Guid id, PayrollService s, CancellationToken ct) => TypedResults.Ok(await s.GetPeriodAsync(id, ct)))
            .RequirePermission(view).WithName("GetPayrollPeriod");
        g.MapPost("/payroll-periods/{id:guid}/calculate", async (Guid id, PayrollService s, CancellationToken ct) => TypedResults.Ok(await s.RecalculateAsync(id, ct)))
            .RequirePermission(Perm.Payroll.Manage).WithName("CalculatePayrollPeriod");
        g.MapPost("/payroll-periods/{id:guid}/approve", async (Guid id, PayrollService s, CancellationToken ct) => TypedResults.Ok(await s.ApproveAsync(id, ct)))
            .RequirePermission(Perm.Payroll.Manage).WithName("ApprovePayrollPeriod");
        g.MapPost("/payroll-periods/{id:guid}/pay", async (Guid id, PayrollService s, CancellationToken ct) => TypedResults.Ok(await s.MarkPaidAsync(id, ct)))
            .RequirePermission(Perm.Payroll.Manage).WithName("MarkPayrollPeriodPaid");

        g.MapGet("/payroll-entries/{id:guid}", async (Guid id, PayrollService s, CancellationToken ct) => TypedResults.Ok(await s.GetEntryAsync(id, ct)))
            .RequirePermission([.. view, Perm.Payroll.ViewOwn]).WithName("GetPayrollEntry");
        g.MapPatch("/payroll-entries/{id:guid}", async (Guid id, UpdatePayrollEntryRequest r, PayrollService s, CancellationToken ct) =>
                TypedResults.Ok(await s.UpdateEntryAsync(id, r, ct)))
            .RequirePermission(Perm.Payroll.Manage).Validate<UpdatePayrollEntryRequest>().WithName("UpdatePayrollEntry");

        g.MapGet("/payroll/me", async (PayrollService s, CancellationToken ct) => TypedResults.Ok(await s.MyAsync(ct)))
            .RequirePermission(Perm.Payroll.ViewOwn).WithName("MyPayroll");
        return app;
    }
}
