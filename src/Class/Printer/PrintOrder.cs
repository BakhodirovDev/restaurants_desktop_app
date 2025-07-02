namespace Restaurants.Class.Printer
{
    public class PrintOrder
    {
        public string? TableNumber { get; set; }
        public string? RestaurantName { get; set; }
        public string? WaiterName { get; set; }
        public string? OrderDate { get; set; }
        public string? OrderTime { get; set; }
        public string? CheckNumber { get; set; }
        public List<OrderItem>? Orders { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal ServiceFee { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal DiscountPercentage { get; set; }
        public bool IsDiscountPercentage { get; set; }
        public decimal GrandTotal { get; set; }
        public int AdditionalPercentage { get; set; }
        public string? PaymentTypeText { get; set; }
        public int PaymentTypeId { get; set; }
        
        // Additional properties for compatibility
        public string? DateTime { get; set; }
        public List<OrderItem>? Items { get; set; }
        public decimal Total { get; set; }
    }
}
