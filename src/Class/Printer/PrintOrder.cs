namespace Restaurants.Class.Printer
{
    public class PrintOrder
    {
        public int TableNumber { get; set; }
        public string RestaurantName { get; set; }
        public string WaiterName { get; set; }
        public string OrderDate { get; set; }
        public string OrderTime { get; set; }
        public string CheckNumber { get; set; }
        public List<OrderItem> Orders { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal ServiceFee { get; set; }
        public decimal GrandTotal { get; set; }
    }
}
