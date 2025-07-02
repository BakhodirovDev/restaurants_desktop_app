namespace Restaurants.Class
{
    public class UserInfo
    {
        public int Id { get; set; }
        public string Inn { get; set; }
        public string UserName { get; set; }
        public string FullName { get; set; }
        public string ShortName { get; set; }
        public string MiddleName { get; set; }
        public string LastName { get; set; }
        public string FirstName { get; set; }
        public bool IsAdmin { get; set; }
        public int? LanguageId { get; set; }
        public string LanguageCode { get; set; }
        public string Language { get; set; }
        public string Pinfl { get; set; }
        public int OrganizationId { get; set; }
        public int? PositionId { get; set; }
        public int StateId { get; set; }
        public string Email { get; set; }
        public string PhoneNumber { get; set; }
        public string Position { get; set; }
        public string Organization { get; set; }
        public string OrganizationInn { get; set; }
        public string OrganizationVatCode { get; set; }
        public string OrganizationAddress { get; set; }
        public bool HasSecondUnitOfMeasure { get; set; }
        public int UserTypeId { get; set; }
        public bool IsSimpleUser { get; set; }
        public bool IsOrgAdmin { get; set; }
        public bool IsSuperAdmin { get; set; }
        public string UserType { get; set; }
        public int EmployeeId { get; set; }
        public int OrganizationCurrencyId { get; set; }
        public List<int> UserOrgAreasOfActivities { get; set; }
        public List<string> Modules { get; set; }
        public List<string> Roles { get; set; }
    }
}
