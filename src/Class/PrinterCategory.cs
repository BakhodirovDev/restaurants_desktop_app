using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace Restaurants.Class
{
    public class PrinterCategory
    {
        [JsonPropertyName("printerId")]
        public string PrinterId { get; set; }

        [JsonPropertyName("printerName")]
        public string PrinterName { get; set; }

        [JsonPropertyName("categoryId")]
        public int CategoryId { get; set; }

        [JsonPropertyName("categoryName")]
        public string CategoryName { get; set; }
    }
} 