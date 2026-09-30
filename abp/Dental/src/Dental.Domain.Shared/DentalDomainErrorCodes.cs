namespace Dental;

public static class DentalDomainErrorCodes
{
    public const string BranchAccessDenied = "Dental:BranchAccessDenied";
    public const string BranchNameAlreadyExists = "Dental:BranchNameAlreadyExists";
    public const string EmployeeAlreadyExists = "Dental:EmployeeAlreadyExists";
    public const string EmployeeAlreadyFired = "Dental:EmployeeAlreadyFired";
    public const string CannotFireYourself = "Dental:CannotFireYourself";
    public const string EmployeeBranchesRequired = "Dental:EmployeeBranchesRequired";
    public const string RoleLimitExceeded = "Dental:RoleLimitExceeded";
    public const string InvalidWorkingHours = "Dental:InvalidWorkingHours";
}
