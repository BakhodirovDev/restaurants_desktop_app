namespace Restaurants.Class.Contractor_GetList;

public class TablesInfo
{
    public int Id { get; set; }
    public string ShortName { get; set; }
    public string FullName { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string MiddleName { get; set; }
    public string OrgName { get; set; }
    public string InnPinfl { get; set; }
    public int? OkedId { get; set; }
    public int? BankId { get; set; }
    public int CountryId { get; set; }
    public int RegionId { get; set; }
    public int DistrictId { get; set; }
    public int ContractorTypeId { get; set; }
    public string Address { get; set; }
    public string Accounter { get; set; }
    public string Director { get; set; }
    public string PhoneNumber { get; set; }
    public string ContactInfo { get; set; }
    public string VatCode { get; set; }
    public string State { get; set; }
    public string Oked { get; set; }
    public string Bank { get; set; }
    public string Country { get; set; }
    public string Region { get; set; }
    public string District { get; set; }
    public string ContractorType { get; set; }
    public bool? IsClient { get; set; } = null;
    public bool IsSupplier { get; set; } = false;
    public bool IsEmployee { get; set; }
    public bool HasNotCompletedOrder { get; set; }
    public int? NotCompletedOrderId { get; set; }
    public int ProductsCount { get; set; }
    public int CompletedProductsCount { get; set; }
}
