namespace Restaurants.Class;

public class ContractorResponse
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public List<ContractorRow> Rows { get; set; }
}
