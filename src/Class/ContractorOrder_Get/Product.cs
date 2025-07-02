namespace Restaurants.Class.ContractorOrder_Get;

public class Product
{
    public string State { get; set; }
    public string? Color { get; set; }
    public string? Gradient { get; set; }
    public decimal? Width { get; set; }
    public decimal? Height { get; set; }
    public string UnitOfMeasure { get; set; }
    public string? SecondUnitOfMeasure { get; set; }
    public string? SerialNumber { get; set; }
    public List<object> Analogs { get; set; } = new();
    public List<object> Properties { get; set; } = new();
    public List<object> Files { get; set; } = new();
    public List<object> Operations { get; set; } = new();
    public int Id { get; set; }
    public int StateId { get; set; }
    public string Code { get; set; }
    public string? SubCode { get; set; } = null;
    public string BarCode { get; set; }
    public string ShortName { get; set; }
    public string FullName { get; set; }
    public int UnitOfMeasureId { get; set; }
    public int? SecondUnitOfMeasureId { get; set; } = null;
    public decimal EstimatedPrice { get; set; }
    public decimal? ItemLength { get; set; } = null;
    public decimal? ItemWidth { get; set; } = null;
    public decimal? ItemHeight { get; set; } = null;
    public decimal? ItemWeight { get; set; } = null;
    public int ProductTypeId { get; set; }
    public int ProductGroupId { get; set; }
    public int? TypeOfPackagingId { get; set; } = null;
    public int? PackageCount { get; set; } = null;
    public decimal? PackageWeight { get; set; } = null;
    public decimal? PackageLength { get; set; } = null;
    public decimal? PackageHeight { get; set; } = null;
    public decimal? PackageWidth { get; set; } = null;
    public decimal? PackageVolume { get; set; } = null;
    public bool IsNotCreateManufacturingReport { get; set; }
    public int? SupplyId { get; set; } = null;
    public string? SellerName { get; set; } = null;
    public string? SellerAddress { get; set; } = null;
    public string? SellerInn { get; set; } = null;
    public int? ColorId { get; set; } = null;
    public int? GradientId { get; set; } = null;
    public int? WidthId { get; set; } = null;
    public int? HeightId { get; set; } = null;
    public bool HasExpireDate { get; set; }
    public bool HasSeria { get; set; }
    public bool HasExParametr { get; set; }
    public int? ImageId { get; set; } = null;
    public List<object> Services { get; set; } = new();
}
