namespace Restaurants.Class.ContractorOrder_Get;

public class ContractorOrder
{
    public string Currency { get; set; }
    public string Status { get; set; }
    public string FirstContact { get; set; }
    public string EstimatedPaymentType { get; set; }
    public string CallPriority { get; set; }
    public string Responsible { get; set; }
    public string ResponsiblePhoneNumber { get; set; }
    public decimal? PayedAmount { get; set; }
    public string PayedCurrency { get; set; }
    public decimal? PayedCurrencyAmount { get; set; }
    public int OrganizationId { get; set; }
    public bool IsForManReport { get; set; }
    public string Contractor { get; set; }
    public decimal Amount { get; set; }
    public decimal SumInCurrency { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal TotalSumInCurrency { get; set; }
    public decimal AdditinalPayment { get; set; }
    public decimal? OrgCurrencyRate { get; set; }
    public string OrganizationAreasOfActivity { get; set; }
    public List<ContractorOrderTable>? Tables { get; set; }
    public List<AdditionalPayment> AdditionalPayments { get; set; }
    public int EstimatedPaymentTypeId { get; set; }
    public int TotalProductsCount { get; set; }
    public int CompletedProductsCount { get; set; }
    public int Id { get; set; }
    public int StatusId { get; set; }
    public string DocNumber { get; set; }
    public string DocDate { get; set; }
    public string DocTime { get; set; }
    public int? FirstContactId { get; set; }
    public string Contact { get; set; }
    public string ClientName { get; set; }
    public string StartDate { get; set; }
    public string EstimatedEndDate { get; set; }
    public string EndDate { get; set; }
    public int ResponsibleId { get; set; }
    public int? CallPriorityId { get; set; }
    public int CurrencyId { get; set; }
    public int OrganizationAreasOfActivityId { get; set; }
    public int? CtWarehouseId { get; set; }
    public int ContractorId { get; set; }
    public bool IsCreateManufacturingReport { get; set; }
    public string Details { get; set; }
    public string LocationUrl { get; set; }
}
