using Restaurants.Class.ContractorOrder_Get;

namespace Restaurants.Class.MnfManufacturingReport
{
    public class ContractorOrderTable
    {
        public string ProductShortName { get; set; }
        public string ProductCode { get; set; }
        public string? Responsible { get; set; }
        public int? PositionId { get; set; }
        public string? Position { get; set; }
        public int ManufacturingReportId { get; set; }
        public bool IsNotCreateManufacturingReport { get; set; }
        public bool CanEdit { get; set; }
        public bool CanDelete { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public List<object> Raws { get; set; } = new();
        public List<object> Services { get; set; } = new();
        public List<object> ProductProperties { get; set; } = new();
        public List<FileItem> Files { get; set; } = new();
        public Product Product { get; set; }
        public List<object> ProductDefaultProperties { get; set; } = new();
        public int Id { get; set; }
        public int OrderNumber { get; set; }
        public int ProductId { get; set; }
        public string ContractorRequirement { get; set; }
        public decimal EstimatedPrice { get; set; }
        public decimal Quantity { get; set; }
        public decimal Amount { get; set; }
        public decimal SumInCurrency { get; set; }
        public string? Details { get; set; }
        public string? SubDetails { get; set; }
        public int? ResponsibleId { get; set; }
        public bool IsForManReport { get; set; }
        public DateTime? ExpireDate { get; set; }
        public string? Seria { get; set; }
    }
}
