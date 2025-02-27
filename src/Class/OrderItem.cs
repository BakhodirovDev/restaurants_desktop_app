namespace Restaurants.Class;

public class OrderItem
{
    public int Id { get; set; }
    public string ProductShortName { get; set; }
    public string ContractorRequirement { get; set; } // Details
    public int Quantity { get; set; }
    public decimal EstimatedPrice { get; set; }
    public decimal Amount { get; set; }
    public int TableNumber { get; set; } // To associate with a table (since ContractorOrderTable lacks TableNumber)
    public int Index { get; internal set; }
}
