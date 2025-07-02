using Restaurants.Class.ContractorOrder_Get;

namespace Restaurants.Class.MnfManufacturingReport;

public class Row
{
    public int Id { get; set; }
    public string DocNumber { get; set; }
    public DateTime DocDate { get; set; }
    public TimeSpan DocTime { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string Details { get; set; }
    public int StatusId { get; set; }
    public int OrganizationAreasOfActivityId { get; set; }
    public int CtWarehouseId { get; set; }
    public int ContractorOrderId { get; set; }
    public int ContractorOrderTableId { get; set; }
    public string ProductName { get; set; }
    public int ProductId { get; set; }
    public int Status { get; set; }
    public string OrganizationAreasOfActivity { get; set; }
    public string CtWarehouse { get; set; }
    public DateTime DateOfCreated { get; set; }
    public DateTime DateOfModified { get; set; }
    public string ContractorRequirement { get; set; }
    public int CallPriorityId { get; set; }
    public decimal Amount { get; set; }
    public decimal Quantity { get; set; }
    public string Responsible { get; set; }
    public string Contractor { get; set; }
    public string ProductUnitOfMeasure { get; set; }
    public DateTime? ContractorOrderEndDate { get; set; }
    public ContractorOrderTable ContractorOrderTable { get; set; }
}
