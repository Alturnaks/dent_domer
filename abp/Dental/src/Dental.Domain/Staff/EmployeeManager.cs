using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Volo.Abp.Options;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Identity;

namespace Dental.Staff;

/// <summary>Создание/изменение сотрудника вместе с его IdentityUser и ролью.</summary>
public class EmployeeManager : DomainService
{
    private readonly IdentityUserManager _userManager;
    private readonly IRepository<Employee, Guid> _employeeRepository;
    private readonly IOptions<IdentityOptions> _identityOptions;

    public EmployeeManager(IdentityUserManager userManager, IRepository<Employee, Guid> employeeRepository, IOptions<IdentityOptions> identityOptions)
    {
        _identityOptions = identityOptions;
        _userManager = userManager;
        _employeeRepository = employeeRepository;
    }

    public async Task<Employee> CreateAsync(
        string email,
        string password,
        string fullName,
        string? phone,
        StaffPosition position,
        IEnumerable<string> roleNames,
        bool allBranches,
        IEnumerable<Guid>? branchIds,
        string? specialty = null,
        string? color = null)
    {
        // Политика паролей ABP берётся из настроек арендатора — применить перед созданием пользователя.
        await _identityOptions.SetAsync();
        email = email.Trim().ToLowerInvariant();
        var user = await _userManager.FindByEmailAsync(email);
        if (user != null && await _employeeRepository.AnyAsync(e => e.UserId == user.Id))
        {
            throw new BusinessException(DentalDomainErrorCodes.EmployeeAlreadyExists).WithData("email", email);
        }

        if (user == null)
        {
            user = new IdentityUser(GuidGenerator.Create(), email, email, CurrentTenant.Id);
            ApplyName(user, fullName);
            (await _userManager.CreateAsync(user, password)).CheckErrors();
        }

        if (!phone.IsNullOrWhiteSpace())
        {
            (await _userManager.SetPhoneNumberAsync(user, phone)).CheckErrors();
        }
        (await _userManager.SetRolesAsync(user, roleNames)).CheckErrors();

        var employee = new Employee(GuidGenerator.Create(), CurrentTenant.Id, user.Id, fullName, position)
        {
            Phone = phone,
            Specialty = specialty,
            Color = color,
        };
        employee.SetBranches(allBranches, branchIds);
        return await _employeeRepository.InsertAsync(employee, autoSave: true);
    }

    public async Task UpdateUserAsync(Employee employee, string fullName, string? phone, IEnumerable<string>? roleNames)
    {
        employee.SetFullName(fullName);
        employee.Phone = phone;
        var user = await _userManager.GetByIdAsync(employee.UserId);
        ApplyName(user, fullName);
        (await _userManager.SetPhoneNumberAsync(user, phone)).CheckErrors();
        (await _userManager.UpdateAsync(user)).CheckErrors();
        if (roleNames != null)
        {
            (await _userManager.SetRolesAsync(user, roleNames)).CheckErrors();
        }
    }

    /// <summary>Увольнение: профиль неактивен, вход заблокирован (IdentityUser.IsActive = false).</summary>
    public async Task FireAsync(Employee employee, DateTime firedAt)
    {
        employee.Fire(firedAt);
        var user = await _userManager.GetByIdAsync(employee.UserId);
        user.SetIsActive(false);
        (await _userManager.UpdateAsync(user)).CheckErrors();
    }

    public async Task RehireAsync(Employee employee)
    {
        employee.Rehire();
        var user = await _userManager.GetByIdAsync(employee.UserId);
        user.SetIsActive(true);
        (await _userManager.UpdateAsync(user)).CheckErrors();
    }

    /// <summary>"Фамилия Имя Отчество" → Surname = Фамилия, Name = остальное.</summary>
    private static void ApplyName(IdentityUser user, string fullName)
    {
        var parts = fullName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        user.Surname = parts.Length > 0 ? parts[0] : null;
        user.Name = parts.Length > 1 ? parts[1] : parts.FirstOrDefault();
    }
}
