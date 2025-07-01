using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Restaurants.Class;
using Restaurants.Services;

namespace Restaurants.Services
{
    public interface IProductCategoryService
    {
        /// <summary>
        /// Get all product categories from API
        /// </summary>
        Task<List<ProductCategory>> GetProductCategoriesAsync();

        /// <summary>
        /// Save printer-category mappings
        /// </summary>
        Task SavePrinterCategoryMappingsAsync(string printerId, List<PrinterCategory> mappings);

        /// <summary>
        /// Get printer-category mappings
        /// </summary>
        Task<List<PrinterCategory>> GetPrinterCategoryMappingsAsync();

        /// <summary>
        /// Get categories for a specific printer
        /// </summary>
        Task<List<ProductCategory>> GetCategoriesForPrinterAsync(string printerId);

        /// <summary>
        /// Get printers for a specific category
        /// </summary>
        Task<List<PrinterInfo>> GetPrintersForCategoryAsync(int categoryId);
    }
} 