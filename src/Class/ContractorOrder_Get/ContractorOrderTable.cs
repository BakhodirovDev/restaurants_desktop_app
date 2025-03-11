using Newtonsoft.Json;
using System.Windows.Documents;

namespace Restaurants.Class.ContractorOrder_Get;

public class ContractorOrderTable
{
    public string ProductShortName { get; set; }
    public string ProductCode { get; set; }
    public string Responsible { get; set; }
    public int? PositionId { get; set; }
    public string Position { get; set; }
    public int? ManufacturingReportId { get; set; }
    public bool IsNotCreateManufacturingReport { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
    public bool IsCompleted { get; set; }
    public List<object> Raws { get; set; }
    public List<object> Services { get; set; }
    public List<object> ProductProperties { get; set; }
    public List<FileItem> Files { get; set; }
    public Product Product { get; set; }
    public int Id { get; set; }
    public int OrderNumber { get; set; }
    public int ProductId { get; set; }
    public string ContractorRequirement { get; set; }
    public decimal EstimatedPrice { get; set; }
    public decimal Quantity { get; set; }
    public decimal Amount { get; set; }
    public decimal SumInCurrency { get; set; }
    public string Details { get; set; }
    public string SubDetails { get; set; }
    public int? ResponsibleId { get; set; }
    public bool IsForManReport { get; set; }
    public string ExpireDate { get; set; }
    public string Seria { get; set; }
}