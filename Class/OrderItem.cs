namespace Restaurants.Class
{
    public class OrderItem
    {
        public int Id { get; set; }
        public string Nomi { get; set; }
        public decimal Soni { get; set; }
        public decimal Narxi { get; set; }
        public decimal Summa { get; set; }
        public string ProductShortName { get; set; }
        public string ProductCode { get; set; }
        public int ProductId { get; set; }
        public decimal EstimatedPrice { get; set; }
        public decimal Quantity { get; set; }
        public decimal Amount { get; set; }
    }
}
