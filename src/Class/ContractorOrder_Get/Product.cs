namespace Restaurants.Class.ContractorOrder_Get;

public class Product
{
    public string State { get; set; }
    public string Color { get; set; }
    public string Gradient { get; set; }
    public string Width { get; set; }
    public string Height { get; set; }
    public string UnitOfMeasure { get; set; }
    public string SecondUnitOfMeasure { get; set; }
    public string SerialNumber { get; set; }
    public List<FileItem> Files { get; set; }
    public List<object> Analogs { get; set; }
    public List<object> Properties { get; set; }
    public List<object> Operations { get; set; }
    public List<object> Services { get; set; }
    public int Id { get; set; }
    public int StateId { get; set; }
    public string Code { get; set; }
    public string SubCode { get; set; }
    public string BarCode { get; set; }
    public string ShortName { get; set; }
    public string FullName { get; set; }
    public int UnitOfMeasureId { get; set; }
    public decimal EstimatedPrice { get; set; }
    public decimal? ItemLength { get; set; }
    public decimal? ItemWidth { get; set; }
    public decimal? ItemHeight { get; set; }
    public decimal? ItemWeight { get; set; }
    public int ProductTypeId { get; set; }
    public int ProductGroupId { get; set; }
    public int? TypeOfPackagingId { get; set; }
    public int? PackageCount { get; set; }
    public decimal? PackageWeight { get; set; }
    public decimal? PackageLength { get; set; }
    public decimal? PackageHeight { get; set; }
    public decimal? PackageWidth { get; set; }
    public decimal? PackageVolume { get; set; }
    public bool IsNotCreateManufacturingReport { get; set; }
    public int? SupplyId { get; set; }
    public string SellerName { get; set; }
    public string SellerAddress { get; set; }
    public string SellerInn { get; set; }
    public int? ColorId { get; set; }
    public int? GradientId { get; set; }
    public int? WidthId { get; set; }
    public int? HeightId { get; set; }
    public bool HasExpireDate { get; set; }
    public bool HasSeria { get; set; }
    public bool HasExParametr { get; set; }
    public string ImageId { get; set; }
}
