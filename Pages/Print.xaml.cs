using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Newtonsoft.Json;

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

        private void btnPrint_Click(object sender, RoutedEventArgs e)
        {
            // Your existing print logic
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

            PrintCheck(printOrder);
        }

        private decimal GetDecimalValueFromText(string text)
        {
            // Extract numeric value from formatted currency text
            string numericText = text.Replace("so'm", "").Replace(" ", "").Replace(",", "");
            decimal.TryParse(numericText, out decimal result);
            return result;
        }

        private void PrintCheck(PrintOrder order)
        {
            try
            {
                // Create a print dialog
                var printDialog = new System.Windows.Controls.PrintDialog();

                // Create print content
                var printContent = CreatePrintContent(order);

                // Apply print settings
                printContent.Width = printDialog.PrintableAreaWidth;
                printContent.Height = printDialog.PrintableAreaHeight;

                // Print
                printDialog.PrintVisual(printContent, $"Check #{order.CheckNumber}");

                // Show success message
                MessageBox.Show($"Chek №{order.CheckNumber} muvaffaqiyatli chop etildi", "Xabar", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Chop etishda xatolik: {ex.Message}", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private FrameworkElement CreatePrintContent(PrintOrder order)
        {
            // Create a print template
            var grid = new Grid();
            grid.Background = Brushes.White;

            var stackPanel = new StackPanel();
            stackPanel.Margin = new Thickness(20);

            // Restaurant name
            var txtRestaurantName = new TextBlock
            {
                Text = order.RestaurantName,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };
            stackPanel.Children.Add(txtRestaurantName);

            // Date and time
            var txtDateTime = new TextBlock
            {
                Text = $"{order.OrderDate} {order.OrderTime}",
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 5)
            };
            stackPanel.Children.Add(txtDateTime);

            // Check number
            var txtCheckNumber = new TextBlock
            {
                Text = $"Chek №{order.CheckNumber}",
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 5)
            };
            stackPanel.Children.Add(txtCheckNumber);

            // Table number
            var txtTableNumber = new TextBlock
            {
                Text = $"Stol: {order.TableNumber}",
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 5)
            };
            stackPanel.Children.Add(txtTableNumber);

            // Waiter
            var txtWaiter = new TextBlock
            {
                Text = $"Ofitsiant: {order.WaiterName}",
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 15)
            };
            stackPanel.Children.Add(txtWaiter);

            // Separator
            var separator1 = new Rectangle
            {
                Height = 1,
                Fill = Brushes.Black,
                Margin = new Thickness(0, 0, 0, 10)
            };
            stackPanel.Children.Add(separator1);

            // Header
            var headerGrid = new Grid();
            headerGrid.Margin = new Thickness(0, 0, 0, 5);

            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

            // Header texts
            var txtHeaderNo = new TextBlock { Text = "№", FontWeight = FontWeights.Bold };
            var txtHeaderName = new TextBlock { Text = "Nomi", FontWeight = FontWeights.Bold };
            var txtHeaderQty = new TextBlock { Text = "Soni", FontWeight = FontWeights.Bold };
            var txtHeaderPrice = new TextBlock { Text = "Narxi", FontWeight = FontWeights.Bold };
            var txtHeaderAmount = new TextBlock { Text = "Summa", FontWeight = FontWeights.Bold };

            // Add header texts to grid
            Grid.SetColumn(txtHeaderNo, 0);
            Grid.SetColumn(txtHeaderName, 1);
            Grid.SetColumn(txtHeaderQty, 2);
            Grid.SetColumn(txtHeaderPrice, 3);
            Grid.SetColumn(txtHeaderAmount, 4);

            headerGrid.Children.Add(txtHeaderNo);
            headerGrid.Children.Add(txtHeaderName);
            headerGrid.Children.Add(txtHeaderQty);
            headerGrid.Children.Add(txtHeaderPrice);
            headerGrid.Children.Add(txtHeaderAmount);

            stackPanel.Children.Add(headerGrid);

            // Separator
            var separator2 = new Rectangle
            {
                Height = 1,
                Fill = Brushes.Black,
                Margin = new Thickness(0, 0, 0, 10)
            };
            stackPanel.Children.Add(separator2);

            // Items
            for (int i = 0; i < order.Orders.Count; i++)
            {
                var item = order.Orders[i];

                var itemGrid = new Grid();
                itemGrid.Margin = new Thickness(0, 0, 0, 5);

                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

                // Item details
                var txtItemNo = new TextBlock { Text = (i + 1).ToString(), TextWrapping = TextWrapping.Wrap };
                var txtItemName = new TextBlock { Text = item.Nomi, TextWrapping = TextWrapping.Wrap };
                var txtItemQty = new TextBlock { Text = item.Soni.ToString(), TextAlignment = TextAlignment.Right };
                var txtItemPrice = new TextBlock { Text = FormatCurrency(item.Narxi), TextAlignment = TextAlignment.Right };
                var txtItemAmount = new TextBlock { Text = FormatCurrency(item.Summa), TextAlignment = TextAlignment.Right };

                // Add item details to grid
                Grid.SetColumn(txtItemNo, 0);
                Grid.SetColumn(txtItemName, 1);
                Grid.SetColumn(txtItemQty, 2);
                Grid.SetColumn(txtItemPrice, 3);
                Grid.SetColumn(txtItemAmount, 4);

                itemGrid.Children.Add(txtItemNo);
                itemGrid.Children.Add(txtItemName);
                itemGrid.Children.Add(txtItemQty);
                itemGrid.Children.Add(txtItemPrice);
                itemGrid.Children.Add(txtItemAmount);

                stackPanel.Children.Add(itemGrid);
            }

            // Separator
            var separator3 = new Rectangle
            {
                Height = 1,
                Fill = Brushes.Black,
                Margin = new Thickness(0, 10, 0, 10)
            };
            stackPanel.Children.Add(separator3);

            // Total
            var totalGrid = new Grid();
            totalGrid.Margin = new Thickness(0, 0, 0, 5);

            totalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            totalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

            // Total amount
            var txtTotalLabel = new TextBlock { Text = "Jami:", FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right };
            var txtTotal = new TextBlock { Text = FormatCurrency(order.TotalAmount), FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Right };

            Grid.SetColumn(txtTotalLabel, 0);
            Grid.SetColumn(txtTotal, 1);

            totalGrid.Children.Add(txtTotalLabel);
            totalGrid.Children.Add(txtTotal);

            stackPanel.Children.Add(totalGrid);

            // Service fee
            var serviceGrid = new Grid();
            serviceGrid.Margin = new Thickness(0, 0, 0, 5);

            serviceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            serviceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

            var txtServiceLabel = new TextBlock { Text = "Xizmat haqi:", HorizontalAlignment = HorizontalAlignment.Right };
            var txtService = new TextBlock { Text = FormatCurrency(order.ServiceFee), TextAlignment = TextAlignment.Right };

            Grid.SetColumn(txtServiceLabel, 0);
            Grid.SetColumn(txtService, 1);

            serviceGrid.Children.Add(txtServiceLabel);
            serviceGrid.Children.Add(txtService);

            stackPanel.Children.Add(serviceGrid);

            // Grand total
            var grandTotalGrid = new Grid();
            grandTotalGrid.Margin = new Thickness(0, 5, 0, 20);

            grandTotalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grandTotalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

            var txtGrandTotalLabel = new TextBlock { Text = "UMUMIY:", FontWeight = FontWeights.Bold, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Right };
            var txtGrandTotal = new TextBlock { Text = FormatCurrency(order.GrandTotal), FontWeight = FontWeights.Bold, FontSize = 14, TextAlignment = TextAlignment.Right };

            Grid.SetColumn(txtGrandTotalLabel, 0);
            Grid.SetColumn(txtGrandTotal, 1);

            grandTotalGrid.Children.Add(txtGrandTotalLabel);
            grandTotalGrid.Children.Add(txtGrandTotal);

            stackPanel.Children.Add(grandTotalGrid);

            // Thank you message
            var txtThankYou = new TextBlock
            {
                Text = "Tashrifingiz uchun rahmat!",
                FontSize = 14,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };
            stackPanel.Children.Add(txtThankYou);

            grid.Children.Add(stackPanel);

            return grid;
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