using Restaurants.Helper;
using Restaurants.Printer;

namespace Restaurants.Class;

public class OrderItem : IPrintableOrderItem
{
    public int Id { get; set; }
    public string? ProductShortName { get; set; }
    public string? ContractorRequirement { get; set; } // Details
    private int _quantity;
    public int Quantity 
    { 
        get => _quantity; 
        set => _quantity = value; 
    }
    
    // Implement decimal Quantity for IPrintableOrderItem
    decimal IPrintableOrderItem.Quantity => _quantity;
    
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
    
    // Additional properties for compatibility
    public string? Name { get; set; }
    public decimal Price { get; set; }
    public decimal Total { get; set; }
    
    // Formatting for display in UI with spaces instead of commas
    public string FormattedEstimatedPrice => AppSettings.FormatCurrency(EstimatedPrice);
    public string FormattedAmount => AppSettings.FormatCurrency(Amount);
}
