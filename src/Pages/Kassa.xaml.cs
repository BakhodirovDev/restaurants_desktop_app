using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Restaurants;
using Restaurants.Class;
using Restaurants.Class.Contractor_GetList;
using Restaurants.Class.ContractorOrder_Get;
using Restaurants.Class.Printer;
using Restaurants.Helper;
using Restaurants.Pages;
using Restaurants.Pages.Windows;
using Restaurants.Printer;
using System.Drawing.Printing;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using static Restaurants.Pages.Windows.PaymentTypes;

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
            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += Timer_Tick;

            this.Loaded += Window_Loaded;
            _printer = printer;
            
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
                var data = await GetTablesList();
                if (data?.Rows != null)
                {
                    GenerateTableButtons(data.Rows);
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

            // Update the responsible person name if available
            var firstOrder = orders.FirstOrDefault();
            if (firstOrder != null && !string.IsNullOrEmpty(firstOrder.Responsible))
            {
                lblOfitsiantValue.Text = firstOrder.Responsible;
            }

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

            if (!orderItems.Any())
            {
                Console.WriteLine($"No valid order items for table {tableNumber}");
            }

            foreach (var item in orderItems)
            {
                lvItems.Items.Add(item);
            }

            if (orders.Any())
            {
                var order = orders.FirstOrDefault();
                if (order != null)
                {
                    // Calculate service fee based on settings if API value is not available
                    int serviceFeePercentage = 0;
                    
                    if (order.AdditionalPayments != null && order.AdditionalPayments.Count > 0)
                    {
                        // Use API value if available
                        serviceFeePercentage = order.AdditionalPayments[0].AdditionalPercentage;
                    }
                    else
                    {
                        // Use our own setting if API didn't provide a value
                        serviceFeePercentage = AppSettings.ServiceFeePercentage;
                        
                        // Calculate the additional payment based on our percentage
                        if (serviceFeePercentage > 0)
                        {
                            order.AdditinalPayment = order.Amount * serviceFeePercentage / 100;
                            order.TotalAmount = order.Amount + order.AdditinalPayment;
                        }
                    }
                    
                    // Format currency with spaces instead of commas
                    lblAmountValue.Text = $"{AppSettings.FormatCurrency(order.Amount)} so'm";
                    lblAdditinalPaymentValue.Text = $"{AppSettings.FormatCurrency(order.AdditinalPayment)} so'm";
                    
                    // Calculate total with discount
                    decimal total = order.Amount + order.AdditinalPayment - discountAmount;
                    if (total < 0) total = 0;
                    
                    lblTotalAmountValue.Text = $"{AppSettings.FormatCurrency(total)} so'm";
                    
                    // To'lov turini ContractorOrder dan olish
                    if (!string.IsNullOrEmpty(order.EstimatedPaymentType))
                    {
                        lblPaymentMethod.Text = order.EstimatedPaymentType;
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
                }
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
                // Fetch and update service fee percentage
                await UpdateServiceFeePercentageFromApi();
                
                // Yangi ma'lumotlarni ol, ammo mavjud ma'lumotlarni tozalamasdan
                var data1 = await ContractorOrderGet();
                if (data1 == null) return;

                // Yangilangan ma'lumotlarni qo'sh, ammo mavjudlarini o'chirmasdan
                ProcessApiDataWithoutClearing(data1);
                
                // Tanlangan stol uchun ma'lumotlarni yangilab, UI ni tahrirla
                if (!string.IsNullOrEmpty(currentSelectedTable) && currentSelectedTable != "-1")
                {
                    var selectedButton = tablesPanel.Children.OfType<Button>()
                        .FirstOrDefault(b => b.Tag is TableButtonData td && td.TableNumber == currentSelectedTable);
                    
                    if (selectedButton != null && selectedButton.Tag is TableButtonData btnData && btnData.NotCompletedOrderId.HasValue)
                    {
                        // Faqat yangi ma'lumotlarni pastdan qo'shib yangilash
                        await UpdateExistingTableData(btnData.NotCompletedOrderId.Value);
                    }
                }

                // Stol ma'lumotlarini yangilash
                await LoadTablesAsync();
            }
            catch (Exception ex) {
                Console.WriteLine($"Silent auto refresh error: {ex.Message}");
            }
        }
        
        private async Task UpdateServiceFeePercentageFromApi()
        {
            try
            {
                string token = await EnsureValidTokenAsync();
                if (string.IsNullOrEmpty(token)) return;
                
                using (HttpClient client = new HttpClient())
                {
                    // Set headers
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                    client.DefaultRequestHeaders.Add("accept", "text/plain");
                    
                    // Create request body
                    var requestData = new
                    {
                        search = (string)null,
                        sortBy = "id",
                        orderType = "DESC",
                        page = 0,
                        pageSize = 0
                    };
                    
                    // Convert request to JSON
                    var content = new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json");
                    
                    // Send request
                    HttpResponseMessage response = await client.PostAsync("https://crm-api.webase.uz/crm/AdditionalPayment/GetList", content);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        string jsonResponse = await response.Content.ReadAsStringAsync();
                        var result = JsonConvert.DeserializeObject<AdditionalPaymentResponse>(jsonResponse);
                        
                        if (result != null && result.Rows != null && result.Rows.Count > 0)
                        {
                            // Get first row as requested
                            var firstPayment = result.Rows[0];
                            
                            // Update the local service fee percentage if it's different
                            if (AppSettings.ServiceFeePercentage != firstPayment.Percentage)
                            {
                                AppSettings.ServiceFeePercentage = firstPayment.Percentage;
                                
                                // If a table is selected, refresh its display to show updated service fee
                                if (!string.IsNullOrEmpty(currentSelectedTable) && currentSelectedTable != "-1")
                                {
                                    LoadTableOrders(currentSelectedTable);
                                }
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
            // Calculate service fee based on settings if API value is not available
            int additionalPercentage = 0;
            decimal serviceFee = order.AdditinalPayment;
            decimal grandTotal = order.TotalAmount;
            
            if (order.AdditionalPayments != null && order.AdditionalPayments.Count > 0)
            {
                // Use API value if available
                additionalPercentage = order.AdditionalPayments[0].AdditionalPercentage;
            }
            else
            {
                // Use our own setting if API didn't provide a value
                additionalPercentage = AppSettings.ServiceFeePercentage;
                
                // Recalculate the service fee and total
                if (additionalPercentage > 0)
                {
                    serviceFee = Math.Round(order.Amount * additionalPercentage / 100, 2);
                }
            }
            
            // Include discount in total calculation
            grandTotal = order.Amount + serviceFee - discountAmount;
            if (grandTotal < 0) grandTotal = 0;
        
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
                TotalAmount = Math.Round(order.Amount, 2),
                ServiceFee = serviceFee,
                DiscountAmount = discountAmount,
                DiscountPercentage = discountPercentage,
                IsDiscountPercentage = isDiscountPercentage,
                GrandTotal = grandTotal,
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

            // Agar joriy tanlangan stol bo'lsa, uni yangilash
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
            // (Bu joyda ID bilan taqqoslash kerak, lekin oddiylashtirish uchun shunday qoldiramiz)
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

            // Calculate service fee based on settings if API value is not available
            int serviceFeePercentage = 0;
            
            if (order.AdditionalPayments != null && order.AdditionalPayments.Count > 0)
            {
                // Use API value if available
                serviceFeePercentage = order.AdditionalPayments[0].AdditionalPercentage;
            }
            else
            {
                // Use our own setting if API didn't provide a value
                serviceFeePercentage = AppSettings.ServiceFeePercentage;
                
                // Calculate the additional payment based on our percentage
                if (serviceFeePercentage > 0)
                {
                    order.AdditinalPayment = order.Amount * serviceFeePercentage / 100;
                    order.TotalAmount = order.Amount + order.AdditinalPayment;
                }
            }

            // Jami summani yangilash
            lblAmountValue.Text = $"{AppSettings.FormatCurrency(order.Amount)} so'm";
            lblAdditinalPaymentValue.Text = $"{AppSettings.FormatCurrency(order.AdditinalPayment)} so'm";
            lblTotalAmountValue.Text = $"{AppSettings.FormatCurrency(order.TotalAmount)} so'm";

            // To'lov usulini yangilash
            if (!string.IsNullOrEmpty(order.EstimatedPaymentType))
            {
                lblPaymentMethod.Text = order.EstimatedPaymentType;
                lblPaymentMethod.Foreground = new SolidColorBrush(Colors.Green);
                PaymentMethodBorder.Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromRgb(200, 230, 201), 0),
                        new GradientStop(Color.FromRgb(165, 214, 167), 1)
                    }
                };
            }

            lvItems.Items.Refresh();
            UpdateButtonStates();
        }

        private void btnChangeDiscount_Click(object sender, RoutedEventArgs e)
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
                    if (discountWindow.DiscountApplied)
                    {
                        // Save the discount settings
                        discountAmount = discountWindow.DiscountAmount;
                        isDiscountPercentage = discountWindow.IsPercentage;
                        discountPercentage = discountWindow.DiscountPercentage;
                        
                        // Update the UI with discount
                        lblDiscountValue.Text = $"{AppSettings.FormatCurrency(discountAmount)} so'm";
                    }
                    else
                    {
                        // Reset discount if user clicked reset
                        discountAmount = 0;
                        isDiscountPercentage = true;
                        discountPercentage = 0;
                        lblDiscountValue.Text = "0 so'm";
                    }
                    
                    // Update the total amount with discount applied
                    UpdateTotalWithDiscount();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Chegirma sozlashda xatolik: {ex.Message}", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
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
            
            // Apply discount
            decimal total = subtotal + serviceFee - discountAmount;
            
            // Ensure total is not negative
            if (total < 0)
                total = 0;
                
            // Update the UI
            lblTotalAmountValue.Text = $"{AppSettings.FormatCurrency(total)} so'm";
        }

        private void btnRemoveDefectItem_Click(object sender, RoutedEventArgs e)
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

                    if (removeEntireItem)
                    {
                        // Remove the entire item
                        decimal itemAmount = item.Amount;
                        order.Amount -= itemAmount;
                        
                        // Remove the item from the tables list
                        order.Tables.Remove(item);
                        
                        // Remove the item from the ListView
                        OrderItem itemToRemove = null;
                        foreach (OrderItem orderItem in lvItems.Items)
                        {
                            if (orderItem.Id == itemId)
                            {
                                itemToRemove = orderItem;
                                break;
                            }
                        }
                        
                        if (itemToRemove != null)
                        {
                            lvItems.Items.Remove(itemToRemove);
                        }
                    }
                    else
                    {
                        // Calculate the amount for defective items
                        decimal singleItemPrice = item.EstimatedPrice;
                        decimal defectiveAmount = singleItemPrice * defectiveQuantity;
                        
                        // Update the order amount
                        order.Amount -= defectiveAmount;
                        
                        // Update the item quantity and amount
                        item.Quantity -= defectiveQuantity;
                        item.Amount = item.EstimatedPrice * item.Quantity;
                        
                        // Update the ListView item
                        foreach (OrderItem orderItem in lvItems.Items)
                        {
                            if (orderItem.Id == itemId)
                            {
                                orderItem.Quantity -= defectiveQuantity;
                                orderItem.Amount = orderItem.EstimatedPrice * orderItem.Quantity;
                                break;
                            }
                        }
                    }
                    
                    // Recalculate service fee if applicable
                    if (order.AdditionalPayments != null && order.AdditionalPayments.Count > 0)
                    {
                        int serviceFeePercentage = order.AdditionalPayments[0].AdditionalPercentage;
                        order.AdditinalPayment = order.Amount * serviceFeePercentage / 100;
                    }
                    else
                    {
                        // Use our own setting if API didn't provide a value
                        int serviceFeePercentage = AppSettings.ServiceFeePercentage;
                        
                        // Calculate the additional payment based on our percentage
                        if (serviceFeePercentage > 0)
                        {
                            order.AdditinalPayment = order.Amount * serviceFeePercentage / 100;
                        }
                    }
                    
                    // Update total amount
                    order.TotalAmount = order.Amount + order.AdditinalPayment - discountAmount;
                    
                    // Renumber remaining items
                    int index = 1;
                    foreach (OrderItem orderItem in lvItems.Items)
                    {
                        orderItem.Index = index++;
                    }
                    
                    // Update the UI
                    lvItems.Items.Refresh();
                    lblAmountValue.Text = $"{AppSettings.FormatCurrency(order.Amount)} so'm";
                    lblAdditinalPaymentValue.Text = $"{AppSettings.FormatCurrency(order.AdditinalPayment)} so'm";
                    UpdateTotalWithDiscount();
                    
                    if (removeEntireItem)
                    {
                        MessageBox.Show("Mahsulot yaroqsiz sifatida chiqarildi!", "Muvaffaqiyatli", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else 
                    {
                        MessageBox.Show($"{defectiveQuantity} ta mahsulot yaroqsiz sifatida belgilandi!", "Muvaffaqiyatli", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
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
}