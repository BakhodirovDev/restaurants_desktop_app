﻿using Newtonsoft.Json;
using Restaurants.Class;
using Restaurants.Class.Contractor_GetList;
using Restaurants.Class.ContractorOrder_Get;
using Restaurants.Class.Printer;
using Restaurants.Helper;
using Restaurants.Printer;
using System.Drawing.Printing;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Restaurants.Classes
{
    public partial class Print : Window
    {
        private readonly HttpClient _httpClient;
        private DispatcherTimer timer;
        private DispatcherTimer timeTimer;
        private int countdown = 3;
        private int currentSelectedTable = -1;
        private Dictionary<int, List<ContractorOrder>> tableOrders = new Dictionary<int, List<ContractorOrder>>();

        private readonly XPrinter _printer;

        public Print(HttpClient httpClient, XPrinter printer)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            InitializeComponent();

            // Real-time timer
            timeTimer = new DispatcherTimer();
            timeTimer.Interval = TimeSpan.FromSeconds(1);
            timeTimer.Tick += (s, e) => lblRealTime.Text = DateTime.Now.ToString("HH:mm:ss");
            timeTimer.Start();

            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += Timer_Tick;

            this.Loaded += Window_Loaded;
            _printer = printer;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (tglAutoRefresh != null)
            {
                tglAutoRefresh.IsChecked = false;
                timer.Stop();
            }
            await LoadTablesAsync(); // Load tables dynamically
            UpdateStatusLabels();
            await GetData(); // Load initial order data
        }

        private async Task LoadTablesAsync()
        {
            try
            {
                string token = await EnsureValidTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    MessageBox.Show("Unable to authenticate. Please log in again.", "Authentication Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    GenerateTableButtons(new List<TablesInfo>()); // Fallback with empty list
                    return;
                }

                var requestData = new
                {
                    pageSize = 20,
                    page = 1
                };

                var content = new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Post, "https://crm-api.webase.uz/crm/Contractor/GetList")
                {
                    Content = content
                };

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

                HttpResponseMessage response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    var data = JsonConvert.DeserializeObject<ContractorGetList>(jsonResponse);

                    if (data?.Rows != null)
                    {
                        GenerateTableButtons(data.Rows);
                    }
                    else
                    {
                        MessageBox.Show("No table data returned from the API.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    string errorMessage = $"Failed to load tables: {response.StatusCode} - {response.ReasonPhrase}";
                    if (!string.IsNullOrEmpty(errorContent))
                    {
                        errorMessage += $"\nServer Response: {errorContent}";
                    }
                    MessageBox.Show(errorMessage, "Error", MessageBoxButton.OK, MessageBoxImage.Error);

                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        MessageBox.Show("Session expired or invalid token. Please log in again.", "Authentication Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        new MainWindow().Show();
                        Close();
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

        private async Task<string> EnsureValidTokenAsync()
        {
            string token = Settings.Default.AccessToken;

            if (!string.IsNullOrEmpty(token))
            {
                return token; // Return current token for testing
            }

            string refreshToken = Settings.Default.RefreshToken;
            if (string.IsNullOrEmpty(refreshToken))
            {
                return null;
            }

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

            return null; // Refresh failed
        }

        private void GenerateTableButtons(List<TablesInfo> tables)
        {
            tablesPanel.Children.Clear();

            tables = tables.ToArray().Reverse().ToList(); // Reverse the list for correct order

            foreach (var table in tables)
            {
                if (int.TryParse(table.FirstName, out int tableNumber))
                {
                    // Calculate order counts for this table
                    int productsCount = 0; // Total number of orders (products)
                    int completedProductsCount = 0; // Number of completed orders
                    if (tableOrders.TryGetValue(tableNumber, out List<ContractorOrder> orders))
                    {
                        productsCount = orders.Sum(o => o.TotalProductsCount); // Total orders from ContractorOrder
                        completedProductsCount = orders.Sum(o => o.CompletedProductsCount); // Completed orders from ContractorOrder
                    }

                    string orderCountText = productsCount > 0 ? $"{completedProductsCount}/{productsCount}" : "0/0";

                    Button tableButton = new Button
                    {
                        Content = tableNumber.ToString(),
                        Tag = new TableButtonData
                        {
                            TableNumber = tableNumber,
                            ContractorId = table.Id,
                            NotCompletedOrderId = table.NotCompletedOrderId,
                            OrderCountText = orderCountText // Store order counts in TableButtonData
                        },
                        Style = table.HasNotCompletedOrder ? (Style)FindResource("BusyTableButtonStyle") : (Style)FindResource("TableButtonStyle")
                    };

                    tableButton.Click += TableButton_Click;
                    tablesPanel.Children.Add(tableButton);
                }
            }
        }

        private void TableButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button clickedButton) return;

            if (clickedButton.Tag is not TableButtonData buttonData)
            {
                MessageBox.Show("Invalid table data.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            int tableNumber = buttonData.TableNumber;
            int? notCompletedOrderId = buttonData.NotCompletedOrderId;

            // Preserve the busy status of the table when selecting it
            bool isBusy = false;
            if (tableOrders.TryGetValue(tableNumber, out List<ContractorOrder> orders) && orders != null && orders.Any(o => o.StatusId != 3)) // Assuming StatusId 3 means completed
            {
                isBusy = true;
            }

            if (currentSelectedTable > 0)
            {
                foreach (Button btn in tablesPanel.Children)
                {
                    if (btn.Tag is TableButtonData btnData && btnData.TableNumber == currentSelectedTable)
                    {
                        bool btnIsBusy = tableOrders.TryGetValue(btnData.TableNumber, out List<ContractorOrder> btnOrders) && btnOrders != null && btnOrders.Any(o => o.StatusId != 3);
                        btn.Style = btnIsBusy ? (Style)FindResource("BusyTableButtonStyle") : (Style)FindResource("TableButtonStyle");
                        // Update order counts for the previously selected button
                        int productsCount = tableOrders.TryGetValue(btnData.TableNumber, out List<ContractorOrder> prevOrders) ? (prevOrders != null ? prevOrders.Sum(o => o.TotalProductsCount) : 0) : 0;
                        int completedProductsCount = tableOrders.TryGetValue(btnData.TableNumber, out List<ContractorOrder> prevCompletedOrders) ? (prevCompletedOrders != null ? prevCompletedOrders.Sum(o => o.CompletedProductsCount) : 0) : 0;
                        btnData.OrderCountText = productsCount > 0 ? $"{completedProductsCount}/{productsCount}" : "0/0";
                        btn.Tag = btnData; // Update the Tag to reflect new counts
                        break;
                    }
                }
            }

            // Calculate responsibleName for the newly selected table only
            string responsibleName = tableOrders.TryGetValue(tableNumber, out List<ContractorOrder> responsibleOrders)
                                     ? (responsibleOrders != null && responsibleOrders.Any() ? responsibleOrders.FirstOrDefault()?.Responsible ?? "Null" : "Null")
                                     : "Null";

            // Set the style for the selected button, preserving busy status if applicable
            clickedButton.Style = isBusy ? (Style)FindResource("BusyTableButtonStyle") : (Style)FindResource("SelectedTableButtonStyle");
            currentSelectedTable = tableNumber;
            lblOfitsiantValue.Text = responsibleName; // Assign the responsible name to lblOfitsiantValue.Text
            lblStolValue.Text = "#" + tableNumber;

            // Update order counts for the newly selected button
            int newProductsCount = tableOrders.TryGetValue(tableNumber, out List<ContractorOrder> newOrders) ? (newOrders != null ? newOrders.Sum(o => o.TotalProductsCount) : 0) : 0;
            int newCompletedProductsCount = tableOrders.TryGetValue(tableNumber, out List<ContractorOrder> newCompletedOrders) ? (newCompletedOrders != null ? newCompletedOrders.Sum(o => o.CompletedProductsCount) : 0) : 0;
            buttonData.OrderCountText = newProductsCount > 0 ? $"{newCompletedProductsCount}/{newProductsCount}" : "0/0";
            clickedButton.Tag = buttonData; // Update the Tag to reflect new counts

            // Reload orders immediately after selecting the table, even if busy
            LoadTableOrders(tableNumber);

            // Fetch order details if there’s an incomplete order
            if (notCompletedOrderId.HasValue)
            {
                GetDataForTable(notCompletedOrderId.Value);
            }
        }

        private void LoadTableOrders(int tableNumber, string currency = null)
        {
            lvItems.Items.Clear();

            if (tableOrders.TryGetValue(tableNumber, out List<ContractorOrder> orders) && orders != null)
            {
                // Combine all tables from all ContractorOrder objects for this table number
                var allTables = orders.SelectMany(o => o.Tables ?? new List<ContractorOrderTable>()).Where(t => t != null).ToList();

                var orderItems = allTables.Select((item, index) => new OrderItem
                {
                    Id = item.Id,
                    ProductShortName = item.ProductShortName ?? "No Name",
                    ContractorRequirement = item.ContractorRequirement ?? "No Details",
                    Quantity = (int)item.Quantity, 
                    EstimatedPrice = item.EstimatedPrice,
                    Amount = item.Amount, 
                    TableNumber = tableNumber 
                }).Where(item => !string.IsNullOrEmpty(item.ProductShortName)).ToList();

                if (orderItems.Any())
                {
                    // Add index for display
                    for (int i = 0; i < orderItems.Count; i++)
                    {
                        orderItems[i].Index = i + 1; // Set the index for each item
                    }

                    foreach (var item in orderItems)
                    {
                        lvItems.Items.Add(item); // Add OrderItem to ListView
                    }

                    foreach (var item in orders)
                    {

                        lblAmountValue.Text = item.Amount.ToString();
                        lblAdditinalPaymentValue.Text = item.AdditinalPayment.ToString();
                        lblTotalAmountValue.Text = item.TotalAmount.ToString() ;
                    }

                    
                }
                else
                {
                    Console.WriteLine($"No valid orders found for table {tableNumber}");
                    lblAmountValue.Text = "0.0 UZS"; // Updated to one decimal place
                    lblAdditinalPaymentValue.Text = "0.0 UZS"; // Updated to one decimal place
                    lblTotalAmountValue.Text = "0.0 UZS"; // Updated to one decimal place
                }
            }
            else
            {
                Console.WriteLine($"No orders in tableOrders for table {tableNumber}");
                lblAmountValue.Text = "0.0 UZS"; // Updated to one decimal place
                lblAdditinalPaymentValue.Text = "0.0 UZS"; // Updated to one decimal place
                lblTotalAmountValue.Text = "0.0 UZS"; // Updated to one decimal place
            }
        }

        private string FormatCurrency(decimal value)
        {
            return $"{Math.Round(value, 1):0.0} UZS"; // Format to one decimal place with explicit rounding
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            countdown--;
            lblCountdown.Text = $"({countdown})";

            if (countdown <= 0)
            {
                countdown = 3;
                _ = GetData();
            }
        }

        private void tglAutoRefresh_Checked(object sender, RoutedEventArgs e)
        {
            if (timer != null)
            {
                timer.Start();
            }
            UpdateStatusLabels();
        }

        private void tglAutoRefresh_Unchecked(object sender, RoutedEventArgs e)
        {
            if (timer != null)
            {
                timer.Stop();
            }
            UpdateStatusLabels();
        }

        private void UpdateStatusLabels()
        {
            if (tglAutoRefresh == null || lblStatus == null || lblCountdown == null)
            {
                return;
            }

            if (tglAutoRefresh.IsChecked == true)
            {
                lblStatus.Text = "Har 3 sekundda ma'lumotlar yangilanmoqda...";
                lblStatus.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
            }
            else
            {
                lblStatus.Text = "Avtomatik yangilanish o'chirilgan";
                lblStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 152, 0)); // Orange
            }
        }

        private async Task GetData()
        {
            try
            {
                string token = await EnsureValidTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    MessageBox.Show("Unable to authenticate. Please log in again.", "Authentication Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var request = new HttpRequestMessage(HttpMethod.Get, "https://crm-api.webase.uz/crm/ContractorOrder/Get");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                HttpResponseMessage response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    var data = JsonConvert.DeserializeObject<ContractorOrder>(jsonResponse);

                    // Handle null data gracefully
                    if (data == null)
                    {
                        MessageBox.Show("No order data returned from the API.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                        lblRestoranValue.Text = "Null";
                        lblOfitsiantValue.Text = "Null";
                        lblSanaValue.Text = "Null";
                        lblVaqtValue.Text = "Null";
                        lblChekRaqamiValue.Text = "#Null";
                        ProcessApiData(new ContractorOrder()); // Process empty data
                        return;
                    }

                    lblRestoranValue.Text = data.OrganizationAreasOfActivity ?? "Null";
                    lblOfitsiantValue.Text = data.Responsible ?? "Null";
                    lblSanaValue.Text = data.DocDate ?? "Null";
                    lblVaqtValue.Text = data.DocTime ?? "Null";
                    lblChekRaqamiValue.Text = "#" + (data.DocNumber ?? "Null");

                    ProcessApiData(data);
                    lblLastUpdate.Text = "Oxirgi yangilanish: " + DateTime.Now.ToString("HH:mm:ss");

                    // Reload orders for the currently selected table after data refresh
                    if (currentSelectedTable > 0)
                    {
                        LoadTableOrders(currentSelectedTable);
                    }
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    string errorMessage = $"API error: {response.StatusCode} - {response.ReasonPhrase}";
                    if (!string.IsNullOrEmpty(errorContent))
                    {
                        errorMessage += $"\nServer Response: {errorContent}";
                    }
                    MessageBox.Show(errorMessage, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error occurred while fetching order data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void GetDataForTable(int notCompletedOrderId)
        {
            try
            {
                string token = await EnsureValidTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    MessageBox.Show("Unable to authenticate. Please log in again.", "Authentication Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var request = new HttpRequestMessage(HttpMethod.Get, $"https://crm-api.webase.uz/crm/ContractorOrder/Get/{notCompletedOrderId}?forEdit=false&isClone=false&forCashDocument=false");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    var data = JsonConvert.DeserializeObject<ContractorOrder>(jsonResponse);

                    // Handle null data gracefully
                    if (data == null)
                    {
                        MessageBox.Show($"No order data returned for Order ID {notCompletedOrderId}.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                        ProcessTableData(new ContractorOrder()); // Process empty data
                        return;
                    }

                    ProcessTableData(data);

                    // Reload orders for the currently selected table after fetching specific data
                    if (currentSelectedTable > 0)
                    {
                        LoadTableOrders(currentSelectedTable);
                    }
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    string errorMessage = $"API error for Order ID {notCompletedOrderId}: {response.StatusCode} - {response.ReasonPhrase}";
                    if (!string.IsNullOrEmpty(errorContent))
                    {
                        errorMessage += $"\nServer Response: {errorContent}";
                    }
                    MessageBox.Show(errorMessage, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error occurred while fetching order data for ID {notCompletedOrderId}: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ProcessApiData(ContractorOrder data)
        {
            if (data == null)
            {
                data = new ContractorOrder(); // Default to empty object if null
            }

            var existingTableOrders = new Dictionary<int, List<ContractorOrder>>(tableOrders);

            // Handle null Tables property
            List<ContractorOrder> orders = new List<ContractorOrder> { data }; // Store the ContractorOrder directly

            foreach (var order in orders)
            {
                int tableNumber = order.Tables?.FirstOrDefault(t => t != null)?.OrderNumber ?? 0; // Infer table number from the first valid table
                if (tableNumber <= 0) continue; // Skip invalid table numbers

                if (!tableOrders.ContainsKey(tableNumber))
                {
                    tableOrders[tableNumber] = new List<ContractorOrder>();
                }

                // Add or update the ContractorOrder for this table number
                if (!tableOrders[tableNumber].Any(o => o.Id == order.Id))
                {
                    tableOrders[tableNumber].Add(order);
                }

                foreach (Button btn in tablesPanel.Children)
                {
                    if (btn.Tag is TableButtonData tagData && tagData.TableNumber == tableNumber)
                    {
                        bool hasOrders = order.Tables != null && order.Tables.Any(t => t != null && (!string.IsNullOrEmpty(t.ProductShortName) || t.Quantity > 0 || t.Amount > 0));
                        bool isBusy = hasOrders && order.Tables.Any(t => t != null && !t.IsCompleted);
                        int productsCount = order.TotalProductsCount; // Use TotalProductsCount from ContractorOrder
                        int completedProductsCount = order.CompletedProductsCount; // Use CompletedProductsCount from ContractorOrder

                        string orderCountText = productsCount > 0 ? $"{completedProductsCount}/{productsCount}" : "0/0";
                        tagData.OrderCountText = orderCountText;

                        if (tableNumber != currentSelectedTable)
                        {
                            btn.Style = isBusy ? (Style)FindResource("BusyTableButtonStyle") : (Style)FindResource("TableButtonStyle");
                        }
                        break;
                    }
                }
            }

            foreach (var kvp in existingTableOrders)
            {
                if (!tableOrders.ContainsKey(kvp.Key))
                {
                    tableOrders[kvp.Key] = kvp.Value;
                }
            }

            if (currentSelectedTable > 0)
            {
                LoadTableOrders(currentSelectedTable);
            }
        }

        private void ProcessTableData(ContractorOrder data)
        {
            if (data == null)
            {
                data = new ContractorOrder(); // Default to empty object if null
            }

            int tableNumber = currentSelectedTable; // Use the currently selected table number
            // Handle null Tables property
            List<ContractorOrderTable> tables = data.Tables ?? new List<ContractorOrderTable>();

            // Infer TableNumber from Seria or OrderNumber
            tableNumber = tables.FirstOrDefault(t => t != null && (!string.IsNullOrEmpty(t.ProductShortName) || t.Quantity > 0 || t.Amount > 0))?.OrderNumber ?? tableNumber;

            if (tableNumber <= 0)
            {
                // Try to infer from Seria if OrderNumber fails
                tableNumber = tables.FirstOrDefault(t => t != null && !string.IsNullOrEmpty(t.Seria) && int.TryParse(t.Seria, out int seria))?.OrderNumber ?? currentSelectedTable;
            }

            // Preserve existing tableOrders for this table
            var existingOrders = tableOrders.ContainsKey(tableNumber) ? new List<ContractorOrder>(tableOrders[tableNumber]) : new List<ContractorOrder>();

            // Store the list of ContractorOrder for the selected table
            if (!tableOrders.ContainsKey(tableNumber))
            {
                tableOrders[tableNumber] = new List<ContractorOrder>();
            }

            // Add or update the ContractorOrder for this table number
            if (!tableOrders[tableNumber].Any(o => o.Id == data.Id))
            {
                tableOrders[tableNumber].Add(data);
            }

            // Update button style and order counts for the selected table
            foreach (Button btn in tablesPanel.Children)
            {
                if (btn.Tag is TableButtonData tagData && tagData.TableNumber == tableNumber)
                {
                    int productsCount = data.TotalProductsCount; // Use TotalProductsCount from ContractorOrder
                    int completedProductsCount = data.CompletedProductsCount; // Use CompletedProductsCount from ContractorOrder

                    string orderCountText = productsCount > 0 ? $"{completedProductsCount}/{productsCount}" : "0/0";
                    tagData.OrderCountText = orderCountText; // Update order counts in TableButtonData

                    bool isBusy = productsCount > 0 && (data.StatusId != 3); // Assuming StatusId 3 means completed
                    btn.Style = isBusy ? (Style)FindResource("BusyTableButtonStyle") : (Style)FindResource("SelectedTableButtonStyle");
                    break;
                }
            }

            // Reload orders for the currently selected table after processing
            LoadTableOrders(tableNumber, data.Currency);
        }

        private void btnGetData_Click(object sender, RoutedEventArgs e)
        {
            countdown = 3;
            lblCountdown.Text = $"({countdown})";
            _ = GetData();
        }

        private decimal GetDecimalValueFromText(string text)
        {
            // Extract numeric value from formatted currency text
            if (string.IsNullOrEmpty(text)) return 0;
            string numericText = text.Replace("UZS", "").Replace(" ", "").Replace(",", "").Trim();
            if (decimal.TryParse(numericText, out decimal result))
            {
                return Math.Round(result/10, 1); // Round to 1 decimal place
            }
            return 0;
        }

        private void btnPrint_Click(object sender, RoutedEventArgs e)
        {
            if (currentSelectedTable <= 0)
            {
                MessageBox.Show("Chop etish uchun stol tanlang", "Xabar", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!tableOrders.ContainsKey(currentSelectedTable) || !tableOrders[currentSelectedTable].Any(o => o != null))
            {
                MessageBox.Show("Tanlangan stolda buyurtmalar mavjud emas", "Xabar", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var printOrder = new PrintOrder
            {
                TableNumber = currentSelectedTable,
                RestaurantName = tableOrders[currentSelectedTable].FirstOrDefault()?.OrganizationAreasOfActivity ?? "Null",
                WaiterName = tableOrders[currentSelectedTable].FirstOrDefault()?.Responsible ?? "Null",
                OrderDate = tableOrders[currentSelectedTable].FirstOrDefault()?.DocDate ?? "Null",
                OrderTime = tableOrders[currentSelectedTable].FirstOrDefault()?.DocTime ?? "Null",
                CheckNumber = tableOrders[currentSelectedTable].FirstOrDefault()?.DocNumber ?? "Null",
                Orders = tableOrders[currentSelectedTable]
                    .SelectMany(o => o.Tables ?? new List<ContractorOrderTable>())
                    .Where(item => item != null)
                    .Select(item => new OrderItem
                    {
                        Id = item.Id,
                        ProductShortName = item.ProductShortName ?? "No Name",
                        ContractorRequirement = item.ContractorRequirement ?? "No Details",
                        Quantity = (int)(item.Quantity > 0 ? item.Quantity : 1), // Default to 1 if zero or negative
                        EstimatedPrice = Math.Round(item.EstimatedPrice > 0 ? item.EstimatedPrice : 0, 1), // Round to 1 decimal place
                        Amount = Math.Round(item.Amount > 0 ? item.Amount : (item.EstimatedPrice * (item.Quantity > 0 ? item.Quantity : 1)), 1), // Round to 1 decimal place
                        TableNumber = currentSelectedTable // Infer TableNumber
                    }).Where(item => !string.IsNullOrEmpty(item.ProductShortName)).ToList(), // Only filter out items with no name
                TotalAmount = tableOrders[currentSelectedTable].FirstOrDefault()?.Amount ?? 0, // Round to 1 decimal place
                ServiceFee = tableOrders[currentSelectedTable].FirstOrDefault()?.AdditinalPayment ?? 0, // Round to 1 decimal place
                GrandTotal = tableOrders[currentSelectedTable].FirstOrDefault()?.TotalAmount ?? 0 // Round to 1 decimal place (Total + 10% service fee)
            };

            string txtForPrint = BuildPrintText(printOrder);
            _printer.PrintText(printOrder);
        }

        private string BuildPrintText(PrintOrder order)
        {
            var sb = new StringBuilder();

            // Initialize printer (ESC @)
            sb.Append("\x1B\x40");

            // Center alignment for header (ESC a 1)
            sb.Append("\x1B\x61\x01");

            // Bold text for header (ESC E 1)
            sb.Append("\x1B\x45\x01");
            sb.AppendLine($"Zakaz N#: {order.CheckNumber}");
            sb.AppendLine($"Restoran: {order.RestaurantName}");
            sb.AppendLine($"Ofitsiant: {order.WaiterName}");
            sb.AppendLine($"Sana: {order.OrderDate}   Vaqt: {order.OrderTime}");
            sb.AppendLine($"Stol: {order.TableNumber}");

            // Reset bold text (ESC E 0)
            sb.Append("\x1B\x45\x00");

            // Print separator line (48 characters to fit 80mm width)
            sb.AppendLine(new string('-', 48));

            // Left alignment (ESC a 0)
            sb.Append("\x1B\x61\x0");

            // Header for table with wider Mahsulot column
            sb.AppendLine($"Mahsulot                    |    Soni    |    Summa");
            sb.AppendLine(new string('-', 48));

            // Print items with wider Mahsulot column (24 characters)
            foreach (var item in order.Orders)
            {
                // Format Amount to one decimal place
                string amountFormatted = Math.Round(item.Amount, 1).ToString("0.0").PadLeft(8);
                // Truncate or pad ProductShortName to 24 characters to fit within 48-character line
                string productName = item.ProductShortName.Length > 24 ? item.ProductShortName.Substring(0, 24) : item.ProductShortName.PadRight(24);
                sb.AppendLine($"{productName} | {item.Quantity.ToString().PadLeft(8)} | {amountFormatted} UZS");
            }

            // Print separator line
            sb.AppendLine(new string('-', 48));

            // Right alignment for totals (ESC a 2)
            sb.Append("\x1B\x61\x02");
            // Format totals to one decimal place
            string totalFormatted = Math.Round(order.TotalAmount, 1).ToString("0.0").PadLeft(8);
            string serviceFeeFormatted = Math.Round(order.ServiceFee, 1).ToString("0.0").PadLeft(8);
            string grandTotalFormatted = Math.Round(order.GrandTotal, 1).ToString("0.0").PadLeft(8);
            sb.AppendLine($"Summa: {totalFormatted} UZS");
            sb.AppendLine($"Xizmat haqi(12%): {serviceFeeFormatted} UZS");
            sb.AppendLine(new string('-', 48));
            sb.AppendLine($"Jami: {grandTotalFormatted} UZS");

            // Center alignment for footer (ESC a 1)
            sb.Append("\x1B\x61\x01");
            sb.AppendLine("Tashrifingiz uchun rahmat!");

            // Cut paper (GS V 0)
            sb.Append("\x1D\x56\x00");

            return sb.ToString();
        }

        private void btnLogout_Click(object sender, RoutedEventArgs e)
        {
            Settings.Default.AccessToken = null;
            Settings.Default.RefreshToken = null;
            Settings.Default.accessTokenExpireAt = null; // If added
            Settings.Default.Save();

            new MainWindow().Show();
            Close();
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
    }

    // Updated TableButtonData to include OrderCountText
    public class TableButtonData
    {
        public int TableNumber { get; set; }
        public int ContractorId { get; set; }
        public int? NotCompletedOrderId { get; set; }
        public string OrderCountText { get; set; } = "0/0"; // Default to 0/0 for order counts
    }
}