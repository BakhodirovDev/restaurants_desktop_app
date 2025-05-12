using Newtonsoft.Json;
using Restaurants.Class;
using Restaurants.Class.Contractor_GetList;
using Restaurants.Class.ContractorOrder_Get;
using Restaurants.Class.Printer;
using Restaurants.Helper;
using Restaurants.Pages;
using Restaurants.Pages.Windows;
using Restaurants.Printer;
using Restaurants.Services;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Globalization;
using System.Windows.Data;

namespace Restaurants.Classes
{
    public partial class Kassa : Window
    {
        private readonly HttpClient _httpClient;
        private DispatcherTimer timer;
        private DispatcherTimer timeTimer;
        private int countdown = 3;
        private string currentSelectedTable = "-1"; // String sifatida boshlang'ich qiymat
        private readonly Dictionary<string, List<ContractorOrder>> tableOrders = new();
        private readonly XPrinter _printer;
        private readonly OrderService _orderService;
        
        // Discount-related properties
        private decimal discountAmount = 0;
        private bool isDiscountPercentage = true;
        private decimal discountPercentage = 0;

        public Kassa(HttpClient httpClient, XPrinter printer)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            InitializeComponent();

            btnPrint.IsEnabled = false;
            btnChangePaymentMethod.IsEnabled = false;

            this.SizeChanged += Kassa_SizeChanged;

            timeTimer = new DispatcherTimer();
            timeTimer.Interval = TimeSpan.FromSeconds(1);
            timeTimer.Tick += (s, e) => lblRealTime.Text = DateTime.Now.ToString("HH:mm:ss");
            timeTimer.Start();

            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(3);
            timer.Tick += Timer_Tick;

            this.Loaded += Window_Loaded;
            _printer = printer;
            _orderService = new OrderService(httpClient);
            
            // Set loading overlay to visible by default
            loadingOverlay.Visibility = Visibility.Visible;
        }

        private void Kassa_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            _ = LoadTablesAsync();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await LoadTablesAsync();
                await GetData();
                
                // Start auto-refresh timer
                timer.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ilova ochilishida xatolik: {ex.Message}", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /* Api */

        private async Task<ContractorGetList> GetTablesList()
        {
            var requestData = new
            {
                orderBy = "asc",
                sortBy = "id",
                isSupplier = false,
                pageSize = 100,
                page = 1
            };

            return await SendApiRequestAsync<ContractorGetList>(
                HttpMethod.Post,
                "https://crm-api.webase.uz/crm/Contractor/GetList",
                requestData
            );
        }

        private async Task<ContractorOrder> ContractorOrderGet()
        {
            // Remove the loading overlay show from here since GetData already shows it
            
            string token = await EnsureValidTokenAsync();
            var request = new HttpRequestMessage(HttpMethod.Get, "https://crm-api.webase.uz/crm/ContractorOrder/Get");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            HttpResponseMessage response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                string jsonResponse = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<ContractorOrder>(jsonResponse);
            }
            return null;
        }

        private async Task<ContractorOrder> ContractorOrderGetById(int notCompletedOrderId)
        {
            return await SendApiRequestAsync<ContractorOrder>(
                HttpMethod.Get,
                $"https://crm-api.webase.uz/crm/ContractorOrder/Get/{notCompletedOrderId}"
            );
        }

        /* End Api */

        private async Task LoadTablesAsync()
        {
            try
            {
                // Get the latest table data from the server
                var data = await GetTablesList();
                if (data?.Rows != null)
                {
                    // Generate updated table buttons with latest server data
                    GenerateTableButtons(data.Rows);
                    
                    // Update currently selected table to ensure its display is up-to-date
                    if (!string.IsNullOrEmpty(currentSelectedTable) && currentSelectedTable != "-1")
                    {
                        // Find the selected button and update its visual state
                        foreach (Button btn in tablesPanel.Children)
                        {
                            if (btn.Tag is TableButtonData btnData && btnData.TableNumber == currentSelectedTable)
                            {
                                bool btnIsBusy = tableOrders.TryGetValue(btnData.TableNumber, out var orders) &&
                                              orders != null &&
                                              orders.Any(o => o.StatusId != 3);
                                ApplyTableStyle(btn, btnIsBusy, true);
                                break;
                            }
                        }
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                MessageBox.Show($"Network error loading tables: {ex.Message}", "Network Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (JsonException ex)
            {
                MessageBox.Show($"Error parsing table data: {ex.Message}", "Parse Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unexpected error loading tables: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void GenerateTableButtons(List<TablesInfo> tables)
        {
            if (tables == null || !tables.Any()) return;

            tablesPanel.Children.Clear();
            double panelWidth = tablesPanel.ActualWidth > 0 ? tablesPanel.ActualWidth : 600;
            int buttonsPerRow = (int)(panelWidth / 140);
            double buttonWidth = (panelWidth / buttonsPerRow) - 20;

            foreach (var table in tables)
            {
                string tableName = table.FirstName;
                int productsCount = 0;
                int completedProductsCount = 0;
                string responsibleName = "";

                if (tableOrders.TryGetValue(tableName, out var orders) && orders != null && orders.Any())
                {
                    productsCount = orders.Sum(o => o.TotalProductsCount);
                    completedProductsCount = orders.Sum(o => o.CompletedProductsCount);
                    responsibleName = orders.FirstOrDefault()?.Responsible ?? "";
                }

                string orderCountText = productsCount > 0 ? $"{completedProductsCount}/{productsCount}" : "0/0";
                bool isBusy = table.HasNotCompletedOrder;

                Button tableButton = new()
                {
                    Content = tableName,
                    Width = buttonWidth,
                    Height = 100,
                    Margin = new Thickness(10),
                    Tag = new TableButtonData
                    {
                        TableNumber = tableName,
                        ContractorId = table.Id,
                        NotCompletedOrderId = table.NotCompletedOrderId,
                        OrderCountText = orderCountText,
                        ResponsibleName = responsibleName
                    }
                };

                ApplyTableStyle(tableButton, isBusy, tableName == currentSelectedTable);
                tableButton.Click += TableButton_Click;
                tablesPanel.Children.Add(tableButton);
            }
        }

        private void ApplyTableStyle(Button button, bool isBusy, bool isSelected)
        {
            if (isSelected)
            {
                if (isBusy)
                    button.Style = (Style)FindResource("SelectedBusyTableButtonStyle"); // #F4B400
                else
                    button.Style = (Style)FindResource("SelectedEmptyTableButtonStyle"); // #0F9D58
            }
            else
            {
                if (isBusy)
                    button.Style = (Style)FindResource("BusyTableButtonStyle"); // #F44336
                else
                    button.Style = (Style)FindResource("TableButtonStyle"); // #5C6BC0
            }
        }

        private async void TableButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button clickedButton) return;
            if (clickedButton.Tag is not TableButtonData buttonData) return;

            string tableNumber = buttonData.TableNumber;
            int? notCompletedOrderId = buttonData.NotCompletedOrderId;
            string responsibleName = buttonData.ResponsibleName;

            if (!string.IsNullOrEmpty(currentSelectedTable) && currentSelectedTable != tableNumber)
            {
                ResetPreviousTableButtonStyle();
            }

            currentSelectedTable = tableNumber;
            lblOfitsiantValue.Text = responsibleName;
            lblStolValue.Text = "#" + tableNumber;

            bool isBusy = tableOrders.TryGetValue(tableNumber, out var orders) && orders != null && orders.Any(o => o.StatusId != 3);
            ApplyTableStyle(clickedButton, isBusy, true);

            LoadTableOrders(tableNumber);

            if (notCompletedOrderId.HasValue)
            {
                await GetDataForTable(notCompletedOrderId.Value);
            }
        }

        private void ResetPreviousTableButtonStyle()
        {
            foreach (Button btn in tablesPanel.Children)
            {
                if (btn.Tag is not TableButtonData btnData || btnData.TableNumber != currentSelectedTable) continue;

                bool btnIsBusy = tableOrders.TryGetValue(btnData.TableNumber, out var orders) &&
                                 orders != null &&
                                 orders.Any(o => o.StatusId != 3);

                ApplyTableStyle(btn, btnIsBusy, false);
                break;
            }
        }

        private void LoadTableOrders(string tableNumber)
        {
            lvItems.Items.Clear();
            lblAmountValue.Text = "0.0 so'm";
            lblAdditinalPaymentValue.Text = "0.0 so'm";
            lblDiscountValue.Text = "0.0 so'm";
            lblTotalAmountValue.Text = "0.0 so'm";
            
            // Reset discount values when loading a new table
            discountAmount = 0;
            isDiscountPercentage = true;
            discountPercentage = 0;

            if (!tableOrders.TryGetValue(tableNumber, out var orders) || orders == null || !orders.Any())
            {
                Console.WriteLine($"No orders found for table {tableNumber}");
                lblPaymentMethod.Text = "Tanlanmagan";
                lblPaymentMethod.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
                PaymentMethodBorder.Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1),
                    GradientStops = new GradientStopCollection
            {
                new GradientStop(System.Windows.Media.Color.FromRgb(255, 235, 238), 0),
                new GradientStop(System.Windows.Media.Color.FromRgb(255, 205, 210), 1)
            }
                };
                UpdateButtonStates(); // Tugmalar holatini yangilash
                return;
            }

            // Always get the first order for consistency
            var firstOrder = orders.FirstOrDefault();
            if (firstOrder == null) return;
            
            // Update the responsible person name if available
            if (!string.IsNullOrEmpty(firstOrder.Responsible))
            {
                lblOfitsiantValue.Text = firstOrder.Responsible;
            }

            // Get all valid order items
            var orderItems = orders
                .SelectMany(o => o.Tables ?? new List<ContractorOrderTable>())
                .Where(t => t != null && !string.IsNullOrEmpty(t.ProductShortName))
                .Select((item, index) => new OrderItem
                {
                    Index = index + 1,
                    Id = item.Id,
                    ProductShortName = item.ProductShortName ?? "No Name",
                    ContractorRequirement = item.ContractorRequirement ?? "No Details",
                    Quantity = (int)Math.Max(1, item.Quantity),
                    EstimatedPrice = item.EstimatedPrice,
                    Amount = item.Amount,
                    TableNumber = int.TryParse(tableNumber, out int num) ? num : 0
                })
                .ToList();

            // Add all items to the list view
            foreach (var item in orderItems)
            {
                lvItems.Items.Add(item);
            }

            // Get service fee percentage from backend
            int serviceFeePercentage = 0;
            
            if (firstOrder.AdditionalPayments != null && firstOrder.AdditionalPayments.Count > 0)
            {
                serviceFeePercentage = firstOrder.AdditionalPayments[0].AdditionalPercentage;
                // Update the global service fee percentage
                AppSettings.ServiceFeePercentage = serviceFeePercentage;
            }
            
            // Update discount values from the server response
            if (firstOrder.SalePercent > 0)
            {
                // If percentage discount is applied
                discountPercentage = firstOrder.SalePercent;
                discountAmount = firstOrder.SaleAmount;
                isDiscountPercentage = true;
            }
            else if (firstOrder.SaleAmount > 0)
            {
                // If amount discount is applied
                discountAmount = firstOrder.SaleAmount;
                discountPercentage = 0;
                isDiscountPercentage = false;
            }
            
            // Format currency with spaces instead of commas - use server values directly
            lblAmountValue.Text = $"{AppSettings.FormatCurrency(firstOrder.Amount)} so'm";
            
            // Show service fee with percentage - use server values directly
            lblAdditinalPaymentValue.Text = $"{AppSettings.FormatCurrency(firstOrder.AdditinalPayment)} so'm ({serviceFeePercentage}%)";
            
            // Show discount - use server values directly
            if (firstOrder.SaleAmount > 0 || firstOrder.SalePercent > 0)
            {
                if (firstOrder.SalePercent > 0)
                {
                    lblDiscountValue.Text = $"{AppSettings.FormatCurrency(firstOrder.SaleAmount)} so'm ({firstOrder.SalePercent}%)";
                }
                else
                {
                    lblDiscountValue.Text = $"{AppSettings.FormatCurrency(firstOrder.SaleAmount)} so'm";
                }
            }
            else
            {
                lblDiscountValue.Text = "0 so'm";
            }
            
            // Show final amount from server
            lblTotalAmountValue.Text = $"{AppSettings.FormatCurrency(firstOrder.TotalAmount)} so'm";
            
            // To'lov turini ContractorOrder dan olish
            if (!string.IsNullOrEmpty(firstOrder.EstimatedPaymentType))
            {
                lblPaymentMethod.Text = firstOrder.EstimatedPaymentType;
                lblPaymentMethod.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
                PaymentMethodBorder.Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1),
                    GradientStops = new GradientStopCollection
            {
                new GradientStop(System.Windows.Media.Color.FromRgb(200, 230, 201), 0),
                new GradientStop(System.Windows.Media.Color.FromRgb(165, 214, 167), 1)
            }
                };
            }
            else
            {
                lblPaymentMethod.Text = "Tanlanmagan";
                lblPaymentMethod.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
                PaymentMethodBorder.Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1),
                    GradientStops = new GradientStopCollection
            {
                new GradientStop(System.Windows.Media.Color.FromRgb(255, 235, 238), 0),
                new GradientStop(System.Windows.Media.Color.FromRgb(255, 205, 210), 1)
            }
                };
            }

            lvItems.Items.Refresh();
            UpdateButtonStates(); // Tugmalar holatini yangilash
        }

        private async Task GetData()
        {
            try
            {
                // Show loading before fetching data
                loadingOverlay.Visibility = Visibility.Visible;
                
                var data = await ContractorOrderGet();
                
                // Hide loading overlay after getting data
                loadingOverlay.Visibility = Visibility.Collapsed;
                
                if (data == null)
                {
                    MessageBox.Show("No order data returned from the API.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    lblRestoranValue.Text = "";
                    lblOfitsiantValue.Text = "";
                    lblSanaValue.Text = "";
                    lblVaqtValue.Text = "";
                    lblChekRaqamiValue.Text = "#";
                    ProcessApiData(new ContractorOrder());
                    return;
                }

                lblRestoranValue.Text = data.OrganizationAreasOfActivity ?? "";
                lblOfitsiantValue.Text = data.Responsible ?? "";
                lblSanaValue.Text = data.DocDate ?? "";
                lblVaqtValue.Text = data.DocTime ?? "";
                lblChekRaqamiValue.Text = "#" + (data.DocNumber ?? "");

                ProcessApiData(data);

                if (!string.IsNullOrEmpty(currentSelectedTable) && currentSelectedTable != "-1")
                {
                    var selectedButton = tablesPanel.Children.OfType<Button>()
                        .FirstOrDefault(b => b.Tag is TableButtonData td && td.TableNumber == currentSelectedTable);
                    if (selectedButton != null && selectedButton.Tag is TableButtonData btnData && btnData.NotCompletedOrderId.HasValue)
                    {
                        await GetDataForTable(btnData.NotCompletedOrderId.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                // Hide loading overlay in case of error
                loadingOverlay.Visibility = Visibility.Collapsed;
                MessageBox.Show($"Error occurred while fetching order data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ProcessApiData(ContractorOrder data)
        {
            if (data == null) return;

            // Clear existing tables data to ensure complete refresh
            tableOrders.Clear();
            
            // Process each table in the order
            foreach (var table in data.Tables ?? new List<ContractorOrderTable>())
            {
                string tableNumber = table.OrderNumber.ToString();
                if (string.IsNullOrEmpty(tableNumber)) continue;

                if (!tableOrders.ContainsKey(tableNumber))
                {
                    tableOrders[tableNumber] = new List<ContractorOrder>();
                }

                // Always add the latest data 
                tableOrders[tableNumber].Add(data);

                // Update the table button UI
                UpdateTableButtonStyle(tableNumber, data);
            }

            // Update UI for currently selected table
            if (!string.IsNullOrEmpty(currentSelectedTable) && currentSelectedTable != "-1")
            {
                LoadTableOrders(currentSelectedTable);
            }
        }

        private void UpdateTableButtonStyle(string tableNumber, ContractorOrder order)
        {
            foreach (Button btn in tablesPanel.Children)
            {
                if (btn.Tag is not TableButtonData tagData || tagData.TableNumber != tableNumber) continue;

                int productsCount = order.TotalProductsCount;
                int completedProductsCount = order.CompletedProductsCount;
                bool isBusy = productsCount > 0 && (order.StatusId != 3);

                tagData.OrderCountText = productsCount > 0 ? $"{completedProductsCount}/{productsCount}" : "0/0";
                btn.Tag = tagData;

                ApplyTableStyle(btn, isBusy, tableNumber == currentSelectedTable);
                break;
            }
        }

        private async Task GetDataForTable(int notCompletedOrderId)
        {
            try
            {
                // Show loading overlay when fetching data for table
                loadingOverlay.Visibility = Visibility.Visible;
                
                var data = await ContractorOrderGetById(notCompletedOrderId);
                
                // Hide loading overlay after getting data
                loadingOverlay.Visibility = Visibility.Collapsed;
                
                if (data == null)
                {
                    MessageBox.Show($"No order data returned for Order ID {notCompletedOrderId}.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    ProcessTableData(new ContractorOrder());
                    return;
                }

                ProcessTableData(data);

                if (!string.IsNullOrEmpty(currentSelectedTable) && currentSelectedTable != "-1")
                {
                    LoadTableOrders(currentSelectedTable);
                }
            }
            catch (Exception ex)
            {
                // Hide loading overlay in case of error
                loadingOverlay.Visibility = Visibility.Collapsed;
                MessageBox.Show($"Error occurred while fetching order data for ID {notCompletedOrderId}: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ProcessTableData(ContractorOrder data)
        {
            if (data == null) data = new ContractorOrder();

            string tableNumber = currentSelectedTable;
            List<ContractorOrderTable> tables = data.Tables ?? new List<ContractorOrderTable>();

            if (!tableOrders.ContainsKey(tableNumber))
            {
                tableOrders[tableNumber] = new List<ContractorOrder>();
            }

            if (!tableOrders[tableNumber].Any(o => o.Id == data.Id))
            {
                tableOrders[tableNumber].Add(data);
            }
            
            // Update the responsible person name if available
            if (!string.IsNullOrEmpty(data.Responsible))
            {
                lblOfitsiantValue.Text = data.Responsible;
                
                // Update the responsible name in TableButtonData too
                foreach (Button btn in tablesPanel.Children)
                {
                    if (btn.Tag is TableButtonData tagData && tagData.TableNumber == tableNumber)
                    {
                        tagData.ResponsibleName = data.Responsible;
                        btn.Tag = tagData;
                        break;
                    }
                }
            }

            foreach (Button btn in tablesPanel.Children)
            {
                if (btn.Tag is TableButtonData tagData && tagData.TableNumber == tableNumber)
                {
                    int productsCount = data.TotalProductsCount;
                    int completedProductsCount = data.CompletedProductsCount;
                    string orderCountText = productsCount > 0 ? $"{completedProductsCount}/{productsCount}" : "0/0";
                    tagData.OrderCountText = orderCountText;
                    bool isBusy = productsCount > 0 && (data.StatusId != 3);
                    ApplyTableStyle(btn, isBusy, true);
                    break;
                }
            }

            LoadTableOrders(tableNumber);
        }

        private async Task SmoothAutoRefresh()
        {
            try {
                // Foydalanuvchiga bilinmasdan, orqa fonda ma'lumotlarni yangilash
                
                // Fetch and update service fee percentage without UI updates
                await UpdateServiceFeePercentageFromApi(false);
                
                // Yangi ma'lumotlarni olib, lekin UI ni tozalamay yangilash
                var data1 = await ContractorOrderGet();
                if (data1 == null) return;

                // Mavjud ma'lumotlarni foydalanuvchiga bilintirmasdan yangilash
                ProcessApiDataSilently(data1);
                
                // Tanlangan stol uchun ma'lumotlarni foydalanuvchiga bilintirmasdan yangilash
                if (!string.IsNullOrEmpty(currentSelectedTable) && currentSelectedTable != "-1")
                {
                    var selectedButton = tablesPanel.Children.OfType<Button>()
                        .FirstOrDefault(b => b.Tag is TableButtonData td && td.TableNumber == currentSelectedTable);
                    
                    if (selectedButton != null && selectedButton.Tag is TableButtonData btnData && btnData.NotCompletedOrderId.HasValue)
                    {
                        // Get fresh data for the selected table
                        var refreshedOrder = await ContractorOrderGetById(btnData.NotCompletedOrderId.Value);
                        if (refreshedOrder != null)
                        {
                            // Process the refreshed data without flickering
                            ProcessTableDataSilently(refreshedOrder);
                        }
                    }
                }

                // Stol ma'lumotlarini yangilash (faqat kerakli qismlarini)
                await UpdateTablesQuietly();
                
                // Update button states without UI flickering
                UpdateButtonStatesSilently();
            }
            catch (Exception ex) {
                Console.WriteLine($"Silent auto refresh error: {ex.Message}");
            }
        }

        // Yangi method: Orqa fonda stollarni yangilash, lipillamay
        private async Task UpdateTablesQuietly()
        {
            try
            {
                var data = await GetTablesList();
                if (data?.Rows == null) return;

                // Stol tugmachalari asosiy parametrlarini faqat (band/bo'sh holati) yangilash
                foreach (var table in data.Rows)
                {
                    string tableName = table.FirstName;
                    bool isBusy = table.HasNotCompletedOrder;
                    int? notCompletedOrderId = table.NotCompletedOrderId;

                    foreach (Button btn in tablesPanel.Children)
                    {
                        if (btn.Tag is TableButtonData btnData && btnData.TableNumber == tableName)
                        {
                            // Tugmacha status/data ni yangilash
                            btnData.NotCompletedOrderId = notCompletedOrderId;
                            btnData.IsBusy = isBusy;

                            if (tableOrders.TryGetValue(tableName, out var orders) && orders != null && orders.Any())
                            {
                                var order = orders.FirstOrDefault();
                                if (order != null)
                                {
                                    int productsCount = order.TotalProductsCount;
                                    int completedProductsCount = order.CompletedProductsCount;
                                    btnData.OrderCountText = productsCount > 0 ? $"{completedProductsCount}/{productsCount}" : "0/0";
                                }
                            }

                            btn.Tag = btnData;

                            // Faqat tanlangan stol bo'lmaganlarga style qo'llash
                            if (tableName != currentSelectedTable)
                            {
                                ApplyTableStyle(btn, isBusy, false);
                            }
                            
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UpdateTablesQuietly error: {ex.Message}");
            }
        }

        // Yangi method: Orqa fonda ma'lumotlarni yangilash, UI ga ta'sir qilmay
        private void ProcessApiDataSilently(ContractorOrder data)
        {
            if (data == null) return;
            
            // Process each table in the order
            foreach (var table in data.Tables ?? new List<ContractorOrderTable>())
            {
                string tableNumber = table.OrderNumber.ToString();
                if (string.IsNullOrEmpty(tableNumber)) continue;

                if (!tableOrders.ContainsKey(tableNumber))
                {
                    tableOrders[tableNumber] = new List<ContractorOrder>();
                }

                var existingOrder = tableOrders[tableNumber].FirstOrDefault(o => o.Id == data.Id);
                if (existingOrder != null)
                {
                    // Mavjud buyurtmani yangilash
                    int index = tableOrders[tableNumber].IndexOf(existingOrder);
                    tableOrders[tableNumber][index] = data;
                }
                else
                {
                    // Yangi buyurtma qo'shish
                    tableOrders[tableNumber].Add(data);
                }

                // Update the table button UI silently
                UpdateTableButtonStyleSilently(tableNumber, data);
            }

            // Agar joriy tanlangan stol bo'lsa, UI ni yangilash
            if (!string.IsNullOrEmpty(currentSelectedTable) && currentSelectedTable != "-1")
            {
                if (tableOrders.ContainsKey(currentSelectedTable) && tableOrders[currentSelectedTable].Any())
                {
                    UpdateUIWithoutFlickering(currentSelectedTable);
                }
            }
        }

        // Yangi method: Tugmacha stilini lippillamay yangilash
        private void UpdateTableButtonStyleSilently(string tableNumber, ContractorOrder order)
        {
            foreach (Button btn in tablesPanel.Children)
            {
                if (btn.Tag is not TableButtonData tagData || tagData.TableNumber != tableNumber) continue;

                int productsCount = order.TotalProductsCount;
                int completedProductsCount = order.CompletedProductsCount;
                bool isBusy = productsCount > 0 && (order.StatusId != 3);

                // Tegni sekin yangilash
                tagData.OrderCountText = productsCount > 0 ? $"{completedProductsCount}/{productsCount}" : "0/0";
                tagData.IsBusy = isBusy;
                btn.Tag = tagData;

                // Faqat tanlangan stol bo'lmaganlarga style qo'llash
                if (tableNumber != currentSelectedTable)
                {
                    ApplyTableStyle(btn, isBusy, false);
                }
                
                break;
            }
        }

        // Yangi method: Stol ma'lumotlarini lippillamay yangilash
        private void ProcessTableDataSilently(ContractorOrder data)
        {
            if (data == null) data = new ContractorOrder();

            string tableNumber = currentSelectedTable;

            if (!tableOrders.ContainsKey(tableNumber))
            {
                tableOrders[tableNumber] = new List<ContractorOrder>();
            }

            var existingOrder = tableOrders[tableNumber].FirstOrDefault(o => o.Id == data.Id);
            if (existingOrder != null)
            {
                // Mavjud buyurtmani yangilash
                int index = tableOrders[tableNumber].IndexOf(existingOrder);
                tableOrders[tableNumber][index] = data;
            }
            else
            {
                // Yangi buyurtma qo'shish
                tableOrders[tableNumber].Add(data);
            }
            
            // Update the responsible person name if available
            if (!string.IsNullOrEmpty(data.Responsible))
            {
                lblOfitsiantValue.Text = data.Responsible;
                
                // Update the responsible name in TableButtonData too
                foreach (Button btn in tablesPanel.Children)
                {
                    if (btn.Tag is TableButtonData tagData && tagData.TableNumber == tableNumber)
                    {
                        tagData.ResponsibleName = data.Responsible;
                        btn.Tag = tagData;
                        break;
                    }
                }
            }

            // UI ma'lumotlarini yangilash
            UpdateUIWithoutFlickering(tableNumber);
        }

        // Yangi method: UI ma'lumotlarini lippillamay yangilash
        private void UpdateUIWithoutFlickering(string tableNumber)
        {
            if (!tableOrders.TryGetValue(tableNumber, out var orders) || orders == null || !orders.Any())
                return;
                
            var firstOrder = orders.FirstOrDefault();
            if (firstOrder == null) return;
            
            // Mavjud buyurtma elementlarini olish
            var existingItems = lvItems.Items.OfType<OrderItem>().ToList();
            var itemsToRemove = new List<OrderItem>();
            var allItemsById = firstOrder.Tables?
                .Where(t => t != null && !string.IsNullOrEmpty(t.ProductShortName))
                .ToDictionary(t => t.Id, t => new OrderItem
                {
                    Id = t.Id,
                    ProductShortName = t.ProductShortName ?? "No Name",
                    ContractorRequirement = t.ContractorRequirement ?? "No Details",
                    Quantity = (int)Math.Max(1, t.Quantity),
                    EstimatedPrice = t.EstimatedPrice,
                    Amount = t.Amount,
                    TableNumber = int.TryParse(tableNumber, out int num) ? num : 0
                });
            
            if (allItemsById == null) return;
            
            // UI yangilash, lipillamasdan
            foreach (var existingItem in existingItems)
            {
                if (allItemsById.TryGetValue(existingItem.Id, out var updatedItem))
                {
                    // Mavjud elementni yangilash
                    existingItem.Quantity = updatedItem.Quantity;
                    existingItem.EstimatedPrice = updatedItem.EstimatedPrice;
                    existingItem.Amount = updatedItem.Amount;
                    
                    // Yangilangandan so'ng o'chirib tashlash
                    allItemsById.Remove(existingItem.Id);
                }
                else
                {
                    // Endi mavjud bo'lmagan element - o'chirish uchun belgilash
                    itemsToRemove.Add(existingItem);
                }
            }
            
            // Endi mavjud bo'lmagan elementlarni o'chirish
            foreach (var item in itemsToRemove)
            {
                lvItems.Items.Remove(item);
            }
            
            // Yangi elementlarni qo'shish
            foreach (var item in allItemsById.Values)
            {
                lvItems.Items.Add(item);
            }
            
            // Qolgan UI elementlarini yangilash
            int serviceFeePercentage = 0;
            if (firstOrder.AdditionalPayments != null && firstOrder.AdditionalPayments.Count > 0)
            {
                serviceFeePercentage = firstOrder.AdditionalPayments[0].AdditionalPercentage;
                AppSettings.ServiceFeePercentage = serviceFeePercentage;
            }
            
            // Chegirma qiymatlarini yangilash
            if (firstOrder.SalePercent > 0)
            {
                discountPercentage = firstOrder.SalePercent;
                discountAmount = firstOrder.SaleAmount;
                isDiscountPercentage = true;
            }
            else if (firstOrder.SaleAmount > 0)
            {
                discountAmount = firstOrder.SaleAmount;
                discountPercentage = 0;
                isDiscountPercentage = false;
            }
            
            // UI elementlarini yangilash
            lblAmountValue.Text = $"{AppSettings.FormatCurrency(firstOrder.Amount)} so'm";
            lblAdditinalPaymentValue.Text = $"{AppSettings.FormatCurrency(firstOrder.AdditinalPayment)} so'm ({serviceFeePercentage}%)";
            
            if (firstOrder.SaleAmount > 0 || firstOrder.SalePercent > 0)
            {
                if (firstOrder.SalePercent > 0)
                {
                    lblDiscountValue.Text = $"{AppSettings.FormatCurrency(firstOrder.SaleAmount)} so'm ({firstOrder.SalePercent}%)";
                }
                else
                {
                    lblDiscountValue.Text = $"{AppSettings.FormatCurrency(firstOrder.SaleAmount)} so'm";
                }
            }
            
            lblTotalAmountValue.Text = $"{AppSettings.FormatCurrency(firstOrder.TotalAmount)} so'm";
            
            if (!string.IsNullOrEmpty(firstOrder.EstimatedPaymentType))
            {
                lblPaymentMethod.Text = firstOrder.EstimatedPaymentType;
            }
            
            // ListView ni yangilash
            lvItems.Items.Refresh();
        }

        // Yangi method: To'lov turi ma'lumotlarini lippillamay yangilash
        private async Task UpdateServiceFeePercentageFromApi(bool updateUI = true)
        {
            try
            {
                string token = await EnsureValidTokenAsync();
                if (string.IsNullOrEmpty(token)) return;
                
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                    client.DefaultRequestHeaders.Add("accept", "text/plain");
                    
                    var requestData = new
                    {
                        search = (string)null,
                        sortBy = "id",
                        orderType = "DESC",
                        page = 0,
                        pageSize = 0
                    };
                    
                    var content = new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json");
                    HttpResponseMessage response = await client.PostAsync("https://crm-api.webase.uz/crm/AdditionalPayment/GetList", content);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        string jsonResponse = await response.Content.ReadAsStringAsync();
                        var result = JsonConvert.DeserializeObject<AdditionalPaymentResponse>(jsonResponse);
                        
                        if (result != null && result.Rows != null && result.Rows.Count > 0)
                        {
                            var firstPayment = result.Rows[0];
                            AppSettings.ServiceFeePercentage = firstPayment.Percentage;
                            
                            // Faqat kerak bo'lganda UI ni yangilash
                            if (updateUI && !string.IsNullOrEmpty(currentSelectedTable) && currentSelectedTable != "-1")
                            {
                                UpdateUIWithoutFlickering(currentSelectedTable);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating service fee from API: {ex.Message}");
            }
        }

        // Yangi method: Tugmachalar holatini lippillamay yangilash
        private void UpdateButtonStatesSilently()
        {
            bool hasItems = false;
            bool hasPaymentMethod = false;

            if (!string.IsNullOrEmpty(currentSelectedTable) && currentSelectedTable != "-1" &&
                tableOrders.TryGetValue(currentSelectedTable, out var orders) && orders != null && orders.Any())
            {
                hasItems = true;
                var order = orders.FirstOrDefault();
                if (order != null)
                {
                    hasPaymentMethod = !string.IsNullOrEmpty(order.EstimatedPaymentType);
                }
            }

            // Tugmachalar holatini yangilash
            if (btnChangePaymentMethod.IsEnabled != hasItems)
                btnChangePaymentMethod.IsEnabled = hasItems;
                
            if (btnPrint.IsEnabled != (hasItems && hasPaymentMethod))
                btnPrint.IsEnabled = hasItems && hasPaymentMethod;
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            _ = SmoothAutoRefresh();
        }

        private void btnPrint_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(currentSelectedTable) || currentSelectedTable == "-1")
            {
                MessageBox.Show("Chop etish uchun stol tanlang", "Xabar", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!tableOrders.TryGetValue(currentSelectedTable, out var orders) || orders == null || !orders.Any())
            {
                MessageBox.Show("Tanlangan stolda buyurtmalar mavjud emas", "Xabar", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var currentOrder = orders.FirstOrDefault();
                if (currentOrder == null)
                {
                    MessageBox.Show("Buyurtma ma'lumotlari topilmadi", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var printOrder = CreatePrintOrder(currentOrder);
                _printer.PrintText(printOrder);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Chop etishda xatolik yuz berdi: {ex.Message}", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private PrintOrder CreatePrintOrder(ContractorOrder order)
        {
            // Get service fee percentage from backend
            int additionalPercentage = 0;
            
            if (order.AdditionalPayments != null && order.AdditionalPayments.Count > 0)
            {
                additionalPercentage = order.AdditionalPayments[0].AdditionalPercentage;
            }
            
            // Use all values from the backend directly without recalculation
            return new PrintOrder
            {
                TableNumber = currentSelectedTable,
                RestaurantName = order.OrganizationAreasOfActivity ?? "",
                WaiterName = order.Responsible ?? "",
                OrderDate = order.DocDate ?? "",
                OrderTime = order.DocTime ?? "",
                CheckNumber = order.DocNumber ?? "",
                Orders = order.Tables?
                    .Where(item => item != null)
                    .Select(item => new OrderItem
                    {
                        Id = item.Id,
                        ProductShortName = item.ProductShortName ?? "No Name",
                        ContractorRequirement = item.ContractorRequirement ?? "No Details",
                        Quantity = (int)Math.Max(1, item.Quantity),
                        EstimatedPrice = Math.Round(Math.Max(0, item.EstimatedPrice), 2),
                        Amount = Math.Round(Math.Max(0, item.Amount), 2),
                        TableNumber = int.TryParse(currentSelectedTable, out int num) ? num : 0
                    })
                    .Where(item => !string.IsNullOrEmpty(item.ProductShortName))
                    .ToList() ?? new List<OrderItem>(),
                TotalAmount = order.Amount,
                ServiceFee = order.AdditinalPayment,
                DiscountAmount = order.SaleAmount,
                DiscountPercentage = order.SalePercent,
                IsDiscountPercentage = order.SalePercent > 0,
                GrandTotal = order.TotalAmount,
                AdditionalPercentage = additionalPercentage,
                PaymentTypeText = order.EstimatedPaymentType ?? "Naqd"
            };
        }

        private void btnLogout_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Settings.Default.AccessToken = null;
                Settings.Default.RefreshToken = null;
                Settings.Default.accessTokenExpireAt = null;
                Settings.Default.Save();

                if (timer != null) timer.Stop();
                if (timeTimer != null) timeTimer.Stop();

                // For full screen application, directly close it
                MainWindow mainWindow = new MainWindow();
                mainWindow.Show();
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Tizimdan chiqishda xatolik: {ex.Message}", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnSettings_Click(object sender, RoutedEventArgs e)
        {
            // Create blur effect for the main window
            System.Windows.Media.Effects.BlurEffect blurEffect = new System.Windows.Media.Effects.BlurEffect
            {
                Radius = 10,
                KernelType = System.Windows.Media.Effects.KernelType.Gaussian
            };

            // Apply blur effect to the content
            this.Effect = blurEffect;

            // Create semi-transparent overlay
            Grid overlay = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0)),
                Opacity = 0.5,
                IsHitTestVisible = true
            };
            
            // Add overlay to the window
            Grid.SetRowSpan(overlay, 100);
            Grid.SetColumnSpan(overlay, 100);
            Grid.SetZIndex(overlay, 1000);
            
            // Get main grid from the window
            var mainGrid = this.Content as Grid;
            mainGrid?.Children.Add(overlay);

            // Create and show settings window    
            var settingsWindow = new Pages.Windows.ServiceFeeSettings();
            settingsWindow.Owner = this;
            
            // When settings window closes, remove blur and overlay
            settingsWindow.Closed += (s, args) => 
            {
                this.Effect = null;
                mainGrid?.Children.Remove(overlay);
            };
            
            // Show settings dialog
            bool? result = settingsWindow.ShowDialog();
            
            // If the dialog result is true (settings were saved), refresh the data to update service fee
            if (result == true)
            {
                // Refresh the currently selected table's data if any table is selected
                if (!string.IsNullOrEmpty(currentSelectedTable) && currentSelectedTable != "-1")
                {
                    LoadTableOrders(currentSelectedTable);
                }
            }
        }

        private void btnBackToTop_Click(object sender, RoutedEventArgs e)
        {
            if (tableScrollViewer != null)
            {
                tableScrollViewer.ScrollToTop();
                btnBackToTop.Visibility = Visibility.Collapsed;
            }
        }

        private void tableScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalOffset > 0)
            {
                btnBackToTop.Visibility = Visibility.Visible;
            }
            else
            {
                btnBackToTop.Visibility = Visibility.Collapsed;
            }
        }

        private async void btnCompleteOrder_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(currentSelectedTable) || currentSelectedTable == "-1")
            {
                MessageBox.Show("Yakunlash uchun stol tanlang", "Xabar", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!tableOrders.TryGetValue(currentSelectedTable, out var orders) || orders == null || !orders.Any())
            {
                MessageBox.Show("Tanlangan stolda buyurtmalar mavjud emas", "Xabar", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var tables = await GetTablesList();
                if (tables?.Rows == null) return;

                var tableInfo = tables.Rows.FirstOrDefault(r => r.FirstName == currentSelectedTable);
                if (tableInfo?.NotCompletedOrderId == null) return;

                MessageBoxResult result = MessageBox.Show(
                    "Buyurtmani yakunlashni xohlaysizmi?",
                    "Tasdiqlash",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                );

                if (result == MessageBoxResult.Yes)
                {
                    bool isComplete = await CompleteOrder(tableInfo.NotCompletedOrderId);
                    if (isComplete)
                    {
                        if (tableOrders.ContainsKey(currentSelectedTable))
                        {
                            tableOrders[currentSelectedTable].Clear();
                        }

                        LoadTableOrders(currentSelectedTable);

                        var btn = tablesPanel.Children.OfType<Button>()
                            .FirstOrDefault(b => b.Tag is TableButtonData td && td.TableNumber == currentSelectedTable);

                        if (btn != null && btn.Tag is TableButtonData btnData)
                        {
                            btnData.IsBusy = false;
                            btnData.OrderCountText = "0/0";
                            btn.Tag = btnData;
                            ApplyTableStyle(btn, false, true);
                        }

                        MessageBox.Show("Buyurtma muvaffaqiyatli yakunlandi", "Xabar", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("Buyurtma yakunlanmadi. Qayta urinib ko'ring.", "Xabar", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Buyurtmani yakunlashda xatolik: {ex.Message}", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task<bool> CompleteOrder(int? orderId)
        {
            if (orderId == null) return false;

            try
            {
                var result = await SendApiRequestAsync<ContractorOrder>(
                    HttpMethod.Get,
                    $"https://crm-api.webase.uz/crm/ContractorOrder/CompleteWithoutCashDocument/{orderId}"
                );

                if (result != null)
                {
                    ProcessApiData(result);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Buyurtmani yakunlashda xatolik: {ex.Message}", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private async Task<string> EnsureValidTokenAsync()
        {
            string token = Settings.Default.AccessToken;

            if (!string.IsNullOrEmpty(token))
            {
                if (Settings.Default.accessTokenExpireAt != null &&
                    DateTime.TryParse(Settings.Default.accessTokenExpireAt, out DateTime expireDate) &&
                    expireDate > DateTime.Now.AddMinutes(5))
                {
                    return token;
                }
            }

            string refreshToken = Settings.Default.RefreshToken;

            if (string.IsNullOrEmpty(refreshToken))
            {
                new MainWindow().Show();
                Close();
                return null;
            }

            try
            {
                var refreshRequest = new { refreshToken };
                var refreshContent = new StringContent(JsonConvert.SerializeObject(refreshRequest), Encoding.UTF8, "application/json");
                HttpResponseMessage refreshResponse = await _httpClient.PostAsync("https://crm-api.webase.uz/account/RefreshToken", refreshContent);

                if (refreshResponse.IsSuccessStatusCode)
                {
                    string jsonResponse = await refreshResponse.Content.ReadAsStringAsync();
                    var newTokenResponse = JsonConvert.DeserializeObject<LoginResponse>(jsonResponse);
                    Settings.Default.AccessToken = newTokenResponse?.AccessToken;
                    Settings.Default.RefreshToken = newTokenResponse?.RefreshToken;
                    Settings.Default.Save();
                    return newTokenResponse?.AccessToken;
                }
                else
                {
                    MessageBox.Show("Sizning sessiyangliz muddati tugagan. Iltimos, qaytadan kiring.", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Warning);
                    new MainWindow().Show();
                    Close();
                    return null;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Token yangilashda xatolik: {ex.Message}", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        private async Task<T> SendApiRequestAsync<T>(HttpMethod method, string endpoint, object requestData = null)
        {
            try
            {
                string token = await EnsureValidTokenAsync();
                if (string.IsNullOrEmpty(token)) return default;

                var request = new HttpRequestMessage(method, endpoint);
                request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/plain"));
                request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("*/*"));
                request.Headers.Add("accept-language", "ru-RU,ru;q=0.9,uz-UZ;q=0.8,uz;q=0.7,en-US;q=0.6,en;q=0.5");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                request.Headers.Add("origin", "https://crm.webase.uz");
                request.Headers.Add("priority", "u=1, i");
                request.Headers.Add("referer", "https://crm.webase.uz/");
                request.Headers.Add("sec-ch-ua", "\"Not(A:Brand\";v=\"99\", \"Google Chrome\";v=\"133\", \"Chromium\";v=\"133\"");
                request.Headers.Add("sec-ch-ua-mobile", "?0");
                request.Headers.Add("sec-ch-ua-platform", "\"Windows\"");
                request.Headers.Add("sec-fetch-dest", "empty");
                request.Headers.Add("sec-fetch-mode", "cors");
                request.Headers.Add("sec-fetch-site", "same-site");
                request.Headers.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/133.0.0.0 Safari/537.36");

                if (requestData != null)
                {
                    var content = new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json");
                    request.Content = content;
                }

                HttpResponseMessage response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    var data = JsonConvert.DeserializeObject<T>(jsonResponse);
                    return data;
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"API xatolik: {response.StatusCode} - {errorContent}");
                    return default;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"API so'rovida xatolik: {ex.Message}");
                return default;
            }
        }

        private void btnChangePaymentMethod_Click(object sender, RoutedEventArgs e)
        {
            OpenPaymentMethodSelection();
        }

        private async void OpenPaymentMethodSelection()
        {
            var paymentTypesWindow = new PaymentTypes();
            if (paymentTypesWindow.ShowDialog() == true)
            {
                var selectedMethod = paymentTypesWindow.SelectedPaymentMethod;

                // Agar stol tanlanmagan bo'lsa, xabar chiqaramiz
                if (string.IsNullOrEmpty(currentSelectedTable) || currentSelectedTable == "-1")
                {
                    MessageBox.Show("Iltimos, avval stol tanlang!", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Stolga tegishli buyurtma mavjudligini tekshiramiz
                if (!tableOrders.TryGetValue(currentSelectedTable, out var orders) || orders == null || !orders.Any())
                {
                    MessageBox.Show("Tanlangan stolda buyurtma mavjud emas!", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var order = orders.FirstOrDefault();
                if (order == null)
                {
                    MessageBox.Show("Buyurtma ma'lumotlari topilmadi!", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                try
                {
                    // To'lov turi tanlangan bo'lsa
                    if (selectedMethod != null)
                    {
                        using (HttpClient client = new HttpClient())
                        {
                            string apiUrl = $"https://crm-api.webase.uz/crm/ContractorOrder/ChangePaymentType?orderId={order.Id}&paymentTypeId={selectedMethod.Value}";
                            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/plain"));
                            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("*/*"));
                            client.DefaultRequestHeaders.Add("accept-language", "ru-RU,ru;q=0.9,uz-UZ;q=0.8,uz;q=0.7,en-US;q=0.6,en;q=0.5");
                            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Settings.Default.AccessToken);
                            client.DefaultRequestHeaders.Add("origin", "https://crm.webase.uz");
                            client.DefaultRequestHeaders.Add("priority", "u=1, i");
                            client.DefaultRequestHeaders.Add("referer", "https://crm.webase.uz/");
                            client.DefaultRequestHeaders.Add("sec-ch-ua", "\"Not(A:Brand\";v=\"99\", \"Google Chrome\";v=\"133\", \"Chromium\";v=\"133\"");
                            client.DefaultRequestHeaders.Add("sec-ch-ua-mobile", "?0");
                            client.DefaultRequestHeaders.Add("sec-ch-ua-platform", "\"Windows\"");
                            client.DefaultRequestHeaders.Add("sec-fetch-dest", "empty");
                            client.DefaultRequestHeaders.Add("sec-fetch-mode", "cors");
                            client.DefaultRequestHeaders.Add("sec-fetch-site", "same-site");
                            client.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/133.0.0.0 Safari/537.36");

                            HttpResponseMessage response = await client.PostAsync(apiUrl,null);

                            if (response.IsSuccessStatusCode)
                            {
                                // Muvaffaqiyatli bo'lsa, ContractorOrder ni yangilash
                                order.EstimatedPaymentType = selectedMethod.Text;
                                lblPaymentMethod.Text = selectedMethod.Text; // Tanlangan to'lov turini ko'rsatish
                                lblPaymentMethod.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
                                PaymentMethodBorder.Background = new LinearGradientBrush
                                {
                                    StartPoint = new Point(0, 0),
                                    EndPoint = new Point(1, 1),
                                    GradientStops = new GradientStopCollection
                                    {
                                        new GradientStop(System.Windows.Media.Color.FromRgb(200, 230, 201), 0),
                                        new GradientStop(System.Windows.Media.Color.FromRgb(165, 214, 167), 1)
                                    }
                                };
                            }
                            else
                            {
                                MessageBox.Show("To'lov turini o'zgartirishda xatolik yuz berdi!", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
                                return;
                            }
                        }

                    }
                    
                    UpdateButtonStates();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"To'lov turini o'zgartirishda xatolik: {ex.Message}", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void UpdateButtonStates()
        {
            bool hasItems = lvItems.Items.Count > 0; // Stolda buyurtma borligini tekshirish
            bool hasPaymentMethod = false;

            if (hasItems && tableOrders.TryGetValue(currentSelectedTable, out var orders) && orders != null && orders.Any())
            {
                var order = orders.FirstOrDefault();
                if (order != null)
                {
                    hasPaymentMethod = !string.IsNullOrEmpty(order.EstimatedPaymentType); // To'lov turi mavjudligini tekshirish
                }
            }

            // Agar buyurtma bo'lmasa, tahrirlash va chek chiqarish tugmalari faolsiz bo'ladi
            btnChangePaymentMethod.IsEnabled = hasItems;
            btnPrint.IsEnabled = hasItems && hasPaymentMethod;
        }

        private void ProcessApiDataWithoutClearing(ContractorOrder data)
        {
            if (data == null || data.Tables == null || !data.Tables.Any()) return;

            foreach (var table in data.Tables.Where(t => t != null))
            {
                string tableNumber = table.OrderNumber.ToString();
                if (string.IsNullOrEmpty(tableNumber)) continue;

                if (!tableOrders.ContainsKey(tableNumber))
                {
                    tableOrders[tableNumber] = new List<ContractorOrder>();
                }

                var existingOrder = tableOrders[tableNumber].FirstOrDefault(o => o.Id == data.Id);
                if (existingOrder != null)
                {
                    int index = tableOrders[tableNumber].IndexOf(existingOrder);
                    tableOrders[tableNumber][index] = data;
                }
                else
                {
                    tableOrders[tableNumber].Add(data);
                }

                UpdateTableButtonStyle(tableNumber, data);
            }

            // Agar joriy tanlangan stol bo'lsa, UI ni yangilash
            if (!string.IsNullOrEmpty(currentSelectedTable) && currentSelectedTable != "-1")
            {
                // Faqat stolga tegishli buyurtmalar mavjud bo'lsa, yangilaymiz
                if (tableOrders.ContainsKey(currentSelectedTable) && tableOrders[currentSelectedTable].Any())
                {
                    // ListView ni yangilash
                    UpdateExistingListView(currentSelectedTable);
                }
            }
        }

        private async Task UpdateExistingTableData(int notCompletedOrderId)
        {
            try
            {
                var data = await ContractorOrderGetById(notCompletedOrderId);
                if (data == null) return;

                if (!tableOrders.ContainsKey(currentSelectedTable))
                {
                    tableOrders[currentSelectedTable] = new List<ContractorOrder>();
                }

                // Agar buyurtma mavjud bo'lsa, yangilaymiz
                var existingOrder = tableOrders[currentSelectedTable].FirstOrDefault(o => o.Id == data.Id);
                if (existingOrder != null)
                {
                    int index = tableOrders[currentSelectedTable].IndexOf(existingOrder);
                    tableOrders[currentSelectedTable][index] = data;
                }
                else
                {
                    tableOrders[currentSelectedTable].Add(data);
                }

                // ListView ni yangilash
                UpdateExistingListView(currentSelectedTable);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UpdateExistingTableData error: {ex.Message}");
            }
        }

        private void UpdateExistingListView(string tableNumber)
        {
            if (!tableOrders.TryGetValue(tableNumber, out var orders) || orders == null || !orders.Any()) return;

            // Joriy ListView dagi oxirgi indeksni olish
            int lastIndex = lvItems.Items.Count;

            var order = orders.FirstOrDefault();
            if (order == null) return;

            // Yangi buyurtma elementlarini ListView ga qo'shish
            var orderItems = order.Tables?
                .Where(t => t != null && !string.IsNullOrEmpty(t.ProductShortName))
                .Select((item, index) => new OrderItem
                {
                    Index = lastIndex + index + 1,
                    Id = item.Id,
                    ProductShortName = item.ProductShortName ?? "No Name",
                    ContractorRequirement = item.ContractorRequirement ?? "No Details",
                    Quantity = (int)Math.Max(1, item.Quantity),
                    EstimatedPrice = item.EstimatedPrice,
                    Amount = item.Amount,
                    TableNumber = int.TryParse(tableNumber, out int num) ? num : 0
                })
                .ToList();

            // Faqat yangi elementlarni qo'shamiz
            if (orderItems != null)
            {
                foreach (var item in orderItems)
                {
                    if (!lvItems.Items.OfType<OrderItem>().Any(i => i.Id == item.Id))
                    {
                        lvItems.Items.Add(item);
                    }
                }
            }

            // Get service fee percentage from backend
            int serviceFeePercentage = 0;
            
            if (order.AdditionalPayments != null && order.AdditionalPayments.Count > 0)
            {
                serviceFeePercentage = order.AdditionalPayments[0].AdditionalPercentage;
            }

            // Update discount values from the server response directly
            if (order.SalePercent > 0)
            {
                discountPercentage = order.SalePercent;
                discountAmount = order.SaleAmount;
                isDiscountPercentage = true;
            }
            else if (order.SaleAmount > 0)
            {
                discountAmount = order.SaleAmount;
                discountPercentage = 0;
                isDiscountPercentage = false;
            }
            else
            {
                discountAmount = 0;
                discountPercentage = 0;
                isDiscountPercentage = true;
            }

            // Use backend values directly - no recalculation
            lblAmountValue.Text = $"{AppSettings.FormatCurrency(order.Amount)} so'm";
            lblAdditinalPaymentValue.Text = $"{AppSettings.FormatCurrency(order.AdditinalPayment)} so'm ({serviceFeePercentage}%)";
            
            // Show discount from backend
            if (order.SaleAmount > 0 || order.SalePercent > 0)
            {
                if (order.SalePercent > 0)
                {
                    lblDiscountValue.Text = $"{AppSettings.FormatCurrency(order.SaleAmount)} so'm ({order.SalePercent}%)";
                }
                else
                {
                    lblDiscountValue.Text = $"{AppSettings.FormatCurrency(order.SaleAmount)} so'm";
                }
            }
            
            lblTotalAmountValue.Text = $"{AppSettings.FormatCurrency(order.TotalAmount)} so'm";
            
            if (!string.IsNullOrEmpty(order.EstimatedPaymentType))
            {
                lblPaymentMethod.Text = order.EstimatedPaymentType;
            }
            
            // ListView ni yangilash
            lvItems.Items.Refresh();
        }

        private async void btnChangeDiscount_Click(object sender, RoutedEventArgs e)
        {
            // Agar stol tanlanmagan bo'lsa, xabar chiqaramiz
            if (string.IsNullOrEmpty(currentSelectedTable) || currentSelectedTable == "-1")
            {
                MessageBox.Show("Iltimos, avval stol tanlang!", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Stolga tegishli buyurtma mavjudligini tekshiramiz
            if (!tableOrders.TryGetValue(currentSelectedTable, out var orders) || orders == null || !orders.Any())
            {
                MessageBox.Show("Tanlangan stolda buyurtma mavjud emas!", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var order = orders.FirstOrDefault();
            if (order == null)
            {
                MessageBox.Show("Buyurtma ma'lumotlari topilmadi!", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                // Create blur effect for the main window
                System.Windows.Media.Effects.BlurEffect blurEffect = new System.Windows.Media.Effects.BlurEffect
                {
                    Radius = 10,
                    KernelType = System.Windows.Media.Effects.KernelType.Gaussian
                };

                // Apply blur effect to the content
                this.Effect = blurEffect;

                // Create semi-transparent overlay
                Grid overlay = new Grid
                {
                    Background = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0)),
                    Opacity = 0.5,
                    IsHitTestVisible = true
                };
                
                // Add overlay to the window
                Grid.SetRowSpan(overlay, 100);
                Grid.SetColumnSpan(overlay, 100);
                Grid.SetZIndex(overlay, 1000);
                
                // Get main grid from the window
                var mainGrid = this.Content as Grid;
                mainGrid?.Children.Add(overlay);

                // Open the discount settings window
                var discountWindow = new DiscountSettings(order.Amount, discountAmount, isDiscountPercentage);
                discountWindow.Owner = this;
                
                // When discount window closes, remove blur and overlay
                discountWindow.Closed += (s, args) => 
                {
                    this.Effect = null;
                    mainGrid?.Children.Remove(overlay);
                };
                
                bool? result = discountWindow.ShowDialog();
                
                if (result == true)
                {
                    try
                    {
                        // Show loading overlay during API operation
                        loadingOverlay.Visibility = Visibility.Visible;
                        
                        // Get current token for API request
                        string token = await EnsureValidTokenAsync();
                        if (string.IsNullOrEmpty(token))
                        {
                            loadingOverlay.Visibility = Visibility.Collapsed;
                            MessageBox.Show("Avtorizatsiya xatoligi. Iltimos, qayta kiring.", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                        
                        if (discountWindow.DiscountApplied)
                        {
                            // Save the discount settings locally
                            discountAmount = discountWindow.DiscountAmount;
                            isDiscountPercentage = discountWindow.IsPercentage;
                            discountPercentage = discountWindow.DiscountPercentage;
                            
                            // Send discount to backend
                            var updatedOrder = await ApplyDiscountToOrder(
                                order,
                                discountWindow.SalePercent, 
                                discountWindow.SaleAmount,
                                token);
                                
                            if (updatedOrder != null)
                            {
                                // Replace the current order with the updated one from backend
                                int index = tableOrders[currentSelectedTable].IndexOf(order);
                                if (index >= 0)
                                {
                                    tableOrders[currentSelectedTable][index] = updatedOrder;
                                }
                                
                                // Update UI
                                LoadTableOrders(currentSelectedTable);
                                lblDiscountValue.Text = $"{AppSettings.FormatCurrency(discountAmount)} so'm";
                                MessageBox.Show("Chegirma muvaffaqiyatli saqlandi!", "Muvaffaqiyatli", MessageBoxButton.OK, MessageBoxImage.Information);
                            }
                            else
                            {
                                // If API call failed, still update UI with local discount values
                                lblDiscountValue.Text = $"{AppSettings.FormatCurrency(discountAmount)} so'm";
                                UpdateTotalWithDiscount();
                            }
                        }
                        else
                        {
                            // Reset discount on backend
                            var updatedOrder = await ApplyDiscountToOrder(order, 0, 0, token);
                            
                            if (updatedOrder != null)
                            {
                                // Replace the current order with the updated one from backend
                                int index = tableOrders[currentSelectedTable].IndexOf(order);
                                if (index >= 0)
                                {
                                    tableOrders[currentSelectedTable][index] = updatedOrder;
                                }
                                
                                // Reset discount if user clicked reset
                                discountAmount = 0;
                                isDiscountPercentage = true;
                                discountPercentage = 0;
                                lblDiscountValue.Text = "0 so'm";
                                
                                // Update UI
                                LoadTableOrders(currentSelectedTable);
                                MessageBox.Show("Chegirma bekor qilindi!", "Muvaffaqiyatli", MessageBoxButton.OK, MessageBoxImage.Information);
                            }
                            else
                            {
                                // If API call failed, still reset local discount values
                                discountAmount = 0;
                                isDiscountPercentage = true;
                                discountPercentage = 0;
                                lblDiscountValue.Text = "0 so'm";
                                UpdateTotalWithDiscount();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Chegirmani saqlashda xatolik: {ex.Message}", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally
                    {
                        // Hide loading overlay
                        loadingOverlay.Visibility = Visibility.Collapsed;
                    }
                }
            }
            catch (Exception ex)
            {
                // Hide loading in case of error
                loadingOverlay.Visibility = Visibility.Collapsed;
                MessageBox.Show($"Chegirma sozlashda xatolik: {ex.Message}", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private async Task<ContractorOrder> ApplyDiscountToOrder(ContractorOrder order, decimal salePercent, decimal saleAmount, string token)
        {
            try
            {
                // Create a copy of the current order to update with discount
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
                    currencyId = order.CurrencyId,
                    isForManReport = order.IsForManReport,
                    organizationAreasOfActivityId = order.OrganizationAreasOfActivityId,
                    ctWarehouseId = order.CtWarehouseId,
                    contractorId = order.ContractorId,
                    isCreateManufacturingReport = order.IsCreateManufacturingReport,
                    details = order.Details,
                    salePercent = (int)Math.Round(salePercent),
                    saleAmount = saleAmount,
                    // Keep existing items
                    tables = order.Tables?.Select(t => new
                    {
                        id = t.Id,
                        orderNumber = t.OrderNumber,
                        productId = t.ProductId,
                        contractorRequirement = t.ContractorRequirement,
                        estimatedPrice = t.EstimatedPrice,
                        quantity = t.Quantity,
                        defectedQuantity = t.DefectedQuantity,
                        amount = t.Amount,
                        defectedAmount = t.DefectedAmount,
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
                    }).ToList()
                };

                // Send API request to update order with discount
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
                        return updatedOrder;
                    }
                    else
                    {
                        // Handle error response
                        string errorResponse = await response.Content.ReadAsStringAsync();
                        throw new HttpRequestException($"API error: {response.StatusCode}\n{errorResponse}");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Chegirmani saqlashda xatolik: {ex.Message}", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        private void UpdateTotalWithDiscount()
        {
            if (string.IsNullOrEmpty(currentSelectedTable) || currentSelectedTable == "-1")
                return;

            if (!tableOrders.TryGetValue(currentSelectedTable, out var orders) || orders == null || !orders.Any())
                return;
            
            var order = orders.FirstOrDefault();
            if (order == null)
                return;
                
            // Get original amounts
            decimal subtotal = order.Amount;
            decimal serviceFee = order.AdditinalPayment;
            
            // Get service fee percentage
            int serviceFeePercentage = 0;
            if (order.AdditionalPayments != null && order.AdditionalPayments.Count > 0)
            {
                serviceFeePercentage = order.AdditionalPayments[0].AdditionalPercentage;
            }
            
            // Update UI display
            lblAmountValue.Text = $"{AppSettings.FormatCurrency(subtotal)} so'm";
            lblAdditinalPaymentValue.Text = $"{AppSettings.FormatCurrency(serviceFee)} so'm ({serviceFeePercentage}%)";
            
            // Show discount if any
            if (discountAmount > 0)
            {
                if (isDiscountPercentage)
                {
                    lblDiscountValue.Text = $"{AppSettings.FormatCurrency(discountAmount)} so'm ({discountPercentage}%)";
                }
                else
                {
                    lblDiscountValue.Text = $"{AppSettings.FormatCurrency(discountAmount)} so'm";
                }
            }
            else
            {
                lblDiscountValue.Text = "0 so'm";
            }
            
            // Use total amount from server if available, otherwise calculate
            lblTotalAmountValue.Text = $"{AppSettings.FormatCurrency(order.TotalAmount)} so'm";
        }

        private async void btnRemoveDefectItem_Click(object sender, RoutedEventArgs e)
        {
            // Get the item ID from the button's tag
            if (sender is Button button && button.Tag != null)
            {
                string itemIdStr = button.Tag.ToString();
                
                // Parse the item ID
                if (!int.TryParse(itemIdStr, out int itemId))
                {
                    MessageBox.Show("Noto'g'ri mahsulot identifikatori", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                try
                {
                    // Get the current table and order
                    if (string.IsNullOrEmpty(currentSelectedTable) || currentSelectedTable == "-1")
                    {
                        MessageBox.Show("Iltimos, avval stol tanlang!", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (!tableOrders.TryGetValue(currentSelectedTable, out var orders) || orders == null || !orders.Any())
                    {
                        MessageBox.Show("Tanlangan stolda buyurtma mavjud emas!", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var order = orders.FirstOrDefault();
                    if (order == null || order.Tables == null)
                    {
                        MessageBox.Show("Buyurtma ma'lumotlari topilmadi!", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // Find the item in the order
                    var item = order.Tables.FirstOrDefault(t => t.Id == itemId);
                    if (item == null)
                    {
                        MessageBox.Show("Tanlangan mahsulot topilmadi!", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // Check if quantity is more than 1
                    int totalQuantity = (int)Math.Max(1, item.Quantity);
                    int defectiveQuantity = totalQuantity;
                    bool removeEntireItem = true;

                    if (totalQuantity > 1)
                    {
                        // Create a simple input dialog for quantity
                        Window quantityWindow = new Window
                        {
                            Title = "Yaroqsiz miqdorini kiriting",
                            Width = 400,
                            Height = 260,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner,
                            Owner = this,
                            ResizeMode = ResizeMode.NoResize,
                            Background = new SolidColorBrush(Colors.White),
                            WindowStyle = WindowStyle.SingleBorderWindow
                        };

                        // Create main grid with padding
                        Grid mainGrid = new Grid { Margin = new Thickness(20) };
                        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                        // Add header text
                        TextBlock headerText = new TextBlock
                        {
                            Text = $"\"{item.ProductShortName}\" - jami soni: {totalQuantity}",
                            FontSize = 14,
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 0, 0, 20),
                            FontWeight = FontWeights.SemiBold
                        };
                        Grid.SetRow(headerText, 0);
                        mainGrid.Children.Add(headerText);

                        // Add label
                        TextBlock labelText = new TextBlock
                        {
                            Text = "Yaroqsiz mahsulotlar soni:",
                            FontSize = 14,
                            Margin = new Thickness(0, 0, 0, 10)
                        };
                        Grid.SetRow(labelText, 1);
                        mainGrid.Children.Add(labelText);

                        // Add numeric input
                        TextBox quantityInput = new TextBox
                        {
                            Text = "1",
                            FontSize = 16,
                            Height = 40,
                            Padding = new Thickness(10, 8, 10, 8),
                            Margin = new Thickness(0, 0, 0, 20),
                            VerticalContentAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Stretch
                        };
                        
                        // Allow only numbers
                        quantityInput.PreviewTextInput += (s, args) =>
                        {
                            args.Handled = !int.TryParse(args.Text, out _);
                        };
                        Grid.SetRow(quantityInput, 2);
                        mainGrid.Children.Add(quantityInput);

                        // Add buttons
                        Grid buttonGrid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
                        buttonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        buttonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                        Button cancelButton = new Button
                        {
                            Content = "Bekor qilish",
                            Height = 40,
                            Margin = new Thickness(0, 0, 5, 0),
                            FontSize = 14,
                            Background = new SolidColorBrush(Color.FromRgb(244, 67, 54)),
                            Foreground = new SolidColorBrush(Colors.White),
                            BorderThickness = new Thickness(0)
                        };
                        
                        // Add corner radius
                        cancelButton.Resources = new ResourceDictionary();
                        Style cancelStyle = new Style(typeof(Border));
                        cancelStyle.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(4)));
                        cancelButton.Resources.Add(typeof(Border), cancelStyle);
                        
                        cancelButton.Click += (s, args) =>
                        {
                            quantityWindow.DialogResult = false;
                        };
                        Grid.SetColumn(cancelButton, 0);
                        buttonGrid.Children.Add(cancelButton);

                        Button confirmButton = new Button
                        {
                            Content = "Tasdiqlash",
                            Height = 40,
                            Margin = new Thickness(5, 0, 0, 0),
                            FontSize = 14,
                            Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                            Foreground = new SolidColorBrush(Colors.White),
                            BorderThickness = new Thickness(0)
                        };
                        
                        // Add corner radius
                        confirmButton.Resources = new ResourceDictionary();
                        Style confirmStyle = new Style(typeof(Border));
                        confirmStyle.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(4)));
                        confirmButton.Resources.Add(typeof(Border), confirmStyle);
                        
                        confirmButton.Click += (s, args) =>
                        {
                            if (int.TryParse(quantityInput.Text, out int enteredQuantity) && 
                                enteredQuantity > 0 && enteredQuantity <= totalQuantity)
                            {
                                defectiveQuantity = enteredQuantity;
                                quantityWindow.DialogResult = true;
                            }
                            else
                            {
                                MessageBox.Show("Iltimos, to'g'ri son kiriting!", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        };
                        Grid.SetColumn(confirmButton, 1);
                        buttonGrid.Children.Add(confirmButton);

                        Grid.SetRow(buttonGrid, 3);
                        mainGrid.Children.Add(buttonGrid);

                        quantityWindow.Content = mainGrid;
                        
                        // Apply blur effect to main window
                        System.Windows.Media.Effects.BlurEffect blurEffect = new System.Windows.Media.Effects.BlurEffect
                        {
                            Radius = 10,
                            KernelType = System.Windows.Media.Effects.KernelType.Gaussian
                        };
                        this.Effect = blurEffect;
                        
                        // Show dialog
                        bool? result = quantityWindow.ShowDialog();
                        
                        // Remove blur effect
                        this.Effect = null;
                        
                        if (result != true)
                        {
                            return; // User cancelled
                        }
                        
                        // If all items are defective, remove the entire item
                        // Otherwise, decrease the quantity and update the amount
                        removeEntireItem = (defectiveQuantity >= totalQuantity);
                    }
                    else
                    {
                        // If quantity is 1, ask for confirmation before removing
                        MessageBoxResult result = MessageBox.Show(
                            $"Mahsulot \"{item.ProductShortName}\" yaroqsiz sifatida belgilansinmi?", 
                            "Yaroqsiz mahsulot", 
                            MessageBoxButton.YesNo, 
                            MessageBoxImage.Question);
                        
                        if (result == MessageBoxResult.No)
                            return;
                    }

                    // Show loading overlay while processing
                    loadingOverlay.Visibility = Visibility.Visible;

                    try
                    {
                        // Get current token for API request
                        string token = await EnsureValidTokenAsync();
                        if (string.IsNullOrEmpty(token))
                        {
                            loadingOverlay.Visibility = Visibility.Collapsed;
                            MessageBox.Show("Avtorizatsiya xatoligi. Iltimos, qayta kiring.", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }

                        // Use the Order Service to update the order
                        var updatedOrder = await _orderService.MarkProductAsDefectiveAsync(
                            order, 
                            itemId, 
                            defectiveQuantity, 
                            token);

                        // Update local state
                        if (updatedOrder != null)
                        {
                            // Replace the current order with the updated one
                            int index = tableOrders[currentSelectedTable].IndexOf(order);
                            if (index >= 0)
                            {
                                tableOrders[currentSelectedTable][index] = updatedOrder;
                            }

                            // Update UI to reflect changes
                            LoadTableOrders(currentSelectedTable);
                            
                            // Immediately refresh the tables display to ensure it's up-to-date
                            await LoadTablesAsync();
                            
                            // Force a complete refresh of the data from the server
                            await GetData();

                            if (removeEntireItem)
                            {
                                MessageBox.Show("Mahsulot yaroqsiz sifatida chiqarildi!", "Muvaffaqiyatli", MessageBoxButton.OK, MessageBoxImage.Information);
                            }
                            else
                            {
                                MessageBox.Show($"{defectiveQuantity} ta mahsulot yaroqsiz sifatida belgilandi!", "Muvaffaqiyatli", MessageBoxButton.OK, MessageBoxImage.Information);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"API so'rovi yuborishda xatolik: {ex.Message}", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally
                    {
                        // Hide loading overlay
                        loadingOverlay.Visibility = Visibility.Collapsed;
                    }
                }
                catch (Exception ex)
                {
                    // Hide loading in case of error
                    loadingOverlay.Visibility = Visibility.Collapsed;
                    MessageBox.Show($"Xatolik yuz berdi: {ex.Message}", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    public class TableButtonData
    {
        public string TableNumber { get; set; }
        public int ContractorId { get; set; }
        public int? NotCompletedOrderId { get; set; }
        public string OrderCountText { get; set; } = "0/0";
        public string ResponsibleName { get; set; } = "";
        public bool IsBusy { get; set; }
    }

    public class AdditionalPaymentResponse
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int Total { get; set; }
        public List<AdditionalPayment> Rows { get; set; }
    }

    public class AdditionalPayment
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string ShortName { get; set; }
        public string FullName { get; set; }
        public int Percentage { get; set; }
        public int StateId { get; set; }
        public string State { get; set; }
    }

    // FontSize Converter for responsive UI - commented out as we're using fixed font sizes
    /*
    public class WidthToFontSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double width && parameter is string paramValues)
            {
                string[] sizes = paramValues.Split(',');
                
                if (sizes.Length >= 3 && double.TryParse(sizes[0], out double largeSize) &&
                   double.TryParse(sizes[1], out double mediumSize) &&
                   double.TryParse(sizes[2], out double smallSize))
                {
                    if (width > 300)
                        return largeSize;
                    else if (width > 200)
                        return mediumSize;
                    else
                        return smallSize;
                }
                return 14; // Default size
            }
            return 14; // Default size
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    */
}