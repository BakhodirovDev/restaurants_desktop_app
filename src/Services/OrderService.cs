using Newtonsoft.Json;
using Restaurants.Class.ContractorOrder_Get;
using Restaurants.Class;
using Restaurants.Pages;
using System.Net.Http;
using System.Text;
using System.Windows;
using Restaurants.Class.Printer;
using Restaurants.Class.Contractor_GetList;

namespace Restaurants.Services
{
    public class OrderService
    {
        private readonly HttpClient _httpClient;
        private PrinterService? _printerService;
        private IProductCategoryService? _productCategoryService;
        private LocalStorageService? _localStorageService;
        
        // Dictionary to store previous order states for comparison
        private readonly Dictionary<int, ContractorOrder> _previousOrders = new Dictionary<int, ContractorOrder>();
        
        public OrderService(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _productCategoryService = new ProductCategoryService(httpClient);
            _localStorageService = new LocalStorageService();
        }
        
        /// <summary>
        /// Sets the printer service instance to use for printing orders
        /// </summary>
        public void SetPrinterService(PrinterService printerService)
        {
            _printerService = printerService;
            System.Diagnostics.Debug.WriteLine("printerservice o'rnatildi");
        }
        
        /// <summary>
        /// Sets the product category service instance to use for product category-related operations
        /// </summary>
        public void SetProductCategoryService(IProductCategoryService productCategoryService)
        {
            _productCategoryService = productCategoryService;
            System.Diagnostics.Debug.WriteLine("productcategoryservice o'rnatildi");
        }

        /// <summary>
        /// Sets the local storage service instance to use for local data operations
        /// </summary>
        public void SetLocalStorageService(LocalStorageService localStorageService)
        {
            _localStorageService = localStorageService;
            System.Diagnostics.Debug.WriteLine("localstorageservice o'rnatildi");
        }

        /// <summary>
        /// Process all tables for automatic printing - monitors all orders in background
        /// </summary>
        public async Task ProcessAllTablesForAutoPrint(string token)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("processalltablesforautoprint boshlandi");
                
                if (_localStorageService == null)
                {
                    ErrorHandlingService.LogError("ProcessAllTablesForAutoPrint", new InvalidOperationException("LocalStorageService null"));
                    return;
                }

                // Get all orders from server
                var allOrders = await GetAllOrdersAsync(token);
                if (allOrders == null || !allOrders.Any())
                {
                    System.Diagnostics.Debug.WriteLine("hech qanday buyurtma topilmadi");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"serverdan {allOrders.Count} ta buyurtma olindi");

                // Compare with local storage
                var comparison = _localStorageService.CompareWithServerData(allOrders);

                // Process new orders
                if (comparison.NewOrders.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"{comparison.NewOrders.Count} ta yangi buyurtma topildi");
                    foreach (var newOrder in comparison.NewOrders)
                    {
                        await ProcessNewOrderForPrinting(newOrder);
                    }
                }

                // Process new items in existing orders
                if (comparison.NewItems.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"{comparison.NewItems.Count} ta yangi mahsulot topildi");
                    foreach (var newItem in comparison.NewItems)
                    {
                        await ProcessNewItemForPrinting(newItem);
                    }
                }

                // Handle deleted orders (optional - for logging)
                if (comparison.DeletedOrderIds.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"{comparison.DeletedOrderIds.Count} ta buyurtma o'chirildi: {string.Join(", ", comparison.DeletedOrderIds)}");
                }
            }
            catch (Exception ex)
            {
                ErrorHandlingService.LogError("ProcessAllTablesForAutoPrint", ex);
            }
        }

        /// <summary>
        /// Process a new order for automatic printing
        /// </summary>
        private async Task ProcessNewOrderForPrinting(ContractorOrder order)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"yangi buyurtmani chop etish: {order.Id}");
                
                if (order.Tables == null || !order.Tables.Any())
                {
                    System.Diagnostics.Debug.WriteLine("buyurtmada mahsulotlar yo'q");
                    return;
                }

                // Print each item individually
                var allItems = order.Tables.ToList();
                foreach (var item in allItems)
                {
                    await ProcessNewItemForPrinting(new OrderItemChange
                    {
                        OrderId = order.Id,
                        Item = item,
                        TableNumber = item.OrderNumber
                    });
                }
            }
            catch (Exception ex)
            {
                ErrorHandlingService.LogError($"ProcessNewOrderForPrinting-{order.Id}", ex);
            }
        }

        /// <summary>
        /// Process a new item for automatic printing
        /// </summary>
        private async Task ProcessNewItemForPrinting(OrderItemChange itemChange)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"yangi mahsulotni chop etish: {itemChange.Item.ProductShortName} (Order: {itemChange.OrderId})");
                
                if (_productCategoryService == null)
                {
                    ErrorHandlingService.LogError("ProcessNewItemForPrinting", new InvalidOperationException("ProductCategoryService null"));
                    return;
                }

                // Get all categories
                var categories = await _productCategoryService.GetProductCategoriesAsync();
                if (categories == null || !categories.Any())
                {
                    System.Diagnostics.Debug.WriteLine("kategoriyalar mavjud emas");
                    return;
                }

                // Find category for this item
                var category = categories.FirstOrDefault(c => 
                    itemChange.Item.ProductCode?.StartsWith(c.ShortName, StringComparison.OrdinalIgnoreCase) == true);

                if (category == null)
                {
                    System.Diagnostics.Debug.WriteLine($"mahsulot uchun kategoriya topilmadi: {itemChange.Item.ProductCode}");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"mahsulot kategoriyasi: {category.ShortName}");

                // Find printers for this category
                var printers = await GetPrintersForCategory(category.Id);
                if (printers == null || !printers.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"kategoriya uchun printer topilmadi: {category.ShortName}");
                    return;
                }

                // Create print order for this single item
                var printOrder = CreatePrintOrderForSingleItem(itemChange);
                
                // Print on all printers for this category
                foreach (var printer in printers)
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"chop etish: {printer.Name} printerida");
                        await PrintOrderAsync(printOrder, printer);
                    }
                    catch (Exception ex)
                    {
                        ErrorHandlingService.LogError($"Print-{printer.Name}-{itemChange.Item.ProductShortName}", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorHandlingService.LogError($"ProcessNewItemForPrinting-{itemChange.Item.ProductShortName}", ex);
            }
        }

        /// <summary>
        /// Create print order for a single item
        /// </summary>
        private PrintOrder CreatePrintOrderForSingleItem(OrderItemChange itemChange)
        {
            return new PrintOrder
            {
                CheckNumber = $"T{itemChange.TableNumber}-{itemChange.OrderId}",
                DateTime = DateTime.Now.ToString("dd.MM.yyyy HH:mm"),
                Items = new List<OrderItem>
                {
                    new OrderItem
                    {
                        Name = itemChange.Item.ProductShortName ?? "noma'lum mahsulot",
                        Quantity = (int)itemChange.Item.Quantity,
                        Price = itemChange.Item.EstimatedPrice,
                        Total = itemChange.Item.Amount
                    }
                },
                Total = itemChange.Item.Amount,
                TableNumber = itemChange.TableNumber.ToString()
            };
        }

        /// <summary>
        /// Get all orders from API
        /// </summary>
        private async Task<List<ContractorOrder>> GetAllOrdersAsync(string token)
        {
            try
            {
                var allOrders = new List<ContractorOrder>();
                
                // Get list of all contractors/tables
                var contractors = await GetContractorListAsync(token);
                if (contractors == null || !contractors.Any())
                {
                    System.Diagnostics.Debug.WriteLine("hech qanday stol topilmadi");
                    return allOrders;
                }

                // Get orders for each contractor/table
                foreach (var contractor in contractors)
                {
                    try
                    {
                        var orders = await GetOrdersByContractorAsync(contractor.Id, token);
                        if (orders != null && orders.Any())
                        {
                            allOrders.AddRange(orders);
                        }
                    }
                    catch (Exception ex)
                    {
                        ErrorHandlingService.LogError($"GetOrders-Contractor-{contractor.Id}", ex);
                    }
                }

                return allOrders;
            }
            catch (Exception ex)
            {
                ErrorHandlingService.LogError("GetAllOrdersAsync", ex);
                return new List<ContractorOrder>();
            }
        }

        /// <summary>
        /// Get contractor list from API
        /// </summary>
        private async Task<List<ContractorInfo>> GetContractorListAsync(string token)
        {
            try
            {
                var requestData = new
                {
                    orderBy = "asc",
                    sortBy = "id",
                    isSupplier = false,
                    pageSize = 100,
                    page = 1
                };

                var request = new HttpRequestMessage(HttpMethod.Post, "https://crm-api.webase.uz/crm/Contractor/GetList");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                request.Content = new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    var data = JsonConvert.DeserializeObject<ContractorGetList>(jsonResponse);
                    
                    if (data?.Rows != null)
                    {
                        return data.Rows.Select(r => new ContractorInfo
                        {
                            Id = r.Id,
                            Name = r.FirstName,
                            ShortName = r.FirstName,
                            IsActive = r.HasNotCompletedOrder,
                            TableNumber = r.Id
                        }).ToList();
                    }
                }
                
                return new List<ContractorInfo>();
            }
            catch (Exception ex)
            {
                ErrorHandlingService.LogError("GetContractorListAsync", ex);
                return new List<ContractorInfo>();
            }
        }

        /// <summary>
        /// Get orders by contractor from API
        /// </summary>
        private async Task<List<ContractorOrder>> GetOrdersByContractorAsync(int contractorId, string token)
        {
            try
            {
                var orders = new List<ContractorOrder>();
                
                // Get contractor details first to check if it has orders
                var contractorRequest = new HttpRequestMessage(HttpMethod.Get, $"https://crm-api.webase.uz/crm/Contractor/Get/{contractorId}");
                contractorRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                
                var contractorResponse = await _httpClient.SendAsync(contractorRequest);
                if (contractorResponse.IsSuccessStatusCode)
                {
                    string contractorJson = await contractorResponse.Content.ReadAsStringAsync();
                    var contractorData = JsonConvert.DeserializeObject<TablesInfo>(contractorJson);
                    
                    if (contractorData?.HasNotCompletedOrder == true && contractorData.NotCompletedOrderId.HasValue)
                    {
                        // Get the specific order
                        var orderRequest = new HttpRequestMessage(HttpMethod.Get, $"https://crm-api.webase.uz/crm/ContractorOrder/Get/{contractorData.NotCompletedOrderId.Value}");
                        orderRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                        
                        var orderResponse = await _httpClient.SendAsync(orderRequest);
                        if (orderResponse.IsSuccessStatusCode)
                        {
                            string orderJson = await orderResponse.Content.ReadAsStringAsync();
                            var order = JsonConvert.DeserializeObject<ContractorOrder>(orderJson);
                            
                            if (order != null)
                            {
                                orders.Add(order);
                            }
                        }
                    }
                }
                
                return orders;
            }
            catch (Exception ex)
            {
                ErrorHandlingService.LogError($"GetOrdersByContractorAsync-{contractorId}", ex);
                return new List<ContractorOrder>();
            }
        }

        /// <summary>
        /// Get printers for a specific category
        /// </summary>
        private async Task<List<PrinterInfo>> GetPrintersForCategory(int categoryId)
        {
            try
            {
                if (_productCategoryService == null)
                {
                    System.Diagnostics.Debug.WriteLine("productcategoryservice null");
                    return new List<PrinterInfo>();
                }

                var printers = await _productCategoryService.GetPrintersForCategoryAsync(categoryId);
                return printers ?? new List<PrinterInfo>();
            }
            catch (Exception ex)
            {
                ErrorHandlingService.LogError($"GetPrintersForCategory-{categoryId}", ex);
                return new List<PrinterInfo>();
            }
        }
        
        /// <summary>
        /// Prints an order to the default printer or to the specified printer
        /// If specificPrinter is null, it will try to find a printer based on product categories
        /// </summary>
        public async Task<bool> PrintOrderAsync(PrintOrder printOrder, PrinterInfo specificPrinter = null)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"printorderasync boshlanmoqda: {printOrder?.CheckNumber ?? "null"}");
                
                if (_printerService == null)
                {
                    System.Diagnostics.Debug.WriteLine("printerservice null");
                    ErrorHandlingService.ShowErrorOnce("PrinterService-Null", 
                        "printer xizmati mavjud emas. iltimos, dasturni qayta ishga tushiring.");
                    return false;
                }
                
                // Use the specified printer if provided, otherwise use the default printer
                PrinterInfo printerToUse = specificPrinter;
                
                if (printerToUse == null)
                {
                    // Try to find a printer based on product categories
                    printerToUse = await FindPrinterForOrderAsync(printOrder);
                    
                    // If no category-based printer found, use default
                    if (printerToUse == null)
                    {
                        // Get the default printer
                        printerToUse = _printerService.GetDefaultPrinter();
                        
                        if (printerToUse == null)
                        {
                            System.Diagnostics.Debug.WriteLine("default printer mavjud emas");
                            ErrorHandlingService.ShowWarningOnce("Default-Printer-Missing", 
                                "default printer tanlanmagan. iltimos, printer sozlamalaridan default printerni tanlang.");
                            return false;
                        }
                        
                        System.Diagnostics.Debug.WriteLine($"default printer ishlatilmoqda: {printerToUse.Name}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"kategoriya asosida printer tanlandi: {printerToUse.Name}");
                    }
                }
                
                // Print using the selected printer
                return await _printerService.PrintOrderAsync(printOrder, printerToUse);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"printorderasync xatolik: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"stack trace: {ex.StackTrace}");
                ErrorHandlingService.ShowErrorOnce($"PrintOrder-{printOrder?.CheckNumber}", 
                    $"buyurtmani chop etishda xatolik: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Prints a kitchen order to the default printer or to the specified printer
        /// If specificPrinter is null, it will try to find a printer based on product categories
        /// </summary>
        public async Task<bool> PrintKitchenOrderAsync(ContractorOrder order, PrinterInfo specificPrinter = null)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"printkitchenorderasync boshlanmoqda: {order?.DocNumber ?? "null"}");
                
                if (_printerService == null)
                {
                    System.Diagnostics.Debug.WriteLine("printerservice null");
                    ErrorHandlingService.ShowErrorOnce("PrinterService-Null-Kitchen", 
                        "printer xizmati mavjud emas. iltimos, dasturni qayta ishga tushiring.");
                    return false;
                }
                
                // Use the specified printer if provided, otherwise use the default printer
                PrinterInfo printerToUse = specificPrinter;
                
                if (printerToUse == null)
                {
                    // Try to find a printer based on product categories
                    printerToUse = await FindPrinterForKitchenOrderAsync(order);
                    
                    // If no category-based printer found, use default
                    if (printerToUse == null)
                    {
                        // Get the default printer
                        printerToUse = _printerService.GetDefaultPrinter();
                        
                        if (printerToUse == null)
                        {
                            System.Diagnostics.Debug.WriteLine("default printer mavjud emas");
                            ErrorHandlingService.ShowWarningOnce("Default-Printer-Missing-Kitchen", 
                                "default printer tanlanmagan. iltimos, printer sozlamalaridan default printerni tanlang.");
                            return false;
                        }
                        
                        System.Diagnostics.Debug.WriteLine($"default printer ishlatilmoqda: {printerToUse.Name}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"kategoriya asosida printer tanlandi: {printerToUse.Name}");
                    }
                }
                
                // Print using the selected printer
                return await _printerService.PrintKitchenOrderAsync(order, printerToUse);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"printkitchenorderasync xatolik: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"stack trace: {ex.StackTrace}");
                ErrorHandlingService.ShowErrorOnce($"PrintKitchenOrder-{order?.DocNumber}", 
                    $"oshxona buyurtmasini chop etishda xatolik: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Checks for new items in the order and prints receipts for them on all printers associated with their categories
        /// </summary>
        public async Task CheckAndPrintNewItemsAsync(ContractorOrder order)
        {
            if (order == null || order.Id <= 0 || order.Tables == null || !order.Tables.Any())
            {
                System.Diagnostics.Debug.WriteLine("checkandprintnewitemsasync: order null yoki bo'sh");
                return;
            }
            
            try
            {
                System.Diagnostics.Debug.WriteLine($"checkandprintnewitemsasync boshlandi: order {order.Id}, mahsulotlar soni: {order.Tables.Count}");
                
                // Check if ProductCategoryService is available
                if (_productCategoryService == null)
                {
                    System.Diagnostics.Debug.WriteLine("xatolik: productcategoryservice null");
                    return;
                }
                
                // Get previous state of this order
                ContractorOrder previousOrder = null;
                _previousOrders.TryGetValue(order.Id, out previousOrder);
                
                // Find new items
                List<ContractorOrderTable> newItems;
                
                // If no previous state, treat all items as new
                if (previousOrder == null)
                {
                    System.Diagnostics.Debug.WriteLine($"yangi buyurtma aniqlandi: {order.Id}, barcha mahsulotlar chop etiladi");
                    newItems = order.Tables.ToList();
                }  
                else
                {
                    System.Diagnostics.Debug.WriteLine($"mavjud buyurtma yangilanishi: {order.Id}");
                    // Find new items by comparing current and previous order
                    newItems = FindNewItems(order, previousOrder);
                }
                
                if (!newItems.Any())
                {
                    System.Diagnostics.Debug.WriteLine("yangi mahsulotlar topilmadi");
                    // No new items, update previous state and return
                    _previousOrders[order.Id] = CloneOrder(order);
                    return;
                }
                
                System.Diagnostics.Debug.WriteLine($"yangi mahsulotlar topildi: {newItems.Count} ta");
                foreach (var item in newItems)
                {
                    System.Diagnostics.Debug.WriteLine($"- {item.ProductShortName}: {item.Quantity} ta");
                }
                
                // Get all product categories
                System.Diagnostics.Debug.WriteLine("kategoriyalarni olish boshlandi...");
                var allCategories = await _productCategoryService.GetProductCategoriesAsync();
                if (allCategories == null || allCategories.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("kategoriyalar mavjud emas yoki bo'sh");
                    _previousOrders[order.Id] = CloneOrder(order);
                    return;
                }
                
                System.Diagnostics.Debug.WriteLine($"jami kategoriyalar soni: {allCategories.Count}");
                foreach (var cat in allCategories.Take(3))
                {
                    System.Diagnostics.Debug.WriteLine($"  - {cat.ShortName} (ID: {cat.Id})");
                }
                
                // Group new items by category
                System.Diagnostics.Debug.WriteLine("mahsulotlarni kategoriyalarga ajratish...");
                var itemsByCategory = GroupItemsByCategory(newItems, allCategories);
                
                System.Diagnostics.Debug.WriteLine($"mahsulotlar {itemsByCategory.Count} ta kategoriyaga guruhlandi");
                
                // Print receipts for each category on all associated printers
                foreach (var categoryGroup in itemsByCategory)
                {
                    int categoryId = categoryGroup.Key;
                    var items = categoryGroup.Value;
                    
                    // Skip if no items for this category
                    if (!items.Any()) continue;
                    
                    var category = allCategories.FirstOrDefault(c => c.Id == categoryId);
                    System.Diagnostics.Debug.WriteLine($"kategoriya {categoryId} ({category?.ShortName ?? "nomalum"}) uchun {items.Count} ta mahsulot chop etiladi");
                    
                    // Get all printers for this category
                    System.Diagnostics.Debug.WriteLine($"kategoriya {categoryId} uchun printerlarni olish...");
                    var printers = await _productCategoryService.GetPrintersForCategoryAsync(categoryId);
                    if (printers == null || !printers.Any())
                    {
                        System.Diagnostics.Debug.WriteLine($"kategoriya {categoryId} uchun printerlar topilmadi");
                        continue;
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"kategoriya {categoryId} uchun {printers.Count} ta printer topildi");
                    
                    // Create a print order with only the new items for this category
                    var printOrder = CreatePrintOrderForItems(order, items);
                    System.Diagnostics.Debug.WriteLine($"print order yaratildi: {printOrder.Orders?.Count ?? 0} ta mahsulot");
                    
                    // Print on all printers for this category
                    foreach (var printer in printers)
                    {
                        System.Diagnostics.Debug.WriteLine($"kategoriya {categoryId} uchun {printer.Name} printeriga chop etilmoqda");
                        try
                        {
                            bool printResult = await PrintOrderAsync(printOrder, printer);
                            System.Diagnostics.Debug.WriteLine($"chop etish natijasi: {(printResult ? "muvaffaqiyatli" : "muvaffaqiyatsiz")}");
                        }
                        catch (Exception printEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"chop etishda xatolik: {printEx.Message}");
                        }
                    }
                }
                
                // Update previous state
                _previousOrders[order.Id] = CloneOrder(order);
                System.Diagnostics.Debug.WriteLine($"checkandprintnewitemsasync tugadi: order {order.Id}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"yangi mahsulotlarni tekshirish va chop etishda xatolik: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"stack trace: {ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// Checks for new items in the order and prints each item individually on associated printers
        /// </summary>
        public async Task CheckAndPrintNewItemsIndividuallyAsync(ContractorOrder order)
        {
            if (order == null || order.Id <= 0 || order.Tables == null || !order.Tables.Any())
            {
                System.Diagnostics.Debug.WriteLine("checkandprintnewitemsindividuallyasync: order null yoki bo'sh");
                return;
            }
            
            try
            {
                System.Diagnostics.Debug.WriteLine($"checkandprintnewitemsindividuallyasync boshlandi: order {order.Id}, mahsulotlar soni: {order.Tables.Count}");
                
                // Check if ProductCategoryService is available
                if (_productCategoryService == null)
                {
                    System.Diagnostics.Debug.WriteLine("productcategoryservice null - chop etish imkonsiz");
                    return;
                }
                
                // Get previous state of this order
                ContractorOrder previousOrder = null;
                _previousOrders.TryGetValue(order.Id, out previousOrder);
                
                // Find new items
                var newItems = FindNewItems(order, previousOrder);
                
                if (newItems.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"yangi mahsulotlar topildi: {newItems.Count} ta");
                    
                    // Get categories from the service
                    var categories = await _productCategoryService.GetProductCategoriesAsync();
                    if (categories == null || !categories.Any())
                    {
                        System.Diagnostics.Debug.WriteLine("kategoriyalar olinmadi");
                        _previousOrders[order.Id] = order;
                        return;
                    }
                    
                    // Print each item individually
                    foreach (var item in newItems)
                    {
                        await PrintIndividualItemAsync(item, categories, order);
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("yangi mahsulotlar topilmadi");
                }
                
                // Store current state for future comparisons
                _previousOrders[order.Id] = order;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"checkandprintnewitemsindividuallyasync da xatolik: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Prints an individual item on all printers associated with its category
        /// </summary>
        private async Task PrintIndividualItemAsync(ContractorOrderTable item, List<ProductCategory> categories, ContractorOrder order)
        {
            try
            {
                string productName = item.ProductShortName;
                if (string.IsNullOrEmpty(productName))
                {
                    System.Diagnostics.Debug.WriteLine("mahsulot nomi bo'sh");
                    return;
                }
                
                System.Diagnostics.Debug.WriteLine($"individual mahsulot chop etilmoqda: {productName}");
                
                // Find category for this product
                var category = categories.FirstOrDefault(c => 
                    productName.Contains(c.ShortName, StringComparison.OrdinalIgnoreCase) || 
                    c.ShortName.Contains(productName, StringComparison.OrdinalIgnoreCase));
                
                if (category == null)
                {
                    System.Diagnostics.Debug.WriteLine($"mahsulot '{productName}' uchun kategoriya topilmadi");
                    return;
                }
                
                System.Diagnostics.Debug.WriteLine($"mahsulot '{productName}' kategoriya '{category.ShortName}' ga tegishli");
                
                // Get printers for this category
                var printers = await _productCategoryService.GetPrintersForCategoryAsync(category.Id);
                if (printers == null || !printers.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"kategoriya '{category.ShortName}' uchun printer topilmadi");
                    return;
                }
                
                System.Diagnostics.Debug.WriteLine($"kategoriya '{category.ShortName}' uchun {printers.Count} ta printer topildi");
                
                // Create a temporary order with just this item for printing
                var singleItemOrder = new ContractorOrder
                {
                    Id = order.Id,
                    DocNumber = order.DocNumber,
                    DocDate = order.DocDate,
                    DocTime = order.DocTime,
                    Responsible = order.Responsible,
                    OrganizationAreasOfActivity = order.OrganizationAreasOfActivity,
                    Tables = new List<ContractorOrderTable> { item },
                    Amount = item.Amount,
                    TotalAmount = item.Amount
                };
                
                // Print on each printer associated with this category
                foreach (var printer in printers)
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"mahsulot '{productName}' printer '{printer.Name}' da chop etilmoqda");
                        
                        if (_printerService != null)
                        {
                            bool success = await _printerService.PrintKitchenOrderAsync(singleItemOrder, printer);
                            if (success)
                            {
                                System.Diagnostics.Debug.WriteLine($"mahsulot '{productName}' muvaffaqiyatli chop etildi printer '{printer.Name}' da");
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"mahsulot '{productName}' chop etishda xatolik printer '{printer.Name}' da");
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("printerservice null");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"mahsulot '{productName}' printer '{printer.Name}' da chop etishda xatolik: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"printindividualitemasync da xatolik: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Creates a deep copy of a ContractorOrder
        /// </summary>
        private ContractorOrder CloneOrder(ContractorOrder order)
        {
            if (order == null) return null;
            
            // Use JSON serialization for deep cloning
            string json = JsonConvert.SerializeObject(order);
            return JsonConvert.DeserializeObject<ContractorOrder>(json);
        }
        
        /// <summary>
        /// Finds new items added to the order by comparing with previous state
        /// </summary>
        private List<ContractorOrderTable> FindNewItems(ContractorOrder currentOrder, ContractorOrder previousOrder)
        {
            var newItems = new List<ContractorOrderTable>();
            
            if (currentOrder?.Tables == null || previousOrder?.Tables == null)
            {
                return newItems;
            }
            
            // Find items in current order that weren't in previous order
            foreach (var currentItem in currentOrder.Tables)
            {
                // Skip if item has no ID
                if (currentItem.Id <= 0) continue;
                
                // Check if this item existed in previous order
                var previousItem = previousOrder.Tables.FirstOrDefault(i => i.Id == currentItem.Id);
                
                if (previousItem == null)
                {
                    // This is a completely new item
                    newItems.Add(currentItem);
                    System.Diagnostics.Debug.WriteLine($"yangi mahsulot qo'shildi: {currentItem.ProductShortName}");
                }
                else if (currentItem.Quantity > previousItem.Quantity)
                {
                    // The quantity of this item has increased
                    // Create a copy with just the increased quantity
                    var increasedItem = CloneItem(currentItem);
                    increasedItem.Quantity = currentItem.Quantity - previousItem.Quantity;
                    increasedItem.Amount = increasedItem.Quantity * increasedItem.EstimatedPrice;
                    
                    newItems.Add(increasedItem);
                    System.Diagnostics.Debug.WriteLine($"mahsulot miqdori oshdi: {currentItem.ProductShortName}, +{increasedItem.Quantity}");
                }
            }
            
            return newItems;
        }
        
        /// <summary>
        /// Creates a deep copy of a ContractorOrderTable
        /// </summary>
        private ContractorOrderTable CloneItem(ContractorOrderTable item)
        {
            if (item == null) return null;
            
            // Use JSON serialization for deep cloning
            string json = JsonConvert.SerializeObject(item);
            return JsonConvert.DeserializeObject<ContractorOrderTable>(json);
        }
        
        /// <summary>
        /// Groups items by their category ID
        /// </summary>
        private Dictionary<int, List<ContractorOrderTable>> GroupItemsByCategory(
            List<ContractorOrderTable> items, 
            List<ProductCategory> categories)
        {
            var result = new Dictionary<int, List<ContractorOrderTable>>();
            
            System.Diagnostics.Debug.WriteLine($"mahsulotlarni kategoriyalarga ajratish boshlandi. mahsulotlar soni: {items.Count}, kategoriyalar soni: {categories.Count}");
            
            foreach (var item in items)
            {
                string productName = item.ProductShortName;
                if (string.IsNullOrEmpty(productName)) 
                {
                    System.Diagnostics.Debug.WriteLine($"mahsulot nomi bo'sh, o'tkazib yuborildi");
                    continue;
                }
                
                System.Diagnostics.Debug.WriteLine($"mahsulot uchun kategoriya qidirish: '{productName}'");
                
                // Find category for this product using multiple approaches
                ProductCategory category = null;
                
                // Approach 1: Exact match
                category = categories.FirstOrDefault(c => 
                    string.Equals(productName, c.ShortName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(productName, c.FullName, StringComparison.OrdinalIgnoreCase));
                
                if (category != null)
                {
                    System.Diagnostics.Debug.WriteLine($"aniq moslik topildi: '{productName}' -> '{category.ShortName}' (ID: {category.Id})");
                }
                else
                {
                    // Approach 2: Contains match
                    category = categories.FirstOrDefault(c => 
                        productName.Contains(c.ShortName, StringComparison.OrdinalIgnoreCase) || 
                        c.ShortName.Contains(productName, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(c.FullName) && (
                            productName.Contains(c.FullName, StringComparison.OrdinalIgnoreCase) ||
                            c.FullName.Contains(productName, StringComparison.OrdinalIgnoreCase))));
                    
                    if (category != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"qisman moslik topildi: '{productName}' -> '{category.ShortName}' (ID: {category.Id})");
                    }
                }
                
                if (category == null)
                {
                    // Approach 3: Keyword-based matching for common food categories
                    string lowerProductName = productName.ToLower();
                    
                    if (lowerProductName.Contains("salat") || lowerProductName.Contains("салат"))
                    {
                        category = categories.FirstOrDefault(c => c.ShortName.ToLower().Contains("salat") || c.ShortName.ToLower().Contains("салат"));
                    }
                    else if (lowerProductName.Contains("shashlik") || lowerProductName.Contains("шашлик") || 
                             lowerProductName.Contains("kabob") || lowerProductName.Contains("кабоб"))
                    {
                        category = categories.FirstOrDefault(c => c.ShortName.ToLower().Contains("shashlik") || c.ShortName.ToLower().Contains("шашлик"));
                    }
                    else if (lowerProductName.Contains("taom") || lowerProductName.Contains("еда") || 
                             lowerProductName.Contains("osh") || lowerProductName.Contains("плов"))
                    {
                        category = categories.FirstOrDefault(c => c.ShortName.ToLower().Contains("taom") || c.ShortName.ToLower().Contains("еда"));
                    }
                    
                    if (category != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"kalit so'z orqali moslik topildi: '{productName}' -> '{category.ShortName}' (ID: {category.Id})");
                    }
                }
                
                if (category == null)
                {
                    System.Diagnostics.Debug.WriteLine($"kategoriya topilmadi: '{productName}'. mavjud kategoriyalar:");
                    foreach (var cat in categories.Take(5)) // Show first 5 categories as example
                    {
                        System.Diagnostics.Debug.WriteLine($"  - {cat.ShortName} (ID: {cat.Id})");
                    }
                    
                    // Use the first category as fallback or skip
                    if (categories.Any())
                    {
                        category = categories.First();
                        System.Diagnostics.Debug.WriteLine($"default kategoriya ishlatildi: '{category.ShortName}' (ID: {category.Id})");
                    }
                    else
                    {
                        continue;
                    }
                }
                
                // Add item to the category group
                if (!result.ContainsKey(category.Id))
                {
                    result[category.Id] = new List<ContractorOrderTable>();
                }
                
                result[category.Id].Add(item);
                System.Diagnostics.Debug.WriteLine($"mahsulot qo'shildi: '{productName}' -> kategoriya {category.Id} ({category.ShortName})");
            }
            
            System.Diagnostics.Debug.WriteLine($"guruhlash tugadi. {result.Count} ta kategoriya, jami {result.Values.Sum(v => v.Count)} ta mahsulot");
            
            return result;
        }
        
        /// <summary>
        /// Creates a print order for specific items
        /// </summary>
        private PrintOrder CreatePrintOrderForItems(ContractorOrder order, List<ContractorOrderTable> items)
        {
            // Create a print order with only the specified items
            var printOrder = new PrintOrder
            {
                TableNumber = order.Tables.FirstOrDefault()?.OrderNumber.ToString() ?? "0",
                RestaurantName = order.OrganizationAreasOfActivity ?? "",
                WaiterName = order.Responsible ?? "",
                OrderDate = order.DocDate ?? "",
                OrderTime = order.DocTime ?? "",
                CheckNumber = order.DocNumber ?? "",
                Orders = items
                    .Where(item => item != null)
                    .Select(item => new OrderItem
                    {
                        Id = item.Id,
                        ProductShortName = item.ProductShortName ?? "No Name",
                        ContractorRequirement = item.ContractorRequirement ?? "No Details",
                        Quantity = (int)Math.Max(1, item.Quantity),
                        EstimatedPrice = Math.Round(Math.Max(0, item.EstimatedPrice), 2),
                        Amount = Math.Round(Math.Max(0, item.Amount), 2),
                        TableNumber = int.TryParse(order.Tables.FirstOrDefault()?.OrderNumber.ToString() ?? "0", out int num) ? num : 0
                    })
                    .Where(item => !string.IsNullOrEmpty(item.ProductShortName))
                    .ToList() ?? new List<OrderItem>(),
                TotalAmount = items.Sum(i => i.Amount),
                PaymentTypeText = order.EstimatedPaymentType ?? "Naqd",
                PaymentTypeId = order.EstimatedPaymentTypeId
            };
            
            return printOrder;
        }
        
        /// <summary>
        /// Finds a printer for an order based on product categories
        /// </summary>
        private async Task<PrinterInfo> FindPrinterForOrderAsync(PrintOrder order)
        {
            if (_productCategoryService == null || order?.Orders == null || !order.Orders.Any())
            {
                return null;
            }
            
            try
            {
                // Get all product categories from the API
                var allCategories = await _productCategoryService.GetProductCategoriesAsync();
                if (allCategories == null || allCategories.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("kategoriyalar mavjud emas");
                    return null;
                }
                
                // For now, we'll use the first product's category to determine the printer
                // In a more complex implementation, you might want to group products by category
                // and print separate receipts for each category
                
                // Extract product name from the first order item
                var firstProduct = order.Orders.FirstOrDefault();
                if (firstProduct == null)
                {
                    return null;
                }
                
                string productName = firstProduct.ProductShortName;
                
                // Find category for this product (simplified approach - in real implementation, 
                // you would need to have proper product-to-category mapping)
                var category = allCategories.FirstOrDefault(c => 
                    productName.Contains(c.ShortName, StringComparison.OrdinalIgnoreCase) || 
                    c.ShortName.Contains(productName, StringComparison.OrdinalIgnoreCase));
                
                if (category == null)
                {
                    System.Diagnostics.Debug.WriteLine($"mahsulot uchun kategoriya topilmadi: {productName}");
                    return null;
                }
                
                System.Diagnostics.Debug.WriteLine($"mahsulot kategoriyasi: {category.ShortName}, ID: {category.Id}");
                
                // Find printers for this category
                var printers = await _productCategoryService.GetPrintersForCategoryAsync(category.Id);
                if (printers == null || printers.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"kategoriya uchun printerlar topilmadi: {category.ShortName}");
                    return null;
                }
                
                // Use the first printer found for this category
                return printers.FirstOrDefault();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"printer tanlashda xatolik: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"stack trace: {ex.StackTrace}");
                return null;
            }
        }
        
        /// <summary>
        /// Finds a printer for a kitchen order based on product categories
        /// </summary>
        private async Task<PrinterInfo> FindPrinterForKitchenOrderAsync(ContractorOrder order)
        {
            if (_productCategoryService == null || order?.Tables == null || !order.Tables.Any())
            {
                return null;
            }
            
            try
            {
                // Get all product categories from the API
                var allCategories = await _productCategoryService.GetProductCategoriesAsync();
                if (allCategories == null || allCategories.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("kategoriyalar mavjud emas");
                    return null;
                }
                
                // Extract product name from the first table item
                var firstProduct = order.Tables.FirstOrDefault();
                if (firstProduct == null)
                {
                    return null;
                }
                
                string productName = firstProduct.ProductShortName;
                
                // Find category for this product (simplified approach - in real implementation, 
                // you would need to have proper product-to-category mapping)
                var category = allCategories.FirstOrDefault(c => 
                    productName.Contains(c.ShortName, StringComparison.OrdinalIgnoreCase) || 
                    c.ShortName.Contains(productName, StringComparison.OrdinalIgnoreCase));
                
                if (category == null)
                {
                    System.Diagnostics.Debug.WriteLine($"mahsulot uchun kategoriya topilmadi: {productName}");
                    return null;
                }
                
                System.Diagnostics.Debug.WriteLine($"mahsulot kategoriyasi: {category.ShortName}, ID: {category.Id}");
                
                // Find printers for this category
                var printers = await _productCategoryService.GetPrintersForCategoryAsync(category.Id);
                if (printers == null || printers.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"kategoriya uchun printerlar topilmadi: {category.ShortName}");
                    return null;
                }
                
                // Use the first printer found for this category
                return printers.FirstOrDefault();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"printer tanlashda xatolik: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"stack trace: {ex.StackTrace}");
                return null;
            }
        }
        
        /// <summary>
        /// Marks a product as defective and removes it from the order
        /// </summary>
        public async Task<ContractorOrder> MarkProductAsDefectiveAsync(
            ContractorOrder order, 
            int itemId, 
            decimal defectiveQuantity, 
            string token)
        {
            if (order == null)
                throw new ArgumentNullException(nameof(order));
                
            if (string.IsNullOrEmpty(token))
                throw new ArgumentException("Valid token is required", nameof(token));
                
            // Find the item in the order
            var item = order.Tables?.FirstOrDefault(t => t.Id == itemId);
            if (item == null)
                throw new ArgumentException($"Item with ID {itemId} not found in order", nameof(itemId));
                
            // Create a copy of the order without the defective product for the update
            var orderToUpdate = new
            {
                id = order.Id,
                statusId = order.StatusId,
                docNumber = order.DocNumber,
                docDate = order.DocDate,
                docTime = order.DocTime,
                firstContactId = order.FirstContactId,
                contact = order.Contact,
                clientName = order.ClientName,
                startDate = order.StartDate,
                estimatedEndDate = order.EstimatedEndDate,
                endDate = order.EndDate,
                estimatedPaymentTypeId = order.EstimatedPaymentTypeId,
                responsibleId = order.ResponsibleId,
                callPriorityId = order.CallPriorityId,
                currencyId = order.CurrencyId,
                isForManReport = order.IsForManReport,
                organizationAreasOfActivityId = order.OrganizationAreasOfActivityId,
                ctWarehouseId = order.CtWarehouseId,
                contractorId = order.ContractorId,
                isCreateManufacturingReport = order.IsCreateManufacturingReport,
                details = order.Details,
                locationUrl = order.LocationUrl,
                additionalQuantity = order.AdditionalQuantity,
                saleAmount = order.SaleAmount,
                salePercent = order.SalePercent,
                tables = (from t in order.Tables
                         where t.Id != itemId || defectiveQuantity < t.Quantity
                         select new
                         {
                             id = t.Id,
                             orderNumber = t.OrderNumber,
                             productId = t.ProductId,
                             contractorRequirement = t.ContractorRequirement,
                             estimatedPrice = t.EstimatedPrice,
                             quantity = t.Id == itemId ? (t.Quantity - defectiveQuantity) : t.Quantity,
                             defectedQuantity = t.Id == itemId ? defectiveQuantity : t.DefectedQuantity,
                             amount = t.Id == itemId ? (t.EstimatedPrice * (t.Quantity - defectiveQuantity)) : t.Amount,
                             defectedAmount = t.Id == itemId ? (t.EstimatedPrice * defectiveQuantity) : t.DefectedAmount,
                             details = t.Details,
                             responsibleId = t.ResponsibleId,
                             ctWarehouseId = t.CtWarehouseId
                         }).ToList(),
                additionalPayments = order.AdditionalPayments?.Select(p => new
                {
                    id = p.Id,
                    orderNumber = p.OrderNumber,
                    additionalPaymentId = p.AdditionalPaymentId,
                    amount = p.Amount,
                    details = p.Details
                }).ToList(),

            };

            // Send API request to update order
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                client.DefaultRequestHeaders.Add("accept", "*/*");

                var content = new StringContent(
                    JsonConvert.SerializeObject(orderToUpdate),
                    Encoding.UTF8,
                    "application/json-patch+json");

                HttpResponseMessage response = await client.PostAsync(
                    "https://crm-api.webase.uz/crm/ContractorOrder/Update",
                    content);

                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    var updatedOrder = JsonConvert.DeserializeObject<ContractorOrder>(jsonResponse);
                    
                    // Immediately get fresh data from the server to ensure everything is updated
                    var refreshedOrder = await GetOrderByIdAsync(updatedOrder.Id, token);
                    return refreshedOrder ?? updatedOrder;
                }
                else
                {
                    // Handle error response
                    string errorResponse = await response.Content.ReadAsStringAsync();
                    throw new HttpRequestException($"API error: {response.StatusCode}\n{errorResponse}");
                }
            }
        }
        
        /// <summary>
        /// Gets the latest order data from the server by ID
        /// </summary>
        public async Task<ContractorOrder> GetOrderByIdAsync(int orderId, string token)
        {
            if (orderId <= 0)
                throw new ArgumentException("Valid order ID is required", nameof(orderId));
                
            if (string.IsNullOrEmpty(token))
                throw new ArgumentException("Valid token is required", nameof(token));
                
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                    client.DefaultRequestHeaders.Add("accept", "text/plain");
                    
                    HttpResponseMessage response = await client.GetAsync(
                        $"https://crm-api.webase.uz/crm/ContractorOrder/Get/{orderId}");
                        
                    if (response.IsSuccessStatusCode)
                    {
                        string jsonResponse = await response.Content.ReadAsStringAsync();
                        return JsonConvert.DeserializeObject<ContractorOrder>(jsonResponse);
                    }
                    
                    // Return null if we couldn't get the refreshed data
                    return null;
                }
            }
            catch
            {
                // If refreshing fails, we'll just return null and use the original updated order
                return null;
            }
        }
    }
} 