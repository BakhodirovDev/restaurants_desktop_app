using Restaurants.Helper;

namespace Restaurants.Class;

public class OrderItem
{
    public int Id { get; set; }
    public string ProductShortName { get; set; }
    public string ContractorRequirement { get; set; } // Details
    public int Quantity { get; set; }
    private decimal _estimatedPrice;
    private decimal _amount;
    
    public decimal EstimatedPrice 
    { 
        get => _estimatedPrice; 
        set => _estimatedPrice = value; 
    }
    
    public decimal Amount 
    { 
        get => _amount; 
        set => _amount = value; 
    }
    
    public decimal TotalAmount { get; set; }
    public int TableNumber { get; set; } // To associate with a table (since ContractorOrderTable lacks TableNumber)
    public int Index { get; internal set; }
    
    // Formatting for display in UI with spaces instead of commas
    public string FormattedEstimatedPrice => AppSettings.FormatCurrency(EstimatedPrice);
    public string FormattedAmount => AppSettings.FormatCurrency(Amount);
}
