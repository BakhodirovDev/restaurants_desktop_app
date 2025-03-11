using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Restaurants.Class;
using Restaurants.Class.Contractor_GetList;
using Restaurants.Class.ContractorOrder_Get;
using Restaurants.Class.Printer;
using Restaurants.Helper;
using Restaurants.Printer;
using System.Drawing.Printing;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Restaurants.Classes
{
    public partial class Kassa : Window
    {
        private readonly HttpClient _httpClient;
        private DispatcherTimer timer;
        private DispatcherTimer timeTimer;
        private int countdown = 3;
        private int currentSelectedTable = -1;
        private readonly Dictionary<int, List<ContractorOrder>> tableOrders = new();
        private readonly XPrinter _printer;

        public Kassa(HttpClient httpClient, XPrinter printer)
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
            try
            {
                // UI elementlarini init qilish
                if (tglAutoRefresh != null)
                {
                    tglAutoRefresh.IsChecked = false;
                    StopAutoRefresh();
                }

                // Ma'lumotlarni olish
                UpdateStatusLabels();
                await LoadTablesAsync();
                await GetData();

                lblLastUpdate.Text = "Oxirgi yangilanish: " + DateTime.Now.ToString("HH:mm:ss");
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
                pageSize = 20,
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
            string token = await EnsureValidTokenAsync();

            var request = new HttpRequestMessage(HttpMethod.Get, "https://crm-api.webase.uz/crm/ContractorOrder/Get");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            HttpResponseMessage response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                string jsonResponse = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<ContractorOrder>(jsonResponse);
            }
            else
            {
                return null;
            }
        }
        private async Task<ContractorOrder> ContractorOrderGetById(int notCompletedOrderId)
        {
            return await SendApiRequestAsync<ContractorOrder>(
                HttpMethod.Get,
                $"https://crm-api.webase.uz/crm/ContractorOrder/Get/{notCompletedOrderId}?forEdit=false&isClone=false&forCashDocument=false"
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
                else
                {
                    MessageBox.Show("No table data returned from the API.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            tables = tables.OrderByDescending(t => t.FirstName).ToList(); // To'g'ri tartiblash

            foreach (var table in tables)
            {
                if (!int.TryParse(table.FirstName, out int tableNumber)) continue;

                // Order statistikasini hisoblash
                int productsCount = 0;
                int completedProductsCount = 0;
                string responsibleName = "Null";

                if (tableOrders.TryGetValue(tableNumber, out var orders) && orders != null && orders.Any())
                {
                    productsCount = orders.Sum(o => o.TotalProductsCount);
                    completedProductsCount = orders.Sum(o => o.CompletedProductsCount);
                    responsibleName = orders.FirstOrDefault()?.Responsible ?? "Null";
                }

                string orderCountText = productsCount > 0 ? $"{completedProductsCount}/{productsCount}" : "0/0";
                bool isBusy = table.HasNotCompletedOrder;

                // Button yaratish
                Button tableButton = new()
                {
                    Content = tableNumber.ToString(),
                    Tag = new TableButtonData
                    {
                        TableNumber = tableNumber,
                        ContractorId = table.Id,
                        NotCompletedOrderId = table.NotCompletedOrderId,
                        OrderCountText = orderCountText,
                        ResponsibleName = responsibleName
                    },
                    Style = isBusy ? (Style)FindResource("BusyTableButtonStyle") : (Style)FindResource("TableButtonStyle")
                };

                tableButton.Click += TableButton_Click;
                tablesPanel.Children.Add(tableButton);
            }
        }


        private async void btnCompleteOrder_Click(object sender, RoutedEventArgs e)
        {
            if (currentSelectedTable <= 0)
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

                var tableInfo = tables.Rows.FirstOrDefault(r => r.FirstName == currentSelectedTable.ToString());
                if (tableInfo?.NotCompletedOrderId == null) return;

                // Foydalanuvchidan tasdiqlashni so'rash
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
                        // Stolni yangilash
                        if (tableOrders.ContainsKey(currentSelectedTable))
                        {
                            tableOrders[currentSelectedTable].Clear();
                        }

                        LoadTableOrders(currentSelectedTable);

                        // UI yangilash
                        var btn = tablesPanel.Children.OfType<Button>()
                            .FirstOrDefault(b => b.Tag is TableButtonData td && td.TableNumber == currentSelectedTable);

                        if (btn != null && btn.Tag is TableButtonData btnData)
                        {
                            btnData.IsBusy = false;
                            btnData.OrderCountText = "0/0";
                            btn.Tag = btnData;
                            btn.Style = (Style)FindResource("SelectedTableButtonStyle");
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

            // Token mavjud bo'lsa, qaytarish
            if (!string.IsNullOrEmpty(token))
            {
                // Token muddati tekshirilishi mumkin (agar accessTokenExpireAt qo'shilgan bo'lsa)
                if (Settings.Default.accessTokenExpireAt != null &&
                    DateTime.TryParse(Settings.Default.accessTokenExpireAt, out DateTime expireDate) &&
                    expireDate > DateTime.Now.AddMinutes(5))
                {
                    return token;
                }
            
            }
            // Token yo'q yoki muddati o'tgan bo'lsa, refreshToken bilan yangilash
            string refreshToken = Settings.Default.RefreshToken;
            if (string.IsNullOrEmpty(refreshToken))
            {
                MessageBox.Show("Siz tizimdan chiqib ketgansiz. Iltimos, qayta kiring.", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Warning);
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

                    // Yangi tokenlarni saqlash
                    Settings.Default.AccessToken = newTokenResponse?.AccessToken;
                    Settings.Default.RefreshToken = newTokenResponse?.RefreshToken;
                    Settings.Default.Save();

                    return newTokenResponse?.AccessToken;
                }
                else
                {
                    // Refresh token xatoligi
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

                // Request header'larini qo'shish
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


                // Request body'sini qo'shish (agar kerak bo'lsa)
                if (requestData != null)
                {
                    var content = new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json");
                    request.Content = content;
                }

                HttpResponseMessage response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<T>(jsonResponse);
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

        private void TableButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button clickedButton) return;
            if (clickedButton.Tag is not TableButtonData buttonData) return;

            int tableNumber = buttonData.TableNumber;
            int? notCompletedOrderId = buttonData.NotCompletedOrderId;
            string responsibleName = buttonData.ResponsibleName;

            // Oldingi tanlangan stolni normal ko'rinishga qaytarish
            if (currentSelectedTable > 0)
            {
                ResetPreviousTableButtonStyle();
            }

            // Yangi tanlangan stolni belgilash
            currentSelectedTable = tableNumber;
            clickedButton.Style = (Style)FindResource("SelectedTableButtonStyle");

            // Ma'lumotlarni yangilash
            lblOfitsiantValue.Text = responsibleName;
            lblStolValue.Text = "#" + tableNumber;

            // Stol ma'lumotlarini yuklash
            LoadTableOrders(tableNumber);

            // Agar tugallanmagan buyurtma bo'lsa, ma'lumotlarni olish
            if (notCompletedOrderId.HasValue)
            {
                GetDataForTable(notCompletedOrderId.Value);
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

                btn.Style = btnIsBusy ?
                    (Style)FindResource("BusyTableButtonStyle") :
                    (Style)FindResource("TableButtonStyle");
                break;
            }
        }

        private void LoadTableOrders(int tableNumber)
        {
            lvItems.Items.Clear();

            // Summa maydonlarini tozalash
            lblAmountValue.Text = "0.0 UZS";
            lblAdditinalPaymentValue.Text = "0.0 UZS";
            lblTotalAmountValue.Text = "0.0 UZS";

            if (!tableOrders.TryGetValue(tableNumber, out var orders) || orders == null || !orders.Any())
                return;

            // Barcha stol ma'lumotlarini yig'ish
            var orderItems = orders
                .SelectMany(o => o.Tables ?? new List<ContractorOrderTable>())
                .Where(t => t != null && !string.IsNullOrEmpty(t.ProductShortName))
                .Select((item, index) => new OrderItem
                {
                    Index = index + 1,
                    Id = item.Id,
                    ProductShortName = item.ProductShortName ?? "No Name",
                    ContractorRequirement = item.ContractorRequirement ?? "No Details",
                    Quantity = (int)Math.Max(1, item.Quantity), // Minimum 1
                    EstimatedPrice = item.EstimatedPrice,
                    Amount = item.Amount,
                    TableNumber = tableNumber
                })
                .ToList();

            // Listview'ga qo'shish
            foreach (var item in orderItems)
            {
                lvItems.Items.Add(item);
            }

            // Summa ma'lumotlarini yangilash
            if (orders.Any())
            {
                var order = orders.FirstOrDefault();
                if (order != null)
                {
                    lblAmountValue.Text = $"{order.Amount} UZS";
                    lblAdditinalPaymentValue.Text = $"{order.AdditinalPayment} UZS";
                    lblTotalAmountValue.Text = $"{order.TotalAmount} UZS";
                }
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
                var data = await ContractorOrderGet();
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
            catch (Exception ex)
            {
                MessageBox.Show($"Error occurred while fetching order data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void GetDataForTable(int notCompletedOrderId)
        {
            try
            {
                var data = await ContractorOrderGetById(notCompletedOrderId);
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
                else
                {
                    string errorMessage = $"No valid table number found for Order ID {notCompletedOrderId}.";
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
            if (data == null) return;

            // Faqat orders mavjud bo'lganda ishlaydigan kod
            if (data.Tables == null || !data.Tables.Any()) return;

            // Table raqamini aniqlash
            foreach (var table in data.Tables.Where(t => t != null))
            {
                int tableNumber = table.OrderNumber;
                if (tableNumber <= 0) continue;

                // Stollar uchun ContractorOrder ro'yxatini yaratish
                if (!tableOrders.ContainsKey(tableNumber))
                {
                    tableOrders[tableNumber] = new List<ContractorOrder>();
                }

                // Mavjud orderni yangilash yoki yangi orderni qo'shish 
                var existingOrder = tableOrders[tableNumber].FirstOrDefault(o => o.Id == data.Id);
                if (existingOrder != null)
                {
                    // O'rniga qo'yish
                    int index = tableOrders[tableNumber].IndexOf(existingOrder);
                    tableOrders[tableNumber][index] = data;
                }
                else
                {
                    tableOrders[tableNumber].Add(data);
                }

                // Button stilini yangilash
                UpdateTableButtonStyle(tableNumber, data);
            }

            // Agar joriy tanlangan stol bo'lsa, ma'lumotlarni qayta yuklash
            if (currentSelectedTable > 0)
            {
                LoadTableOrders(currentSelectedTable);
            }
        }

        private void UpdateTableButtonStyle(int tableNumber, ContractorOrder order)
        {
            foreach (Button btn in tablesPanel.Children)
            {
                if (btn.Tag is not TableButtonData tagData || tagData.TableNumber != tableNumber) continue;

                int productsCount = order.TotalProductsCount;
                int completedProductsCount = order.CompletedProductsCount;
                bool isBusy = productsCount > 0 && (order.StatusId != 3);

                // Order count tekstini yangilash
                tagData.OrderCountText = productsCount > 0 ? $"{completedProductsCount}/{productsCount}" : "0/0";
                btn.Tag = tagData;

                // Stilni yangilash
                if (tableNumber != currentSelectedTable)
                {
                    btn.Style = isBusy
                        ? (Style)FindResource("BusyTableButtonStyle")
                        : (Style)FindResource("TableButtonStyle");
                }
                break;
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
            LoadTableOrders(tableNumber);
        }

        private void btnGetData_Click(object sender, RoutedEventArgs e)
        {
            countdown = 3;
            lblCountdown.Text = $"({countdown})";
            _ = ReLoadData();
        }

        private void StartAutoRefresh()
        {
            if (timer != null)
            {
                countdown = 3;
                lblCountdown.Text = $"({countdown})";
                timer.Start();
            }
            UpdateStatusLabels();
        }

        private void StopAutoRefresh()
        {
            if (timer != null)
            {
                timer.Stop();
            }
            UpdateStatusLabels();
        }
        private void HandleScroll(ScrollChangedEventArgs e)
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

        // Timer Tick event handler
        private void Timer_Tick(object sender, EventArgs e)
        {
            countdown--;
            lblCountdown.Text = $"({countdown})";

            if (countdown <= 0)
            {
                countdown = 3;
                _ = ReLoadData();
            }
        }
        private async Task ReLoadData()
        {
            try
            {
                // UI elementlarini yangilash
                countdown = 3;
                lblCountdown.Text = $"({countdown})";
                lblLastUpdate.Text = "Yangilanmoqda...";

                // Ma'lumotlarni qayta yuklash
                tableOrders.Clear();
                await GetData();
                await LoadTablesAsync();

                // Yangilangandan so'ng vaqtni ko'rsatish
                lblLastUpdate.Text = "Oxirgi yangilanish: " + DateTime.Now.ToString("HH:mm:ss");

                // Tanlangan stol ma'lumotlarini qayta yuklash
                if (currentSelectedTable > 0)
                {
                    LoadTableOrders(currentSelectedTable);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ma'lumotlarni yangilashda xatolik: {ex.Message}", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnPrint_Click(object sender, RoutedEventArgs e)
        {
            if (currentSelectedTable <= 0)
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

                MessageBox.Show("Chek muvaffaqiyatli chop etildi", "Xabar", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Chop etishda xatolik yuz berdi: {ex.Message}", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private PrintOrder CreatePrintOrder(ContractorOrder order)
        {
            return new PrintOrder
            {
                TableNumber = currentSelectedTable,
                RestaurantName = order.OrganizationAreasOfActivity ?? "Null",
                WaiterName = order.Responsible ?? "Null",
                OrderDate = order.DocDate ?? "Null",
                OrderTime = order.DocTime ?? "Null",
                CheckNumber = order.DocNumber ?? "Null",
                Orders = order.Tables?
                    .Where(item => item != null)
                    .Select(item => new OrderItem
                    {
                        Id = item.Id,
                        ProductShortName = item.ProductShortName ?? "No Name",
                        ContractorRequirement = item.ContractorRequirement ?? "No Details",
                        Quantity = (int)Math.Max(1, item.Quantity), // Minimum 1
                        EstimatedPrice = Math.Round(Math.Max(0, item.EstimatedPrice), 1),
                        Amount = Math.Round(Math.Max(0, item.Amount), 1),
                        TableNumber = currentSelectedTable
                    })
                    .Where(item => !string.IsNullOrEmpty(item.ProductShortName))
                    .ToList() ?? new List<OrderItem>(),
                TotalAmount = Math.Round(order.Amount, 1),
                ServiceFee = Math.Round(order.AdditinalPayment, 1),
                GrandTotal = Math.Round(order.TotalAmount, 1)
            };
        }
        private string BuildPrintText(PrintOrder order)
        {
            var sb = new StringBuilder();

            // Printer inizializatsiyasi
            sb.Append("\x1B\x40"); // ESC @

            try
            {
                // Markaziy tekislash va qalin shrift
                sb.Append("\x1B\x61\x01"); // ESC a 1
                sb.Append("\x1B\x45\x01"); // ESC E 1

                // Sarlavha ma'lumotlari
                sb.AppendLine($"Zakaz N#: {order.CheckNumber}");
                sb.AppendLine($"Restoran: {order.RestaurantName}");
                sb.AppendLine($"Ofitsiant: {order.WaiterName}");
                sb.AppendLine($"Sana: {order.OrderDate}   Vaqt: {order.OrderTime}");
                sb.AppendLine($"Stol: {order.TableNumber}");

                // Oddiy shrift
                sb.Append("\x1B\x45\x00"); // ESC E 0

                // Ajratuvchi chiziq
                sb.AppendLine(new string('-', 48));

                // Chap tekislash
                sb.Append("\x1B\x61\x00"); // ESC a 0

                // Jadval sarlavhasi
                sb.AppendLine("Mahsulot                    |    Soni    |    Summa");
                sb.AppendLine(new string('-', 48));

                // Maxsulotlar ro'yxati
                foreach (var item in order.Orders ?? new List<OrderItem>())
                {
                    string amountFormatted = Math.Round(item.Amount, 1).ToString("0.0").PadLeft(8);
                    // 24 belgigacha maydon ajratilgan
                    string productName = item.ProductShortName.Length > 24
                        ? item.ProductShortName.Substring(0, 24)
                        : item.ProductShortName.PadRight(24);

                    sb.AppendLine($"{productName} | {item.Quantity.ToString().PadLeft(8)} | {amountFormatted} UZS");
                }

                // Ajratuvchi chiziq
                sb.AppendLine(new string('-', 48));

                // O'ng tekislash
                sb.Append("\x1B\x61\x02"); // ESC a 2

                // Summa ma'lumotlari
                string totalFormatted = Math.Round(order.TotalAmount, 1).ToString("0.0").PadLeft(8);
                string serviceFeeFormatted = Math.Round(order.ServiceFee, 1).ToString("0.0").PadLeft(8);
                string grandTotalFormatted = Math.Round(order.GrandTotal, 1).ToString("0.0").PadLeft(8);

                sb.AppendLine($"Summa: {totalFormatted} UZS");
                sb.AppendLine($"Xizmat haqi(12%): {serviceFeeFormatted} UZS");
                sb.AppendLine(new string('-', 48));
                sb.AppendLine($"Jami: {grandTotalFormatted} UZS");

                // Markaziy tekislash
                sb.Append("\x1B\x61\x01"); // ESC a 1
                sb.AppendLine("Tashrifingiz uchun rahmat!");

                // Qog'ozni kesish
                sb.Append("\x1D\x56\x00"); // GS V 0
            }
            catch (Exception ex)
            {
                // Xatolarni qayd qilish
                Console.WriteLine($"BuildPrintText xatolik: {ex.Message}");
            }

            return sb.ToString();
        }

        private void btnLogout_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Tokenlarni tozalash
                Settings.Default.AccessToken = null;
                Settings.Default.RefreshToken = null;
                Settings.Default.accessTokenExpireAt = null;
                Settings.Default.Save();

                // Timerlarni to'xtatish
                if (timer != null) timer.Stop();
                if (timeTimer != null) timeTimer.Stop();

                // Login oynasini ochish
                new MainWindow().Show();
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Tizimdan chiqishda xatolik: {ex.Message}", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
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

        
    }

    public class TableButtonData
    {
        public int TableNumber { get; set; }
        public int ContractorId { get; set; }
        public int? NotCompletedOrderId { get; set; }
        public string OrderCountText { get; set; } = "0/0";
        public string ResponsibleName { get; set; } = "Null";
        public bool IsBusy { get; set; }
    }
}