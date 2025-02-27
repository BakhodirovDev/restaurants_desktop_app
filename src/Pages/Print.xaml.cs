using Newtonsoft.Json;
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
        private Dictionary<int, List<ContractorOrderTable>> tableOrders = new Dictionary<int, List<ContractorOrderTable>>();

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
                    if (tableOrders.TryGetValue(tableNumber, out List<ContractorOrderTable> orders))
                    {
                        productsCount = orders.Count; // Total orders (products)
                        completedProductsCount = orders.Count(o => o.IsCompleted); // Completed orders based on IsCompleted
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
            if (tableOrders.TryGetValue(tableNumber, out List<ContractorOrderTable> orders) && orders.Any(o => !o.IsCompleted))
            {
                isBusy = true;
            }

            if (currentSelectedTable > 0)
            {
                foreach (Button btn in tablesPanel.Children)
                {
                    if (btn.Tag is TableButtonData btnData && btnData.TableNumber == currentSelectedTable)
                    {
                        bool btnIsBusy = tableOrders.TryGetValue(btnData.TableNumber, out List<ContractorOrderTable> btnOrders) && btnOrders.Any(o => !o.IsCompleted);
                        btn.Style = btnIsBusy ? (Style)FindResource("BusyTableButtonStyle") : (Style)FindResource("TableButtonStyle");
                        break;
                    }
                }
            }

            // Set the style for the selected button, preserving busy status if applicable
            clickedButton.Style = isBusy ? (Style)FindResource("BusyTableButtonStyle") : (Style)FindResource("SelectedTableButtonStyle");
            currentSelectedTable = tableNumber;
            lblStolValue.Text = "#" + tableNumber;

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

            if (tableOrders.TryGetValue(tableNumber, out List<ContractorOrderTable> items) && items != null)
            {
                // Map ContractorOrderTable to OrderItem for display, including busy tables
                var orderItems = items.Select((item, index) => new OrderItem
                {
                    Id = item.Id,
                    ProductShortName = item.ProductShortName ?? "No Name",
                    ContractorRequirement = item.ContractorRequirement ?? "No Details",
                    Quantity = (int)(item.Quantity > 0 ? item.Quantity : 1), // Default to 1 if zero or negative
                    EstimatedPrice = item.EstimatedPrice > 0 ? item.EstimatedPrice : 0,
                    Amount = item.Amount > 0 ? item.Amount : (item.EstimatedPrice * (item.Quantity > 0 ? item.Quantity : 1)), // Calculate if Amount is 0
                    TableNumber = tableNumber // Infer TableNumber from context
                }).Where(item => !string.IsNullOrEmpty(item.ProductShortName)).ToList(); // Only filter out items with no name

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
                    CalculateTotals(orderItems);
                }
                else
                {
                    Console.WriteLine($"No valid orders found for table {tableNumber}");
                    lblAmountValue.Text = "0 so'm";
                    lblAdditinalPaymentValue.Text = "0 so'm";
                    lblTotalAmountValue.Text = "0 so'm";
                }
            }
            else
            {
                Console.WriteLine($"No orders in tableOrders for table {tableNumber}");
                lblAmountValue.Text = "0 so'm";
                lblAdditinalPaymentValue.Text = "0 so'm";
                lblTotalAmountValue.Text = "0 so'm";
            }
        }

        private void CalculateTotals(List<OrderItem> items)
        {
            decimal total = items.Sum(item => item.Amount);
            decimal serviceFee = total * 0.1m; // 10% service fee
            lblAmountValue.Text = FormatCurrency(total);
            lblAdditinalPaymentValue.Text = FormatCurrency(serviceFee);
            lblTotalAmountValue.Text = FormatCurrency(total + serviceFee);
        }

        private string FormatCurrency(decimal value)
        {
            return $"{value:0.00} so'm";
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
                    MessageBox.Show($"API error: {response.StatusCode} - {errorContent}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    var data = JsonConvert.DeserializeObject<ContractorOrder>(jsonResponse); // Use ContractorOrder
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
                    MessageBox.Show($"API error: {response.StatusCode} - {errorContent}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ProcessApiData(ContractorOrder data)
        {
            if (data == null || data.Tables == null) return;

            // Preserve existing tableOrders and update with new data
            var existingTableOrders = new Dictionary<int, List<ContractorOrderTable>>(tableOrders);

            // Update tableOrders with new data, preserving existing entries if possible
            foreach (var table in data.Tables)
            {
                // Infer TableNumber from Seria or OrderNumber if available
                int tableNumber = int.TryParse(table.Seria, out int seriaNumber) ? seriaNumber : table.OrderNumber;

                if (!tableOrders.ContainsKey(tableNumber))
                {
                    tableOrders[tableNumber] = new List<ContractorOrderTable>();
                }

                // Add or update the table's orders, accepting more lenient filtering
                var validTables = data.Tables.Where(t =>
                    int.TryParse(t.Seria, out int tn) ? tn == tableNumber : t.OrderNumber == tableNumber)
                    .Where(t => !string.IsNullOrEmpty(t.ProductShortName) || t.Quantity > 0 || t.Amount > 0) // More lenient filter
                    .ToList();

                tableOrders[tableNumber] = validTables.Any() ? validTables : new List<ContractorOrderTable>();

                // Update button style and order counts
                foreach (Button btn in tablesPanel.Children)
                {
                    if (btn.Tag is TableButtonData tagData && tagData.TableNumber == tableNumber)
                    {
                        bool hasOrders = validTables.Any(t => !string.IsNullOrEmpty(t.ProductShortName) || t.Quantity > 0 || t.Amount > 0);
                        bool isBusy = hasOrders && validTables.Any(t => !t.IsCompleted); // Check if any order is not completed
                        int productsCount = validTables.Count; // Total number of orders (products)
                        int completedProductsCount = validTables.Count(t => t.IsCompleted); // Completed orders based on IsCompleted

                        string orderCountText = productsCount > 0 ? $"{completedProductsCount}/{productsCount}" : "0/0";
                        tagData.OrderCountText = orderCountText; // Update order counts in TableButtonData

                        if (tableNumber != currentSelectedTable)
                        {
                            btn.Style = isBusy ? (Style)FindResource("BusyTableButtonStyle") : (Style)FindResource("TableButtonStyle");
                        }
                        break;
                    }
                }
            }

            // Restore any existing orders not updated by the new data
            foreach (var kvp in existingTableOrders)
            {
                if (!tableOrders.ContainsKey(kvp.Key))
                {
                    tableOrders[kvp.Key] = kvp.Value;
                }
            }

            // Reload orders for the currently selected table after processing
            if (currentSelectedTable > 0)
            {
                LoadTableOrders(currentSelectedTable);
            }
        }

        private void ProcessTableData(ContractorOrder data)
        {
            if (data == null || data.Tables == null) return;

            int tableNumber = currentSelectedTable; // Use the currently selected table number
            // Infer TableNumber from Seria or OrderNumber
            tableNumber = data.Tables.FirstOrDefault(t => !string.IsNullOrEmpty(t.ProductShortName) || t.Quantity > 0 || t.Amount > 0)?.OrderNumber ?? tableNumber;

            if (tableNumber <= 0)
            {
                // Try to infer from Seria if OrderNumber fails
                tableNumber = data.Tables.FirstOrDefault(t => !string.IsNullOrEmpty(t.Seria) && int.TryParse(t.Seria, out int seria))?.OrderNumber ?? currentSelectedTable;
            }

            // Preserve existing tableOrders for this table
            var existingOrders = tableOrders.ContainsKey(tableNumber) ? new List<ContractorOrderTable>(tableOrders[tableNumber]) : new List<ContractorOrderTable>();

            // Store the list of ContractorOrderTable for the selected table, with lenient filtering
            if (!tableOrders.ContainsKey(tableNumber))
            {
                tableOrders[tableNumber] = new List<ContractorOrderTable>();
            }

            var validTables = data.Tables.Where(t => !string.IsNullOrEmpty(t.ProductShortName) || t.Quantity > 0 || t.Amount > 0).ToList();
            tableOrders[tableNumber] = validTables.Any() ? validTables : existingOrders; // Use existing orders if no new valid data

            // Update button style and order counts for the selected table
            foreach (Button btn in tablesPanel.Children)
            {
                if (btn.Tag is TableButtonData tagData && tagData.TableNumber == tableNumber)
                {
                    int productsCount = tableOrders[tableNumber].Count; // Total number of orders (products)
                    int completedProductsCount = tableOrders[tableNumber].Count(t => t.IsCompleted); // Completed orders based on IsCompleted

                    string orderCountText = productsCount > 0 ? $"{completedProductsCount}/{productsCount}" : "0/0";
                    tagData.OrderCountText = orderCountText; // Update order counts in TableButtonData

                    bool isBusy = productsCount > 0 && tableOrders[tableNumber].Any(t => !t.IsCompleted);
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
            string numericText = text.Replace("so'm", "").Replace(" ", "").Replace(",", "").Trim();
            return decimal.TryParse(numericText, out decimal result) ? result : 0;
        }

        private void btnPrint_Click(object sender, RoutedEventArgs e)
        {
            if (currentSelectedTable <= 0)
            {
                MessageBox.Show("Chop etish uchun stol tanlang", "Xabar", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!tableOrders.ContainsKey(currentSelectedTable) || !tableOrders[currentSelectedTable].Any())
            {
                MessageBox.Show("Tanlangan stolda buyurtmalar mavjud emas", "Xabar", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var printOrder = new PrintOrder
            {
                TableNumber = currentSelectedTable,
                RestaurantName = lblRestoranValue.Text,
                WaiterName = lblOfitsiantValue.Text,
                OrderDate = lblSanaValue.Text,
                OrderTime = lblVaqtValue.Text,
                CheckNumber = lblChekRaqamiValue.Text.TrimStart('#'),
                Orders = tableOrders[currentSelectedTable].Select(item => new OrderItem
                {
                    Id = item.Id,
                    ProductShortName = item.ProductShortName ?? "No Name",
                    ContractorRequirement = item.ContractorRequirement ?? "No Details",
                    Quantity = (int)(item.Quantity > 0 ? item.Quantity : 1), // Default to 1 if zero or negative
                    EstimatedPrice = item.EstimatedPrice > 0 ? item.EstimatedPrice : 0,
                    Amount = item.Amount > 0 ? item.Amount : (item.EstimatedPrice * (item.Quantity > 0 ? item.Quantity : 1)), // Calculate if Amount is 0
                    TableNumber = currentSelectedTable // Infer TableNumber
                }).Where(item => !string.IsNullOrEmpty(item.ProductShortName)).ToList(), // Only filter out items with no name
                TotalAmount = GetDecimalValueFromText(lblAmountValue.Text),
                ServiceFee = GetDecimalValueFromText(lblAdditinalPaymentValue.Text),
                GrandTotal = GetDecimalValueFromText(lblTotalAmountValue.Text)
            };

            string txtForPrint = $"Zakaz N#: {printOrder.CheckNumber}\n";
            foreach (var item in printOrder.Orders)
            {
                txtForPrint += $"{item.ProductShortName.PadRight(20)} {item.Quantity.ToString().PadLeft(5)} {FormatCurrency(item.Amount)}\n";
            }

            txtForPrint += $"Summa: {FormatCurrency(printOrder.TotalAmount)}\n" +
                           $"Xizmat haqi: {FormatCurrency(printOrder.ServiceFee)}\n" +
                           $"Jami: {FormatCurrency(printOrder.GrandTotal)}";

            _printer.PrintText(txtForPrint);
        }

        private static void PrintToXP80C(PrintOrder order)
        {
            try
            {
                string printerName = "XP-80C";

                // Check if printer exists
                bool printerExists = false;
                foreach (string installedPrinter in PrinterSettings.InstalledPrinters)
                {
                    if (installedPrinter.Equals(printerName, StringComparison.OrdinalIgnoreCase))
                    {
                        printerExists = true;
                        break;
                    }
                }

                if (!printerExists)
                {
                    throw new Exception($"\"{printerName}\" nomli printer topilmadi. Iltimos, printer ulanganligini tekshiring.");
                }

                // Create receipt in ESC/POS format
                StringBuilder receipt = new StringBuilder();
                receipt.Append("\x1B\x40"); // ESC @ - Reset printer
                receipt.Append("\x1B\x61\x01"); // Center alignment
                receipt.Append("\x1D\x21\x11"); // Large font
                receipt.AppendLine(order.RestaurantName);
                receipt.Append("\x1D\x21\x00"); // Normal font
                receipt.AppendLine($"{order.OrderDate} {order.OrderTime}");
                receipt.AppendLine($"Chek №{order.CheckNumber}");
                receipt.AppendLine($"Stol: {order.TableNumber}");
                receipt.AppendLine($"Ofitsiant: {order.WaiterName}");
                receipt.AppendLine(new string('-', 32));

                int itemNumber = 1;
                foreach (var item in order.Orders)
                {
                    string name = item.ProductShortName.Length > 15 ? item.ProductShortName.Substring(0, 15) : item.ProductShortName.PadRight(15);
                    string qty = item.Quantity.ToString().PadLeft(4);
                    string price = item.EstimatedPrice.ToString("0.00").PadLeft(7);
                    string amount = item.Amount.ToString("0.00").PadLeft(7);
                    receipt.AppendLine($"{itemNumber++.ToString().PadLeft(2)} {name} {qty} {price} {amount}");
                }

                receipt.AppendLine(new string('-', 32));
                receipt.Append("\x1B\x61\x02"); // Right alignment
                receipt.AppendLine($"Jami:         {order.TotalAmount:0.00}");
                receipt.AppendLine($"Xizmat haqi:  {order.ServiceFee:0.00}");
                receipt.Append("\x1D\x21\x01"); // Large font
                receipt.AppendLine($"UMUMIY:       {order.GrandTotal:0.00}");
                receipt.Append("\x1D\x21\x00"); // Normal font

                receipt.Append("\x1B\x61\x01"); // Center alignment
                receipt.AppendLine("Tashrifingiz uchun rahmat!");
                receipt.Append("\x1D\x56\x00"); // Cut paper

                // Send ESC/POS command to printer
                bool result = PrinterHelper.SendStringToPrinter(printerName, receipt.ToString());

                if (result)
                {
                    MessageBox.Show($"Chek №{order.CheckNumber} muvaffaqiyatli chop etildi", "Xabar", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    throw new Exception("Printerga ma'lumot yuborishda xatolik yuz berdi.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Chop etishda xatolik: {ex.Message}", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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