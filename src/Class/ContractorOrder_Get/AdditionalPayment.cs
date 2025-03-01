namespace Restaurants.Class.ContractorOrder_Get;

public class AdditionalPayment
{
    public string AdditionalPaymentType { get; set; }
    public decimal AdditionalPaymentAmount { get; set; }
    public int AdditionalPercentage { get; set; }
    public int Id { get; set; }
    public int OrderNumber { get; set; }
    public int AdditionalPaymentId { get; set; }
    public decimal Amount { get; set; }
    public string Details { get; set; }
}