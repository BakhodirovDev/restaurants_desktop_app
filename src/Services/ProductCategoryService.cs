using Newtonsoft.Json.Linq;
using Restaurants.Class;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Restaurants.Services
{
    public class ProductCategoryService : IProductCategoryService
    {
        private readonly HttpClient _httpClient;
        private const string CATEGORY_API_URL = "https://crm-api.webase.uz/whm/ProductGroup/GetList";
        private const string PRINTER_CATEGORY_MAPPINGS_FILE = "printer_categories.json";

        public ProductCategoryService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            
            // Clear any existing headers
            _httpClient.DefaultRequestHeaders.Clear();
            
            // Add all headers from curl command
            _httpClient.DefaultRequestHeaders.Add("accept", "application/json, text/plain, */*");
            _httpClient.DefaultRequestHeaders.Add("accept-language", "ru-RU,ru;q=0.9,uz-UZ;q=0.8,uz;q=0.7,en-US;q=0.6,en;q=0.5");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", 
                "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJiYWRtaW4iLCJleHAiOjE3NTA0MzQ1MjgsImlzcyI6Imh0dHA6Ly9sb2NhbGhvc3Q6NTAwMCIsImF1ZCI6Imh0dHA6Ly9sb2NhbGhvc3Q6NTAwMCJ9.Mx-XmqaaVr9Zi387eiALHYLmRhWEiTYaki4UWplwL4o");
            _httpClient.DefaultRequestHeaders.Add("origin", "https://crm.webase.uz");
            _httpClient.DefaultRequestHeaders.Add("priority", "u=1, i");
            _httpClient.DefaultRequestHeaders.Add("referer", "https://crm.webase.uz/");
            _httpClient.DefaultRequestHeaders.Add("sec-ch-ua", "\"Not(A:Brand\";v=\"99\", \"Google Chrome\";v=\"133\", \"Chromium\";v=\"133\"");
            _httpClient.DefaultRequestHeaders.Add("sec-ch-ua-mobile", "?0");
            _httpClient.DefaultRequestHeaders.Add("sec-ch-ua-platform", "\"Windows\"");
            _httpClient.DefaultRequestHeaders.Add("sec-fetch-dest", "empty");
            _httpClient.DefaultRequestHeaders.Add("sec-fetch-mode", "cors");
            _httpClient.DefaultRequestHeaders.Add("sec-fetch-site", "same-site");
            _httpClient.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/133.0.0.0 Safari/537.36");
        }

        /// <summary>
        /// Get all product categories from API
        /// </summary>
        public async Task<List<ProductCategory>> GetProductCategoriesAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("kategoriyalarni olish boshlandi");
                
                // Create request body exactly as in the curl command
                var requestData = new
                {
                    search = "",
                    sortBy = "",
                    orderType = "asc",
                    pageSize = 20,
                    page = 1,
                    pageSizeOptions = new[] { 10, 20, 50, 100 },
                    totalRows = 0
                };

                // Serialize with proper options
                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                };
                
                string jsonContent = JsonSerializer.Serialize(requestData, jsonOptions);
                System.Diagnostics.Debug.WriteLine($"so'rov json matni: {jsonContent}");
                
                // Create request with proper content type
                var content = new StringContent(jsonContent);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                content.Headers.ContentType.CharSet = "UTF-8";
                
                // Create a new HttpRequestMessage to have more control
                var request = new HttpRequestMessage(HttpMethod.Post, CATEGORY_API_URL)
                {
                    Content = content
                };
                
                // Add authorization token to request headers, not content headers
                if (!string.IsNullOrEmpty(Settings.Default.AccessToken))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Settings.Default.AccessToken);
                    System.Diagnostics.Debug.WriteLine($"using access token from settings: {Settings.Default.AccessToken.Substring(0, 20)}...");
                }
                
                System.Diagnostics.Debug.WriteLine($"so'rov yuborilmoqda: {CATEGORY_API_URL}");
                System.Diagnostics.Debug.WriteLine($"headers: {string.Join(", ", _httpClient.DefaultRequestHeaders.Select(h => $"{h.Key}: {string.Join(", ", h.Value)}"))}");
                
                // Send request
                var response = await _httpClient.SendAsync(request);
                
                System.Diagnostics.Debug.WriteLine($"javob olindi, status: {response.StatusCode}");
                System.Diagnostics.Debug.WriteLine($"javob headers: {string.Join(", ", response.Headers.Select(h => $"{h.Key}: {string.Join(", ", h.Value)}"))}");
                
                // Read response content even if status code is not success
                var responseContent = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"javob matni: {responseContent}");
                
                // Now check status code
                response.EnsureSuccessStatusCode();
                
                // Deserialize with proper options
                var categoryResponse = JsonSerializer.Deserialize<ProductCategoryResponse>(responseContent, jsonOptions);
                System.Diagnostics.Debug.WriteLine($"kategoriyalar soni: {categoryResponse?.Rows?.Count ?? 0}");

                return categoryResponse?.Rows ?? new List<ProductCategory>();
            }
            catch (HttpRequestException ex)
            {
                System.Diagnostics.Debug.WriteLine($"http so'rov xatoligi: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"status kodi: {ex.StatusCode}");
                System.Diagnostics.Debug.WriteLine($"stack trace: {ex.StackTrace}");
                return new List<ProductCategory>();
            }
            catch (JsonException ex)
            {
                System.Diagnostics.Debug.WriteLine($"json xatoligi: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"stack trace: {ex.StackTrace}");
                return new List<ProductCategory>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"kategoriyalarni olishda xatolik: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"stack trace: {ex.StackTrace}");
                return new List<ProductCategory>();
            }
        }

        /// <summary>
        /// Save printer-category mappings
        /// </summary>
        public async Task SavePrinterCategoryMappingsAsync(string printerId, List<PrinterCategory> mappings)
        {
            try
            {
                // Load existing mappings
                var allMappings = await GetPrinterCategoryMappingsAsync();
                
                // Remove existing mappings for this printer
                allMappings.RemoveAll(m => m.PrinterId == printerId);
                
                // Add new mappings
                allMappings.AddRange(mappings);
                
                // Save to file
                string json = JsonSerializer.Serialize(allMappings);
                await File.WriteAllTextAsync(PRINTER_CATEGORY_MAPPINGS_FILE, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving printer category mappings: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Get printer-category mappings
        /// </summary>
        public async Task<List<PrinterCategory>> GetPrinterCategoryMappingsAsync()
        {
            try
            {
                if (File.Exists(PRINTER_CATEGORY_MAPPINGS_FILE))
                {
                    string json = await File.ReadAllTextAsync(PRINTER_CATEGORY_MAPPINGS_FILE);
                    return JsonSerializer.Deserialize<List<PrinterCategory>>(json) ?? new List<PrinterCategory>();
                }
                
                return new List<PrinterCategory>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting printer category mappings: {ex.Message}");
                return new List<PrinterCategory>();
            }
        }

        /// <summary>
        /// Get categories for a specific printer
        /// </summary>
        public async Task<List<ProductCategory>> GetCategoriesForPrinterAsync(string printerId)
        {
            try
            {
                // Get all mappings
                var allMappings = await GetPrinterCategoryMappingsAsync();
                
                // Filter mappings for this printer
                var printerMappings = allMappings.Where(m => m.PrinterId == printerId).ToList();
                
                // Get all categories
                var allCategories = await GetProductCategoriesAsync();
                
                // Return only categories that are mapped to this printer
                return allCategories.Where(c => printerMappings.Any(m => m.CategoryId == c.Id)).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting categories for printer: {ex.Message}");
                return new List<ProductCategory>();
            }
        }

        public async Task<List<PrinterInfo>> GetPrintersForCategoryAsync(int categoryId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"kategoriya uchun printerlarni olish boshlandi: {categoryId}");
                
                var mappings = await GetPrinterCategoryMappingsAsync();
                System.Diagnostics.Debug.WriteLine($"jami mappinglar soni: {mappings.Count}");
                
                var printerIds = mappings
                    .Where(m => m.CategoryId == categoryId)
                    .Select(m => m.PrinterId)
                    .ToList();
                
                System.Diagnostics.Debug.WriteLine($"kategoriya {categoryId} uchun printer idlar: [{string.Join(", ", printerIds)}]");
                
                // Load printers from file
                string printersFilePath = "printers.json";
                if (!File.Exists(printersFilePath))
                {
                    System.Diagnostics.Debug.WriteLine("printerlar fayli mavjud emas");
                    return new List<PrinterInfo>();
                }
                
                string json = await File.ReadAllTextAsync(printersFilePath);
                var allPrinters = JsonSerializer.Deserialize<List<PrinterInfo>>(json);
                
                System.Diagnostics.Debug.WriteLine($"jami printerlar soni: {allPrinters?.Count ?? 0}");
                if (allPrinters != null)
                {
                    foreach (var printer in allPrinters)
                    {
                        System.Diagnostics.Debug.WriteLine($"  printer: {printer.Name}");
                    }
                }
                
                var categoryPrinters = allPrinters?
                    .Where(p => printerIds.Contains(p.Name))
                    .ToList() ?? new List<PrinterInfo>();
                
                System.Diagnostics.Debug.WriteLine($"kategoriya {categoryId} uchun topilgan printerlar: {categoryPrinters.Count} ta");
                foreach (var printer in categoryPrinters)
                {
                    System.Diagnostics.Debug.WriteLine($"  - {printer.Name} ({printer.Description})");
                }
                
                return categoryPrinters;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"kategoriya uchun printerlarni olishda xatolik: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"stack trace: {ex.StackTrace}");
                return new List<PrinterInfo>();
            }
        }
    }
} 