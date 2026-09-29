using Dental.Api.Auth;
using Dental.Api.Infrastructure;
using Dental.Application.Common;
using Dental.Application.Orgs;
using Dental.Application.Schedule;
using Dental.Application.Permissions;
using Dental.Domain.Organizations;
using Microsoft.AspNetCore.Mvc;

namespace Dental.Api.Endpoints;

public static class OrgEndpoints
{
    public static IEndpointRouteBuilder MapOrgEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1").WithTags("Org").RequireAuthorization();

        g.MapGet("/org", async (OrgService s, CancellationToken ct) => TypedResults.Ok(await s.GetOrgAsync(ct))).WithName("GetOrg");
        g.MapPatch("/org", async (UpdateOrgRequest r, OrgService s, CancellationToken ct) => TypedResults.Ok(await s.UpdateOrgAsync(r, ct)))
            .RequirePermission(Perm.Org.SettingsManage).WithName("UpdateOrg");

        g.MapGet("/branches", async ([FromQuery(Name = "include_inactive")] bool? includeInactive, OrgService s, CancellationToken ct) =>
            TypedResults.Ok(await s.ListBranchesAsync(includeInactive ?? false, ct))).WithName("ListBranches");
        g.MapPost("/branches", async (BranchRequest r, OrgService s, CancellationToken ct) => TypedResults.Ok(await s.CreateBranchAsync(r, ct)))
            .RequirePermission(Perm.Org.BranchesManage).Validate<BranchRequest>().WithName("CreateBranch");
        g.MapGet("/branches/{id:guid}", async (Guid id, OrgService s, CancellationToken ct) => TypedResults.Ok(await s.GetBranchAsync(id, ct))).WithName("GetBranch");
        g.MapPatch("/branches/{id:guid}", async (Guid id, BranchRequest r, OrgService s, CancellationToken ct) => TypedResults.Ok(await s.UpdateBranchAsync(id, r, ct)))
            .RequirePermission(Perm.Org.BranchesManage).Validate<BranchRequest>().WithName("UpdateBranch");

        g.MapGet("/branches/{id:guid}/rooms", async (Guid id, OrgService s, CancellationToken ct) => TypedResults.Ok(await s.ListRoomsAsync(id, ct))).WithName("ListRooms");
        g.MapPost("/branches/{id:guid}/rooms", async (Guid id, RoomRequest r, OrgService s, CancellationToken ct) => TypedResults.Ok(await s.CreateRoomAsync(id, r, ct)))
            .RequirePermission(Perm.Org.BranchesManage).WithName("CreateRoom");
        g.MapGet("/branches/{id:guid}/chairs", async (Guid id, OrgService s, CancellationToken ct) => TypedResults.Ok(await s.ListChairsAsync(id, ct))).WithName("ListBranchChairs");
        g.MapPost("/branches/{id:guid}/chairs", async (Guid id, ChairRequest r, OrgService s, CancellationToken ct) => TypedResults.Ok(await s.CreateChairAsync(id, r, ct)))
            .RequirePermission(Perm.Org.BranchesManage).WithName("CreateChair");
        g.MapGet("/chairs", async ([FromQuery(Name = "branch_id")] Guid? branchId, OrgService s, CancellationToken ct) => TypedResults.Ok(await s.ListChairsAsync(branchId, ct))).WithName("ListChairs");
        g.MapPatch("/chairs/{id:guid}", async (Guid id, ChairRequest r, OrgService s, CancellationToken ct) => TypedResults.Ok(await s.UpdateChairAsync(id, r, ct)))
            .RequirePermission(Perm.Org.BranchesManage).WithName("UpdateChair");

        // Сотрудники
        g.MapGet("/staff", async ([FromQuery(Name = "include_fired")] bool? includeFired, StaffPosition? position, [FromQuery(Name = "branch_id")] Guid? branchId, OrgService s, CancellationToken ct) =>
            TypedResults.Ok(await s.ListStaffAsync(includeFired ?? false, position, branchId, ct)))
            .RequirePermission(Perm.Org.StaffManage, Perm.Payroll.Manage, Perm.Payroll.ViewAll).WithName("ListStaff");
        g.MapGet("/doctors", async ([FromQuery(Name = "branch_id")] Guid? branchId, OrgService s, CancellationToken ct) => TypedResults.Ok(await s.ListDoctorsAsync(branchId, ct)))
            .WithName("ListDoctors");
        g.MapPost("/staff", async (CreateStaffRequest r, OrgService s, CancellationToken ct) => TypedResults.Ok(await s.CreateStaffAsync(r, ct)))
            .RequirePermission(Perm.Org.StaffManage).Validate<CreateStaffRequest>().WithName("CreateStaff");
        g.MapGet("/staff/{id:guid}", async (Guid id, OrgService s, CancellationToken ct) => TypedResults.Ok(await s.GetStaffAsync(id, ct)))
            .RequirePermission(Perm.Org.StaffManage).WithName("GetStaff");
        g.MapPatch("/staff/{id:guid}", async (Guid id, UpdateStaffRequest r, OrgService s, CancellationToken ct) => TypedResults.Ok(await s.UpdateStaffAsync(id, r, ct)))
            .RequirePermission(Perm.Org.StaffManage).WithName("UpdateStaff");
        g.MapPost("/staff/{id:guid}/fire", async (Guid id, [FromQuery(Name = "on_conflict")] AffectedAppointmentsAction? onConflict, OrgService s, CancellationToken ct) =>
                TypedResults.Ok(await s.FireStaffAsync(id, onConflict, ct)))
            .RequirePermission(Perm.Org.StaffManage).WithName("FireStaff");
        g.MapGet("/staff/role-options", async (OrgService s, CancellationToken ct) =>
                TypedResults.Ok((await s.ListRolesAsync(ct)).Select(r => new NamedRefDto(r.Id, r.Name, r.Code)).ToList()))
            .RequirePermission(Perm.Org.StaffManage, Perm.Org.RolesManage).WithName("RoleOptions");

        // Роли
        g.MapGet("/roles", async (OrgService s, CancellationToken ct) => TypedResults.Ok(await s.ListRolesAsync(ct)))
            .RequirePermission(Perm.Org.RolesManage).WithName("ListRoles");
        g.MapGet("/roles/permissions", () => TypedResults.Ok(OrgService.PermissionCatalog()))
            .RequirePermission(Perm.Org.RolesManage).WithName("PermissionCatalog");
        g.MapPost("/roles", async (RoleRequest r, OrgService s, CancellationToken ct) => TypedResults.Ok(await s.CreateRoleAsync(r, ct)))
            .RequirePermission(Perm.Org.RolesManage).Validate<RoleRequest>().WithName("CreateRole");
        g.MapGet("/roles/{id:guid}", async (Guid id, OrgService s, CancellationToken ct) => TypedResults.Ok(await s.GetRoleAsync(id, ct)))
            .RequirePermission(Perm.Org.RolesManage).WithName("GetRole");
        g.MapPatch("/roles/{id:guid}", async (Guid id, RoleRequest r, OrgService s, CancellationToken ct) => TypedResults.Ok(await s.UpdateRoleAsync(id, r, ct)))
            .RequirePermission(Perm.Org.RolesManage).Validate<RoleRequest>().WithName("UpdateRole");
        g.MapDelete("/roles/{id:guid}", async (Guid id, OrgService s, CancellationToken ct) => { await s.DeleteRoleAsync(id, ct); return TypedResults.NoContent(); })
            .RequirePermission(Perm.Org.RolesManage).WithName("DeleteRole");

        // Подтверждения
        string[] approvalPerms = [Perm.Catalog.DiscountsApply, Perm.Inventory.Writeoff, Perm.Cash.PaymentRefund, Perm.Visits.EditClosed, Perm.Purchase.OrderApprove, Perm.Payroll.Manage];
        g.MapGet("/approvals", async (ApprovalStatus? status, ApprovalService s, CancellationToken ct) => TypedResults.Ok(await s.ListAsync(status, ct)))
            .RequirePermission(approvalPerms).WithName("ListApprovals");
        g.MapPost("/approvals/{id:guid}/approve", async (Guid id, ApprovalDecisionRequest r, ApprovalService s, CancellationToken ct) => TypedResults.Ok(await s.ApproveAsync(id, r, ct)))
            .RequirePermission(approvalPerms).WithName("ApproveRequest");
        g.MapPost("/approvals/{id:guid}/reject", async (Guid id, ApprovalDecisionRequest r, ApprovalService s, CancellationToken ct) => TypedResults.Ok(await s.RejectAsync(id, r, ct)))
            .RequirePermission(approvalPerms).WithName("RejectRequest");

        // Кассы (справочник)
        g.MapGet("/cash-registers", async ([FromQuery(Name = "branch_id")] Guid? branchId, OrgService s, CancellationToken ct) => TypedResults.Ok(await s.ListCashRegistersAsync(branchId, ct)))
            .WithName("ListCashRegisters");
        g.MapPost("/cash-registers", async (CashRegisterRequest r, OrgService s, CancellationToken ct) => TypedResults.Ok(await s.CreateCashRegisterAsync(r, ct)))
            .RequirePermission(Perm.Org.BranchesManage, Perm.Org.SettingsManage).WithName("CreateCashRegister");

        // Справочники
        MapReference(g, "/lead-sources", ReferenceKind.LeadSources, "LeadSources", Perm.Patients.Edit, Perm.Org.SettingsManage);
        MapReference(g, "/cancel-reasons", ReferenceKind.CancelReasons, "CancelReasons", Perm.Org.SettingsManage);
        MapReference(g, "/writeoff-reasons", ReferenceKind.WriteoffReasons, "WriteoffReasons", Perm.Org.SettingsManage, Perm.Inventory.ItemsManage);
        MapReference(g, "/expense-categories", ReferenceKind.ExpenseCategories, "ExpenseCategories", Perm.Org.SettingsManage, Perm.Cash.ExpenseCreate);

        g.MapGet("/message-templates", async (ReferenceService s, CancellationToken ct) => TypedResults.Ok(await s.ListTemplatesAsync(ct)))
            .RequirePermission(Perm.Org.SettingsManage).WithName("ListMessageTemplates");
        g.MapPatch("/message-templates/{id:guid}", async (Guid id, MessageTemplateRequest r, ReferenceService s, CancellationToken ct) => TypedResults.Ok(await s.UpdateTemplateAsync(id, r, ct)))
            .RequirePermission(Perm.Org.SettingsManage).Validate<MessageTemplateRequest>().WithName("UpdateMessageTemplate");

        return app;
    }

    private static void MapReference(RouteGroupBuilder g, string path, ReferenceKind kind, string name, params string[] writePerms)
    {
        g.MapGet(path, async (ReferenceService s, CancellationToken ct) => TypedResults.Ok(await s.ListAsync(kind, ct))).WithName("List" + name);
        g.MapPost(path, async (NamedRefRequest r, ReferenceService s, CancellationToken ct) => TypedResults.Ok(await s.CreateAsync(kind, r, ct)))
            .RequirePermission(writePerms).Validate<NamedRefRequest>().WithName("Create" + name);
        g.MapPatch(path + "/{id:guid}", async (Guid id, NamedRefRequest r, ReferenceService s, CancellationToken ct) => TypedResults.Ok(await s.UpdateAsync(kind, id, r, ct)))
            .RequirePermission(writePerms).Validate<NamedRefRequest>().WithName("Update" + name);
        g.MapDelete(path + "/{id:guid}", async (Guid id, ReferenceService s, CancellationToken ct) => { await s.DeleteAsync(kind, id, ct); return TypedResults.NoContent(); })
            .RequirePermission(writePerms).WithName("Delete" + name);
    }
}
