namespace Restaurants.Class;

public class ContractorRow
{
    public int Id { get; set; }
    public string ShortName { get; set; }
    public string FullName { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string PhoneNumber { get; set; }
    public string ContactInfo { get; set; }
    public string State { get; set; }
    public bool HasNotCompletedOrder { get; set; }
    public int? notCompletedOrderId { get; set; }
}
