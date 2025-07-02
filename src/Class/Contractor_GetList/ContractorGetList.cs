namespace Restaurants.Class.Contractor_GetList;

public class ContractorGetList
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public List<TablesInfo> Rows { get; set; }
}
