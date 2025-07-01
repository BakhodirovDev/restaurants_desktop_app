using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace Restaurants.Class
{
    public class ProductCategory
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        // Add CategoryId as an alias for Id
        public int CategoryId => Id;

        [JsonPropertyName("code")]
        public string Code { get; set; }

        [JsonPropertyName("shortName")]
        public string ShortName { get; set; }

        [JsonPropertyName("fullName")]
        public string FullName { get; set; }

        [JsonPropertyName("stateId")]
        public int StateId { get; set; }

        [JsonPropertyName("productTypeId")]
        public int ProductTypeId { get; set; }

        [JsonPropertyName("productType")]
        public string ProductType { get; set; }

        [JsonPropertyName("isRaw")]
        public bool IsRaw { get; set; }

        [JsonPropertyName("state")]
        public string State { get; set; }
    }

    public class ProductCategoryResponse
    {
        [JsonPropertyName("page")]
        public int Page { get; set; }

        [JsonPropertyName("pageSize")]
        public int PageSize { get; set; }

        [JsonPropertyName("total")]
        public int Total { get; set; }

        [JsonPropertyName("rows")]
        public List<ProductCategory> Rows { get; set; }
    }
} 