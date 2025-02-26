using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Newtonsoft.Json;
using RawPrint;
using RawPrint.NetStd;
using System.Drawing.Printing;
using Restaurants.Helper;

namespace Restaurants.Class
{
    public partial class Print : Window
    {
        private readonly HttpClient _httpClient;
        private DispatcherTimer timer;
        private DispatcherTimer timeTimer;
        private int countdown = 3;
        private int currentSelectedTable = -1;
        private Dictionary<int, List<OrderItem>> tableOrders = new Dictionary<int, List<OrderItem>>();

        public Print(HttpClient httpClient)
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
                // Ensure we have a valid token
                string token = await EnsureValidTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    MessageBox.Show("Unable to authenticate. Please log in again.", "Authentication Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    GenerateTableButtons(20); // Fallback
                    return;
                }

                /*// Log the token for debugging
                MessageBox.Show($"Using Token: {token}", "Debug", MessageBoxButton.OK); // Remove in production
*/
                // Minimal body as confirmed
                var requestData = new
                {
                    pageSize = 20,
                    page = 1
                };

                var content = new StringContent(JsonConvert.SerializeObject(requestData), System.Text.Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Post, "https://crm-api.webase.uz/crm/Contractor/GetList")
                {
                    Content = content
                };

                // Set headers to match Postman exactly
                request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/plain"));
                request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("*/*"));
                request.Headers.Add("accept-language", "ru-RU,ru;q=0.9,uz-UZ;q=0.8,uz;q=0.7,en-US;q=0.6,en;q=0.5");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token); // Ensure Bearer prefix
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

                // Send the request
                HttpResponseMessage response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    var data = JsonConvert.DeserializeObject<ContractorResponse>(jsonResponse);

                    if (data?.Rows != null)
                    {
                        GenerateTableButtons(data.Rows);
                    }
                    else
                    {
                        MessageBox.Show("No table data returned from the API.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                        GenerateTableButtons(20); // Fallback
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
                    GenerateTableButtons(20); // Fallback

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
                GenerateTableButtons(20); // Fallback
            }
            catch (JsonException ex)
            {
                MessageBox.Show($"Error parsing table data: {ex.Message}", "Parse Error", MessageBoxButton.OK, MessageBoxImage.Error);
                GenerateTableButtons(20); // Fallback
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unexpected error loading tables: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                GenerateTableButtons(20); // Fallback
            }
        }

        private async Task<string> EnsureValidTokenAsync()
        
        {
            string token = Settings.Default.AccessToken;

            if (!string.IsNullOrEmpty(token))
            {
                return token; // Return current token for testing
            }

            // Attempt to refresh token
            string refreshToken = Settings.Default.RefreshToken;
            if (string.IsNullOrEmpty(refreshToken))
            {
                return null;
            }

            var refreshRequest = new { refreshToken };
            var refreshContent = new StringContent(JsonConvert.SerializeObject(refreshRequest), System.Text.Encoding.UTF8, "application/json");
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

        private void GenerateTableButtons(List<ContractorRow> tables)
        {
            tablesPanel.Children.Clear();

            tables = tables.ToArray().Reverse().ToList(); // Reverse the list for correct order

            foreach (var table in tables)
            {
                if (int.TryParse(table.FirstName, out int tableNumber))
                {
                    Button tableButton = new Button
                    {
                        Content = tableNumber.ToString(),
                        Tag = tableNumber,
                        Style = table.HasNotCompletedOrder ? (Style)FindResource("BusyTableButtonStyle") : (Style)FindResource("TableButtonStyle")
                    };
                    tableButton.Content = tableNumber.ToString();
                    tableButton.Tag = new TableButtonData
                    {
                        TableNumber = tableNumber,
                        ContractorId = table.Id,
                        NotCompletedOrderId = table.notCompletedOrderId
                    };
                    tableButton.Click += TableButton_Click;
                    tablesPanel.Children.Add(tableButton);
                }
            }
        }
        private void GenerateTableButtons(int count)
        {
            tablesPanel.Children.Clear();

            for (int i = 1; i <= count; i++)
            {
                var buttonData = new TableButtonData
                {
                    TableNumber = i,
                    ContractorId = 0, // Dummy value for fallback
                    NotCompletedOrderId = null // No order in fallback
                };

                Button tableButton = new Button
                {
                    Content = i.ToString(),
                    Tag = buttonData, // Use TableButtonData even in fallback
                    Style = (Style)FindResource("TableButtonStyle")
                };
                tableButton.Click += TableButton_Click;
                tablesPanel.Children.Add(tableButton);
            }
        }

        private void TableButton_Click(object sender, RoutedEventArgs e)
        {
            Button clickedButton = sender as Button;
            if (clickedButton == null) return;

            int tableNumber;
            int? notCompletedOrderId = null;

            // Handle both possible Tag types
            if (clickedButton.Tag is TableButtonData buttonData)
            {
                tableNumber = buttonData.TableNumber;
                notCompletedOrderId = buttonData.NotCompletedOrderId;
            }
            else if (clickedButton.Tag is int intTag)
            {
                tableNumber = intTag;
                notCompletedOrderId = null; // No order ID in fallback case
            }
            else
            {
                MessageBox.Show("Invalid table data.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (currentSelectedTable > 0)
            {
                foreach (Button btn in tablesPanel.Children)
                {
                    int btnTableNumber;
                    bool hasNotCompletedOrder = false;

                    if (btn.Tag is TableButtonData btnData)
                    {
                        btnTableNumber = btnData.TableNumber;
                        hasNotCompletedOrder = btnData.NotCompletedOrderId.HasValue;
                    }
                    else if (btn.Tag is int btnIntTag)
                    {
                        btnTableNumber = btnIntTag;
                    }
                    else
                    {
                        continue;
                    }

                    if (btnTableNumber == currentSelectedTable)
                    {
                        btn.Style = hasNotCompletedOrder ? (Style)FindResource("BusyTableButtonStyle") : (Style)FindResource("TableButtonStyle");
                        break;
                    }
                }
            }

            clickedButton.Style = (Style)FindResource("SelectedTableButtonStyle");
            currentSelectedTable = tableNumber;
            lblStolValue.Text = "#" + tableNumber;

            // Fetch order details if there’s an incomplete order
            if (notCompletedOrderId.HasValue)
            {
                GetDataForTable(notCompletedOrderId.Value);
            }
            else
            {
                LoadTableOrders(tableNumber); // Fallback to local data or clear
            }
        }


        private void LoadTableOrders(int tableNumber)
        {
            lvItems.Items.Clear();

            if (tableOrders.ContainsKey(tableNumber) && tableOrders[tableNumber].Count > 0)
            {
                foreach (var item in tableOrders[tableNumber])
                {
                    lvItems.Items.Add(item);
                }
                CalculateTotals(tableOrders[tableNumber]);
            }
            else
            {
                lblJamiValue.Text = "0 so'm";
                lblXizmatValue.Text = "0 so'm";
                lblTotalValue.Text = "0 so'm";
            }
        }

        private void CalculateTotals(List<OrderItem> items)
        {
            decimal total = 0;
            foreach (var item in items)
            {
                total += item.Summa;
            }

            decimal serviceFee = total * 0.1m;
            lblJamiValue.Text = FormatCurrency(total);
            lblXizmatValue.Text = FormatCurrency(serviceFee);
            lblTotalValue.Text = FormatCurrency(total + serviceFee);
        }

        private string FormatCurrency(decimal amount)
        {
            return $"{amount:N0} so'm";
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
                    var data = JsonConvert.DeserializeObject<ApiResponse>(jsonResponse);

                    lblRestoranValue.Text = data.OrganizationAreasOfActivity ?? "Null";
                    lblOfitsiantValue.Text = data.Responsible ?? "Null";
                    lblSanaValue.Text = data.DocDate ?? "Null";
                    lblVaqtValue.Text = data.DocTime ?? "Null";
                    lblChekRaqamiValue.Text = "#" + (data.DocNumber ?? "Null");

                    ProcessApiData(data);
                    lblLastUpdate.Text = "Oxirgi yangilanish: " + DateTime.Now.ToString("HH:mm:ss");
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
                    var data = JsonConvert.DeserializeObject<TableOrderResponse>(jsonResponse);
                    ProcessTableData(data);
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

        private void ProcessApiData(ApiResponse data)
        {
            if (data == null || data.Tables == null) return;

            foreach (var table in data.Tables)
            {
                tableOrders[table.TableNumber] = table.Orders;
                foreach (Button btn in tablesPanel.Children)
                {
                    if (Convert.ToInt32(btn.Tag) == table.TableNumber)
                    {
                        if (table.Orders.Count > 0 && table.TableNumber != currentSelectedTable)
                        {
                            btn.Style = (Style)FindResource("BusyTableButtonStyle"); // Red for busy
                        }
                        else if (table.Orders.Count == 0 && table.TableNumber != currentSelectedTable)
                        {
                            btn.Style = (Style)FindResource("TableButtonStyle"); // Blue for free
                        }
                        break;
                    }
                }
            }

            if (currentSelectedTable > 0)
            {
                LoadTableOrders(currentSelectedTable);
            }
        }

        private void ProcessTableData(TableOrderResponse data)
        {
            if (data == null || data.Tables == null) return;

            int tableNumber = currentSelectedTable; // Use the currently selected table number

            // Map response data to OrderItem for consistency with existing UI
            var orderItems = data.Tables.Select(t => new OrderItem
            {
                Id = t.Id,
                Nomi = t.ProductShortName,
                Narxi = t.EstimatedPrice,
                Soni = t.Quantity,
                Summa = t.Amount
            }).ToList();

            tableOrders[tableNumber] = orderItems;

            LoadTableOrders(tableNumber);
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
            string numericText = text.Replace("so'm", "").Replace(" ", "").Replace(",", "");
            decimal.TryParse(numericText, out decimal result);
            return result;
        }

        private void btnPrint_Click(object sender, RoutedEventArgs e)
        {
            if (currentSelectedTable <= 0)
            {
                MessageBox.Show("Chop etish uchun stol tanlang", "Xabar", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!tableOrders.ContainsKey(currentSelectedTable) || tableOrders[currentSelectedTable].Count == 0)
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
                Orders = tableOrders[currentSelectedTable],
                TotalAmount = GetDecimalValueFromText(lblJamiValue.Text),
                ServiceFee = GetDecimalValueFromText(lblXizmatValue.Text),
                GrandTotal = GetDecimalValueFromText(lblTotalValue.Text)
            };

            PrintToXP80C(printOrder);
        }

        static void PrintToXP80C(PrintOrder order)
        {
            try
            {
                string printerName = "XP-80C";

                // Printer borligini tekshirish
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

                // ESC/POS formatida chek yaratish
                StringBuilder receipt = new StringBuilder();
                receipt.Append("\x1B\x40"); // ESC @ - Printerni reset qilish
                receipt.Append("\x1B\x61\x01"); // Markazga joylash
                receipt.Append("\x1D\x21\x11"); // Katta font
                receipt.AppendLine(order.RestaurantName);
                receipt.Append("\x1D\x21\x00"); // Oddiy font
                receipt.AppendLine($"{order.OrderDate} {order.OrderTime}");
                receipt.AppendLine($"Chek №{order.CheckNumber}");
                receipt.AppendLine($"Stol: {order.TableNumber}");
                receipt.AppendLine($"Ofitsiant: {order.WaiterName}");
                receipt.AppendLine(new string('-', 32));

                int itemNumber = 1;
                foreach (var item in order.Orders)
                {
                    string name = item.Nomi.Length > 15 ? item.Nomi.Substring(0, 15) : item.Nomi.PadRight(15);
                    string qty = item.Soni.ToString().PadLeft(4);
                    string price = item.Narxi.ToString("0.00").PadLeft(7);
                    string amount = item.Summa.ToString("0.00").PadLeft(7);
                    receipt.AppendLine($"{itemNumber++.ToString().PadLeft(2)} {name} {qty} {price} {amount}");
                }

                receipt.AppendLine(new string('-', 32));
                receipt.Append("\x1B\x61\x02"); // O‘ngga tekislash
                receipt.AppendLine($"Jami:         {order.TotalAmount:0.00}");
                receipt.AppendLine($"Xizmat haqi:  {order.ServiceFee:0.00}");
                receipt.Append("\x1D\x21\x01"); // Katta shrift
                receipt.AppendLine($"UMUMIY:       {order.GrandTotal:0.00}");
                receipt.Append("\x1D\x21\x00"); // Normal shrift

                receipt.Append("\x1B\x61\x01"); // Markazga joylash
                receipt.AppendLine("Tashrifingiz uchun rahmat!");
                receipt.Append("\x1D\x56\x00"); // Qog‘ozni kesish

                // Printerga ESC/POS buyrug‘ini yuborish
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
}