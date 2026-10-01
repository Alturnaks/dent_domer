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

    // Пациенты
    public const string PatientDuplicate = "Dental:PatientDuplicate";
    public const string IinTaken = "Dental:IinTaken";
    public const string InvalidIin = "Dental:InvalidIin";
    public const string InvalidPhone = "Dental:InvalidPhone";
    public const string InvalidBirthDate = "Dental:InvalidBirthDate";
    public const string PatientMergeInvalid = "Dental:PatientMergeInvalid";

    // Каталог
    public const string CategoryParentInvalid = "Dental:CategoryParentInvalid";
    public const string ServiceCodeAlreadyExists = "Dental:ServiceCodeAlreadyExists";
    public const string PriceInvalid = "Dental:PriceInvalid";
    public const string TechCardItemInvalid = "Dental:TechCardItemInvalid";

    // Подтверждения
    public const string ApprovalNotPending = "Dental:ApprovalNotPending";
    public const string ApprovalOwnRequest = "Dental:ApprovalOwnRequest";
    public const string ApprovalLimitExceeded = "Dental:ApprovalLimitExceeded";
}
