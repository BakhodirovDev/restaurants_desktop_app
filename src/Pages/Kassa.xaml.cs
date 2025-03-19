using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Restaurants.Class;
using Restaurants.Class.Contractor_GetList;
using Restaurants.Class.ContractorOrder_Get;
using Restaurants.Class.Printer;
using Restaurants.Helper;
using Restaurants.Printer;
using System.Drawing.Printing;
using System.Globalization;
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
        private string currentSelectedTable = "-1"; // String sifatida boshlang'ich qiymat
        private readonly Dictionary<string, List<ContractorOrder>> tableOrders = new();
        private readonly XPrinter _printer;

        public Kassa(HttpClient httpClient, XPrinter printer)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            InitializeComponent();

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
        }

        private void Kassa_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            _ = LoadTablesAsync();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (tglAutoRefresh != null)
                {
                    tglAutoRefresh.IsChecked = false;
                    StopAutoRefresh();
                }

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
            double panelWidth = tablesPanel.ActualWidth > 0 ? tablesPanel.ActualWidth : 600;
            int buttonsPerRow = (int)(panelWidth / 140);
            double buttonWidth = (panelWidth / buttonsPerRow) - 20;

            foreach (var table in tables)
            {
                string tableName = table.FirstName;
                int productsCount = 0;
                int completedProductsCount = 0;
                string responsibleName = "Null";

                if (tableOrders.TryGetValue(tableName, out var orders) && orders != null && orders.Any())
                {
                    productsCount = orders.Sum(o => o.TotalProductsCount);
                    completedProductsCount = orders.Sum(o => o.CompletedProductsCount);
                    responsibleName = orders.FirstOrDefault()?.Responsible ?? "Null";
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

                // Yangilanishdan keyin rangni to'g'ri qo'llash uchun ApplyTableStyle ishlatiladi
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

            // Agar yangi tanlangan stol boshqa bo'lsa, avvalgi stolni reset qilamiz
            if (!string.IsNullOrEmpty(currentSelectedTable) && currentSelectedTable != tableNumber)
            {
                ResetPreviousTableButtonStyle();
            }

            currentSelectedTable = tableNumber;
            lblOfitsiantValue.Text = responsibleName;
            lblStolValue.Text = "#" + tableNumber;

            bool isBusy = tableOrders.TryGetValue(tableNumber, out var orders) && orders != null && orders.Any(o => o.StatusId != 3);
            ApplyTableStyle(clickedButton, isBusy, true); // Yangi tanlangan stolga mos rang qo'llaniladi

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
                                 orders.Any(o => o.StatusId != 3); // Zakaz borligini tekshirish

                ApplyTableStyle(btn, btnIsBusy, false); // Tanlov olib tashlanadi, agar band bo'lsa qizil qiladi
                break;
            }
        }

        private void LoadTableOrders(string tableNumber)
        {
            lvItems.Items.Clear();
            lblAmountValue.Text = "0.0 UZS";
            lblAdditinalPaymentValue.Text = "0.0 UZS";
            lblTotalAmountValue.Text = "0.0 UZS";

            if (!tableOrders.TryGetValue(tableNumber, out var orders) || orders == null || !orders.Any())
            {
                Console.WriteLine($"No orders found for table {tableNumber}");
                return;
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
                    lblAmountValue.Text = $"{order.Amount:F1} UZS";
                    lblAdditinalPaymentValue.Text = $"{order.AdditinalPayment:F1} UZS";
                    lblTotalAmountValue.Text = $"{order.TotalAmount:F1} UZS";
                }
            }

            lvItems.Items.Refresh();
        }

        private async Task GetData()
        {
            try
            {
                var data = await ContractorOrderGet();
                if (data == null)
                {
                    MessageBox.Show("No order data returned from the API.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    lblRestoranValue.Text = "Null";
                    lblOfitsiantValue.Text = "Null";
                    lblSanaValue.Text = "Null";
                    lblVaqtValue.Text = "Null";
                    lblChekRaqamiValue.Text = "#Null";
                    ProcessApiData(new ContractorOrder());
                    return;
                }

                lblRestoranValue.Text = data.OrganizationAreasOfActivity ?? "Null";
                lblOfitsiantValue.Text = data.Responsible ?? "Null";
                lblSanaValue.Text = data.DocDate ?? "Null";
                lblVaqtValue.Text = data.DocTime ?? "Null";
                lblChekRaqamiValue.Text = "#" + (data.DocNumber ?? "Null");

                ProcessApiData(data);
                lblLastUpdate.Text = "Oxirgi yangilanish: " + DateTime.Now.ToString("HH:mm:ss");

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
                var data = await ContractorOrderGetById(notCompletedOrderId);
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

        private async Task ReLoadData()
        {
            try
            {
                countdown = 3;
                lblCountdown.Text = $"({countdown})";
                lblLastUpdate.Text = "Yangilanmoqda...";

                string previouslySelectedTable = currentSelectedTable;

                tableOrders.Clear();
                await GetData();
                await LoadTablesAsync();

                lblLastUpdate.Text = "Oxirgi yangilanish: " + DateTime.Now.ToString("HH:mm:ss");

                if (!string.IsNullOrEmpty(previouslySelectedTable) && previouslySelectedTable != "-1")
                {
                    currentSelectedTable = previouslySelectedTable;
                    foreach (Button btn in tablesPanel.Children)
                    {
                        if (btn.Tag is TableButtonData btnData && btnData.TableNumber == currentSelectedTable)
                        {
                            bool isBusy = tableOrders.TryGetValue(currentSelectedTable, out var orders) && orders != null && orders.Any(o => o.StatusId != 3);
                            ApplyTableStyle(btn, isBusy, true); // Tanlangan stolning rangi saqlanadi
                            if (btnData.NotCompletedOrderId.HasValue)
                            {
                                await GetDataForTable(btnData.NotCompletedOrderId.Value);
                            }
                            LoadTableOrders(currentSelectedTable);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ma'lumotlarni yangilashda xatolik: {ex.Message}", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
                lblStatus.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80));
            }
            else
            {
                lblStatus.Text = "Avtomatik yangilanish o'chirilgan";
                lblStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 152, 0));
            }
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
                        Quantity = (int)Math.Max(1, item.Quantity),
                        EstimatedPrice = Math.Round(Math.Max(0, item.EstimatedPrice), 1),
                        Amount = Math.Round(Math.Max(0, item.Amount), 1),
                        TableNumber = int.TryParse(currentSelectedTable, out int num) ? num : 0
                    })
                    .Where(item => !string.IsNullOrEmpty(item.ProductShortName))
                    .ToList() ?? new List<OrderItem>(),
                TotalAmount = Math.Round(order.Amount, 1),
                ServiceFee = Math.Round(order.AdditinalPayment, 1),
                GrandTotal = Math.Round(order.TotalAmount, 1),
                AdditionalPercentage = order.AdditionalPayments.Count > 0 ? order.AdditionalPayments[0].AdditionalPercentage : 0
            };
        }

        private string BuildPrintText(PrintOrder order)
        {
            var sb = new StringBuilder();
            sb.Append("\x1B\x40");

            try
            {
                sb.Append("\x1B\x61\x01");
                sb.Append("\x1B\x45\x01");
                sb.AppendLine($"Zakaz N#: {order.CheckNumber}");
                sb.AppendLine($"Restoran: {order.RestaurantName}");
                sb.AppendLine($"Ofitsiant: {order.WaiterName}");
                sb.AppendLine($"Sana: {order.OrderDate}   Vaqt: {order.OrderTime}");
                sb.AppendLine($"Stol: {order.TableNumber}");

                sb.Append("\x1B\x45\x00");
                sb.AppendLine(new string('-', 48));

                sb.Append("\x1B\x61\x00");
                sb.AppendLine("Mahsulot                    |    Soni    |    Summa");
                sb.AppendLine(new string('-', 48));

                foreach (var item in order.Orders ?? new List<OrderItem>())
                {
                    string amountFormatted = Math.Round(item.Amount, 1).ToString("0.0").PadLeft(8);
                    string productName = item.ProductShortName.Length > 24
                        ? item.ProductShortName.Substring(0, 24)
                        : item.ProductShortName.PadRight(24);

                    sb.AppendLine($"{productName} | {item.Quantity.ToString().PadLeft(8)} | {amountFormatted} UZS");
                }

                sb.AppendLine(new string('-', 48));
                sb.Append("\x1B\x61\x02");

                string totalFormatted = Math.Round(order.TotalAmount, 1).ToString("0.0").PadLeft(8);
                string serviceFeeFormatted = Math.Round(order.ServiceFee, 1).ToString("0.0").PadLeft(8);
                string grandTotalFormatted = Math.Round(order.GrandTotal, 1).ToString("0.0").PadLeft(8);

                sb.AppendLine($"Summa: {totalFormatted} UZS");
                sb.AppendLine($"Xizmat haqi(12%): {serviceFeeFormatted} UZS");
                sb.AppendLine(new string('-', 48));
                sb.AppendLine($"Jami: {grandTotalFormatted} UZS");

                sb.Append("\x1B\x61\x01");
                sb.AppendLine("Tashrifingiz uchun rahmat!");
                sb.Append("\x1D\x56\x00");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"BuildPrintText xatolik: {ex.Message}");
            }

            return sb.ToString();
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
                            ApplyTableStyle(btn, false, true); // Bo'sh tanlangan stol sifatida yangilash
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

        private void btnCashPayment_Click(object sender, RoutedEventArgs e)
        {
            // Logic for cash payment
            // You could set a payment method flag or variable here
            ///PaymentMethod = "Cash";

            // You might want to visually indicate which payment method was selected
            btnCashPayment.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#388E3C"));
            btnCardPayment.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#673AB7"));

            // Then proceed with the print operation if needed
            // PrintCashReceipt();
        }

        private void btnCardPayment_Click(object sender, RoutedEventArgs e)
        {
            // Logic for card payment
            // You could set a payment method flag or variable here
            ///PaymentMethod = "Card";

            // You might want to visually indicate which payment method was selected
            btnCardPayment.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#512DA8"));
            btnCashPayment.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));

            // Then proceed with the print operation if needed
            // PrintCardReceipt();
        }

    }

    public class TableButtonData
    {
        public string TableNumber { get; set; }
        public int ContractorId { get; set; }
        public int? NotCompletedOrderId { get; set; }
        public string OrderCountText { get; set; } = "0/0";
        public string ResponsibleName { get; set; } = "Null";
        public bool IsBusy { get; set; }
    }
}