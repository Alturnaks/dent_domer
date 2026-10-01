namespace Dental.Patients;

public enum Gender
{
    Unknown = 0,
    Male = 1,
    Female = 2,
}

public static class PatientConsts
{
    public const int MaxNameLength = 100;
    public const int MaxIinLength = 12;
    public const int MaxPhoneLength = 20;
    public const int MaxEmailLength = 256;
    public const int MaxAddressLength = 500;
    public const int MaxNotesLength = 4000;
    public const int MaxTagsLength = 1000;
    public const int MaxTagLength = 50;
    public const int MaxConsentTypeLength = 100;
    public const int MaxFileUrlLength = 1000;
    public const int MaxLeadSourceNameLength = 200;

    /// <summary>Причины совпадения при поиске дублей.</summary>
    public const string DuplicateReasonPhone = "phone";
    public const string DuplicateReasonIin = "iin";
    public const string DuplicateReasonNameBirthDate = "name_birthdate";
}
