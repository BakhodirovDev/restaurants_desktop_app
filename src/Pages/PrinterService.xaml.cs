using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Net.Http;
using System.Net.Sockets;
using Microsoft.AspNetCore.SignalR.Client;
using System.IO;
using System.Text.Json;
using Restaurants.Printer;
using Restaurants.Class;
using Restaurants.Class.ContractorOrder_Get;
using Restaurants.Class.Printer;
using Restaurants.Helper;
using Restaurants.Services;

namespace Restaurants.Pages
{
    /// <summary>
    /// Printer settings and management window
    /// </summary>
    public partial class PrinterService : Window, IDisposable
    {
        private readonly HttpClient? _httpClient;
        private readonly XPrinter? _printer;
        private HubConnection? _connection;
        private Restaurants.Services.PrinterDiscoveryService _printerDiscoveryService;
        private Restaurants.Services.PrinterInfo? _selectedPrinter;
        private const string PRINTERS_FILE_PATH = "printers.json";
        private bool _isLoadingPrinters = false;
        private IProductCategoryService _productCategoryService;

        public PrinterService()
        {
            InitializeComponent();
            
            // Initialize printer discovery service first to avoid null reference
            _printerDiscoveryService = new PrinterDiscoveryService();
            InitializePrinterDiscovery();
            
            // Set initial UI state
            noPrinterSelectedPanel.Visibility = Visibility.Visible;
            printerDetailsPanel.Visibility = Visibility.Collapsed;
            
            // Initialize button states
            btnTest.IsEnabled = false;
            btnSetDefault.IsEnabled = false;
            btnMapCategories.IsEnabled = false;
            btnAddToSaved.IsEnabled = false;
            btnStopScan.IsEnabled = false;
            btnScanPrinters.IsEnabled = true;
            
            UpdateStatusBar("tayyorlangan. printer qidiruvini boshlang yoki mavjud printerlardan birini tanlang.");
            
            // Initialize product category service
            _productCategoryService = new ProductCategoryService(new HttpClient());
            
            // Load saved printers
            LoadSavedPrinters();
        }

        public PrinterService(HttpClient httpClient, XPrinter printer) : this()
        {
            _httpClient = httpClient;
            _printer = printer;

            // Connect to SignalR if needed
            ConnectToSignalR();
        }

        private void InitializePrinterDiscovery()
        {
            System.Diagnostics.Debug.WriteLine("initializeprinterdiscovery chaqirildi");
            _printerDiscoveryService.PrinterDiscovered += OnPrinterDiscovered;
            _printerDiscoveryService.ScanCompleted += OnScanCompleted;
        }

        private void OnPrinterDiscovered(object sender, Restaurants.Services.PrinterDiscoveredEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"onprinterdiscovered chaqirildi: {e.Printer.Name}");
            
            // Add printer to UI on UI thread
            Dispatcher.Invoke(() => 
            {
                // Check if printer already exists in the list
                bool printerExists = false;
                foreach (Restaurants.Services.PrinterInfo existingPrinter in lvPrinters.Items)
                {
                    if (existingPrinter.Name == e.Printer.Name)
                    {
                        printerExists = true;
                        break;
                    }
                }
                
                if (!printerExists)
                {
                    lvPrinters.Items.Add(e.Printer);
                    System.Diagnostics.Debug.WriteLine($"printer qo'shildi: {e.Printer.Name}");
                    
                    // Hide no printers panel if this is the first printer
                    if (lvPrinters.Items.Count == 1 && noPrintersPanel != null)
                    {
                        noPrintersPanel.Visibility = Visibility.Collapsed;
                    }
                    
                    // Update the UI for default printer if needed
                    UpdateDefaultPrinterVisual();
                    
                    // Note: We no longer automatically save discovered printers
                    // Printers will be saved only when the user clicks the "+" button
                }
            });
        }

        private void OnScanCompleted(object sender, Restaurants.Services.PrinterScanCompletedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"onscancompleted chaqirildi, printerlar soni: {e.DiscoveredPrinters.Count}");
            
            // Update UI on UI thread
            Dispatcher.Invoke(() => 
            {
                btnScanPrinters.IsEnabled = true;
                btnStopScan.IsEnabled = false;
                
                if (e.Cancelled)
                {
                    UpdateStatusBar("printer qidirish to'xtatildi");
                }
                else
                {
                    string message = e.DiscoveredPrinters.Count > 0 
                        ? $"{e.DiscoveredPrinters.Count} ta printer topildi" 
                        : "hech qanday printer topilmadi";
                    
                    UpdateStatusBar(message);
                    
                    // Show message if no printers found
                    if (lvPrinters.Items.Count == 0)
                    {
                        noPrintersPanel.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        noPrintersPanel.Visibility = Visibility.Collapsed;
                    }
                    
                    // Note: We no longer automatically save discovered printers
                    // Printers will be saved only when the user clicks the "+" button
                }
            });
        }

        private void ConnectToSignalR()
        {
            if (_httpClient == null) return;
            
            string accessToken = Settings.Default.AccessToken;
            string userId = Settings.Default.UserId.ToString();
            string organizationId = Settings.Default.OrganizationId.ToString();

            _connection = new HubConnectionBuilder()
                .WithUrl($"ws://crm-api.webase.uz/ws/restarunt", options =>
                {
                    options.AccessTokenProvider = async () => await Task.FromResult(accessToken);
                    options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets;

                    options.Headers["UserId"] = userId;
                    options.Headers["OrganizationId"] = organizationId;
                })
                .WithAutomaticReconnect()
                .Build();

            _connection.On<string>("ReceiveNotification", message =>
            {
                MessageBox.Show($"yangi xabar: {message}");
            });

            _connection.On<object>("OrderData", message =>
            {
                // Handle order data from SignalR
            });

            _connection.On("Ping", async () =>
            {
                await SendPong();
            });

            _ = StartConnection();
        }

        private async Task SendPong()
        {
            if (_connection?.State == HubConnectionState.Connected)
            {
                await _connection.SendAsync("Pong");
            }
        }

        private async Task StartConnection()
        {
            try
            {
                if (_connection != null)
            {
                await _connection.StartAsync();
                    UpdateStatusBar("websocket ulanishi muvaffaqiyatli o'rnatildi");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"signalr ulanishida xatolik: {ex.Message}");
                UpdateStatusBar("websocket ulanishida xatolik yuz berdi");
            }
        }
        
        private void UpdateStatusBar(string message)
        {
            tbStatusBar.Text = message.ToLower();
        }
        
        /// <summary>
        /// Shows the details of a printer in the UI
        /// </summary>
        /// <param name="printer">The printer to show details for</param>
        private void ShowPrinterDetails(Restaurants.Services.PrinterInfo printer)
        {
            if (printer != null)
            {
                _selectedPrinter = printer;
                
                // Update the UI with printer details
                tbPrinterName.Text = printer.Name;
                tbStatus.Text = printer.Status;
                tbConnectionType.Text = printer.ConnectionType;
                tbIpAddress.Text = printer.IsNetworkPrinter ? printer.IpAddress : "-";
                tbPort.Text = printer.IsNetworkPrinter && printer.Port > 0 ? printer.Port.ToString() : "-";
                tbModel.Text = !string.IsNullOrEmpty(printer.Model) ? printer.Model : "-";
                tbDriverName.Text = !string.IsNullOrEmpty(printer.DriverName) ? printer.DriverName : "-";
                tbLocation.Text = !string.IsNullOrEmpty(printer.Location) ? printer.Location : "-";
                
                // Show details panel
                printerDetailsPanel.Visibility = Visibility.Visible;
                noPrinterSelectedPanel.Visibility = Visibility.Collapsed;
                
                // Enable buttons
                btnTest.IsEnabled = true;
                btnSetDefault.IsEnabled = !printer.IsDefault;
                btnSetDefault.Content = printer.IsDefault ? "asosiy printer" : "asosiy printer qilib belgilash";
                btnMapCategories.IsEnabled = true;
                btnAddToSaved.IsEnabled = true;
            }
            else
            {
                _selectedPrinter = null;
                
                // Hide details panel
                printerDetailsPanel.Visibility = Visibility.Collapsed;
                noPrinterSelectedPanel.Visibility = Visibility.Visible;
                
                // Disable buttons
                btnTest.IsEnabled = false;
                btnSetDefault.IsEnabled = false;
                btnMapCategories.IsEnabled = false;
                btnAddToSaved.IsEnabled = false;
            }
        }
        
        #region Event Handlers
        
        private void btnScanPrinters_Click(object sender, RoutedEventArgs e)
        {
            // Clear previous results
            lvPrinters.Items.Clear();
            
            // Show loading indicator
            loadingGrid.Visibility = Visibility.Visible;
            noPrintersPanel.Visibility = Visibility.Collapsed;
            
            // Disable scan button during scan
            btnScanPrinters.IsEnabled = false;
            btnStopScan.IsEnabled = true;
            
            // Reset selected printer
            ShowPrinterDetails(null);
            
            // Start discovery - use local discovery for faster results
            _printerDiscoveryService.StartLocalDiscovery();
            
            UpdateStatusBar("mahalliy printerlar qidirilmoqda...");
        }
        
        private void btnStopScan_Click(object sender, RoutedEventArgs e)
        {
            _printerDiscoveryService.StopDiscovery();
            btnStopScan.IsEnabled = false;
            UpdateStatusBar("printer qidirish jarayoni to'xtatildi");
        }
        
        private void btnAddManualPrinter_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("btnaddmanualprinter_click chaqirildi");
            
            // Create dialog window
            Window dialog = new Window
            {
                Title = "printer qo'lda qo'shish",
                Width = 600,
                Height = 450,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.CanResize,
                WindowStyle = WindowStyle.SingleBorderWindow
            };
            
            // Create layout
            Grid grid = new Grid { Margin = new Thickness(30) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            
            // Title
            TextBlock titleText = new TextBlock
            {
                Text = "ethernet orqali ulangan printer qo'shish",
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 30),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(titleText, 0);
            grid.Children.Add(titleText);
            
            // IP Address Label
            TextBlock ipLabel = new TextBlock
            {
                Text = "ip manzil:",
                FontSize = 18,
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetRow(ipLabel, 1);
            grid.Children.Add(ipLabel);
            
            // IP Address Input
            TextBox ipInput = new TextBox
            {
                FontSize = 18,
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 20),
                Height = 40
            };
            Grid.SetRow(ipInput, 2);
            grid.Children.Add(ipInput);
            
            // Port Label
            TextBlock portLabel = new TextBlock
            {
                Text = "port raqami:",
                FontSize = 18,
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetRow(portLabel, 3);
            grid.Children.Add(portLabel);
            
            // Port Input
            TextBox portInput = new TextBox
            {
                FontSize = 18,
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 20),
                Text = "9100", // Default port for most printers
                Height = 40
            };
            Grid.SetRow(portInput, 4);
            grid.Children.Add(portInput);
            
            // Model Label
            TextBlock modelLabel = new TextBlock
            {
                Text = "printer modeli:",
                FontSize = 18,
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetRow(modelLabel, 5);
            grid.Children.Add(modelLabel);
            
            // Model Input
            TextBox modelInput = new TextBox
            {
                FontSize = 18,
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 25),
                Text = "Termal printer (ESC/POS)", // Default model
                Height = 40
            };
            Grid.SetRow(modelInput, 6);
            grid.Children.Add(modelInput);
            
            // Buttons
            StackPanel buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 15, 0, 0)
            };
            Grid.SetRow(buttonPanel, 7);
            
            Button cancelButton = new Button
            {
                Content = "bekor qilish",
                Padding = new Thickness(25, 12, 25, 12),
                Margin = new Thickness(0, 0, 15, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0E0E0")),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333333")),
                FontSize = 16
            };
            cancelButton.Click += (s, args) => dialog.Close();
            
            Button addButton = new Button
            {
                Content = "qo'shish",
                Padding = new Thickness(30, 12, 30, 12),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50")),
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 16
            };
            addButton.Click += (s, args) =>
            {
                string ip = ipInput.Text.Trim();
                string portText = portInput.Text.Trim();
                string model = modelInput.Text.Trim();
                
                // Validate inputs
                if (string.IsNullOrEmpty(ip))
                {
                    MessageBox.Show("ip manzilni kiriting", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                
                if (string.IsNullOrEmpty(portText) || !int.TryParse(portText, out int port))
                {
                    MessageBox.Show("port raqamini to'g'ri kiriting", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                
                // Create printer info
                var printerInfo = new Restaurants.Services.PrinterInfo
                {
                    Name = $"RAW:{ip}:{port}",
                    ConnectionType = "Ethernet",
                    IpAddress = ip,
                    Status = "Mavjud - Ulanmagan",
                    Description = "Qo'lda qo'shilgan printer",
                    Location = "Tarmoq",
                    IsNetworkPrinter = true,
                    Model = model,
                    Port = port
                };
                
                // Add to list
                lvPrinters.Items.Add(printerInfo);
                
                // Hide no printers panel if this is the first printer
                if (lvPrinters.Items.Count == 1)
                {
                    noPrintersPanel.Visibility = Visibility.Collapsed;
                }
                
                // Update status
                UpdateStatusBar($"printer qo'shildi: {printerInfo.Name}");
                
                // Close dialog
                dialog.Close();
            };
            
            buttonPanel.Children.Add(cancelButton);
            buttonPanel.Children.Add(addButton);
            grid.Children.Add(buttonPanel);
            
            // Error message
            TextBlock errorText = new TextBlock
            {
                Foreground = new SolidColorBrush(Colors.Red),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0),
                Visibility = Visibility.Collapsed
            };
            Grid.SetRow(errorText, 8);
            grid.Children.Add(errorText);
            
            dialog.Content = grid;
            dialog.ShowDialog();
        }
        
        private void lvPrinters_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lvPrinters.SelectedItem != null)
            {
                var selectedPrinter = (Restaurants.Services.PrinterInfo)lvPrinters.SelectedItem;
                _selectedPrinter = selectedPrinter;
                
                // Update UI
                tbPrinterName.Text = selectedPrinter.Name;
                tbStatus.Text = selectedPrinter.Status;
                tbConnectionType.Text = selectedPrinter.ConnectionType;
                tbIpAddress.Text = selectedPrinter.IpAddress ?? "-";
                tbPort.Text = selectedPrinter.Port > 0 ? selectedPrinter.Port.ToString() : "-";
                tbModel.Text = selectedPrinter.Model ?? "-";
                tbDriverName.Text = selectedPrinter.DriverName ?? "-";
                tbLocation.Text = selectedPrinter.Location ?? "-";
                
                // Show printer details panel
                printerDetailsPanel.Visibility = Visibility.Visible;
                noPrinterSelectedPanel.Visibility = Visibility.Collapsed;
                
                // Update button states
                btnSetDefault.IsEnabled = !selectedPrinter.IsDefault;
                btnSetDefault.Content = selectedPrinter.IsDefault ? "asosiy printer" : "asosiy printer qilib belgilash";
                btnTest.IsEnabled = true;
                
                // Enable category mapping and add to saved buttons
                btnMapCategories.IsEnabled = true;
                btnAddToSaved.IsEnabled = true;
            }
            else
            {
                // No printer selected, hide details
                printerDetailsPanel.Visibility = Visibility.Collapsed;
                noPrinterSelectedPanel.Visibility = Visibility.Visible;
                _selectedPrinter = null;
                
                // Disable buttons
                btnTest.IsEnabled = false;
                btnSetDefault.IsEnabled = false;
                btnMapCategories.IsEnabled = false;
                btnAddToSaved.IsEnabled = false;
            }
        }
        
        private void btnTest_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPrinter == null)
            {
                MessageBox.Show("Test uchun printer tanlang", "Printer tanlanmagan", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            try
            {
                // Check what type of test to run based on printer type
                if (_selectedPrinter.IsNetworkPrinter && !string.IsNullOrEmpty(_selectedPrinter.IpAddress))
                {
                    TestNetworkPrinter(_selectedPrinter);
                }
                else
                {
                    TestLocalPrinter(_selectedPrinter);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Printer testida xatolik: {ex.Message}", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatusBar("printer testida xatolik yuz berdi");
            }
        }
        
        private void TestNetworkPrinter(Restaurants.Services.PrinterInfo printer)
        {
            UpdateStatusBar($"tarmoq printerini test qilish: {printer.Name}");
            
            try
            {
                // Create a test printer instance
                var testPrinter = new XPrinter(printer.IpAddress, printer.Port > 0 ? printer.Port : 9100);
                
                // Print a test page
                var testText = new StringBuilder();
                testText.AppendLine("=== PRINTER TEST ===");
                testText.AppendLine($"Printer: {printer.Name}");
                testText.AppendLine($"IP: {printer.IpAddress}");
                testText.AppendLine($"Port: {printer.Port}");
                testText.AppendLine($"Time: {DateTime.Now}");
                testText.AppendLine("===================");
                testText.AppendLine("АБВГДЕЁЖЗИЙКЛМНОП");
                testText.AppendLine("РСТУФХЦЧШЩЪЫЬЭЮЯ");
                testText.AppendLine("абвгдеёжзийклмноп");
                testText.AppendLine("рстуфхцчшщъыьэюя");
                testText.AppendLine("1234567890");
                testText.AppendLine("===================");
                
                // Print the test text directly instead of using PrintCyrillicTestAsync
                _ = testPrinter.ExecutePrintAsync(testText.ToString(), "test sahifasi yuborildi");
                
                MessageBox.Show($"Test sahifasi {printer.Name} printeriga yuborildi", 
                    "Test yuborildi", MessageBoxButton.OK, MessageBoxImage.Information);
                
                UpdateStatusBar($"test sahifasi yuborildi: {printer.Name}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Tarmoq printerini testida xatolik: {ex.Message}", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatusBar($"tarmoq printerini testida xatolik: {ex.Message}");
            }
        }
        
        private void TestLocalPrinter(Restaurants.Services.PrinterInfo printer)
        {
            UpdateStatusBar($"lokal printerini test qilish: {printer.Name}");
            
            try
            {
                System.Diagnostics.Debug.WriteLine($"lokal printer testi: {printer.Name}");
                
                // Create a local XPrinter instance
                var testPrinter = new XPrinter();
                
                // Set the printer name
                testPrinter.SetLocalPrinterName(printer.Name);
                
                // Create test text
                var testText = new StringBuilder();
                testText.AppendLine("=== PRINTER TEST ===");
                testText.AppendLine($"Printer: {printer.Name}");
                testText.AppendLine($"Time: {DateTime.Now}");
                testText.AppendLine("===================");
                testText.AppendLine("АБВГДЕЁЖЗИЙКЛМНОП");
                testText.AppendLine("РСТУФХЦЧШЩЪЫЬЭЮЯ");
                testText.AppendLine("абвгдеёжзийклмноп");
                testText.AppendLine("рстуфхцчшщъыьэюя");
                testText.AppendLine("1234567890");
                testText.AppendLine("===================");
                
                // Print asynchronously
                _ = testPrinter.ExecutePrintAsync(testText.ToString(), "test sahifasi yuborildi");
                
                MessageBox.Show($"Test sahifasi {printer.Name} printeriga yuborildi", 
                    "Test yuborildi", MessageBoxButton.OK, MessageBoxImage.Information);
                
                UpdateStatusBar($"test sahifasi yuborildi: {printer.Name}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"lokal printer testida xatolik: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"stack trace: {ex.StackTrace}");
                MessageBox.Show($"Lokal printerini testida xatolik: {ex.Message}", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatusBar($"lokal printerini testida xatolik: {ex.Message}");
            }
        }
        
        private void btnSetDefault_Click(object sender, RoutedEventArgs e)
        {
            SetDefaultPrinter();
        }
        
        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
        
        /// <summary>
        /// Maps the selected printer to product categories
        /// </summary>
        private async void btnMapCategories_Click(object sender, RoutedEventArgs e)
        {
            if (lvPrinters.SelectedItem == null)
            {
                MessageBox.Show("Iltimos, avval printerni tanlang.", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            
            var selectedPrinter = (Restaurants.Services.PrinterInfo)lvPrinters.SelectedItem;
            
            try
            {
                // Show loading indicator
                UpdateStatusBar("Kategoriyalar yuklanmoqda...");
                loadingGrid.Visibility = Visibility.Visible;
                
                // Get categories from API
                var categories = await _productCategoryService.GetProductCategoriesAsync();
                
                if (categories == null || categories.Count == 0)
                {
                    MessageBox.Show("Kategoriyalar yuklanmadi. Internet aloqasini tekshiring.", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
                    loadingGrid.Visibility = Visibility.Collapsed;
                    UpdateStatusBar("Kategoriyalar yuklanmadi.");
                    return;
                }
                
                // Get current mappings for this printer
                var currentMappings = await _productCategoryService.GetCategoriesForPrinterAsync(selectedPrinter.Name);
                
                // Create category mapping window
                var categoryWindow = new Window
                {
                    Title = "Printerni kategoriyalarga biriktirish",
                    Width = 500,
                    Height = 600,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    ResizeMode = ResizeMode.NoResize
                };
                
                var mainGrid = new Grid();
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                
                // Header
                var headerText = new TextBlock
                {
                    Text = $"{selectedPrinter.Name} printerini kategoriyalarga biriktirish",
                    Margin = new Thickness(10),
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold
                };
                Grid.SetRow(headerText, 0);
                mainGrid.Children.Add(headerText);
                
                // Categories list with checkboxes
                var categoriesPanel = new StackPanel
                {
                    Margin = new Thickness(10)
                };
                
                var scrollViewer = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Margin = new Thickness(10)
                };
                scrollViewer.Content = categoriesPanel;
                Grid.SetRow(scrollViewer, 1);
                mainGrid.Children.Add(scrollViewer);
                
                // Create checkbox for each category
                Dictionary<int, CheckBox> categoryCheckboxes = new Dictionary<int, CheckBox>();
                
                foreach (var category in categories)
                {
                    var checkbox = new CheckBox
                    {
                        Content = $"{category.ShortName} ({category.FullName})",
                        Margin = new Thickness(0, 5, 0, 5),
                        Tag = category.Id,
                        IsChecked = currentMappings?.Any(m => m.CategoryId == category.Id) == true
                    };
                    
                    categoryCheckboxes.Add(category.Id, checkbox);
                    categoriesPanel.Children.Add(checkbox);
                }
                
                // Buttons panel
                var buttonsPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(10)
                };
                Grid.SetRow(buttonsPanel, 2);
                
                var saveButton = new Button
                {
                    Content = "Saqlash",
                    Padding = new Thickness(20, 10, 20, 10),
                    Margin = new Thickness(0, 0, 10, 0),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50")),
                    Foreground = Brushes.White
                };
                
                var cancelButton = new Button
                {
                    Content = "Bekor qilish",
                    Padding = new Thickness(20, 10, 20, 10),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#757575")),
                    Foreground = Brushes.White
                };
                
                buttonsPanel.Children.Add(saveButton);
                buttonsPanel.Children.Add(cancelButton);
                mainGrid.Children.Add(buttonsPanel);
                
                categoryWindow.Content = mainGrid;
                
                // Button event handlers
                saveButton.Click += async (s, args) =>
                {
                    try
                    {
                        // Create list of mappings
                        List<PrinterCategory> mappings = new List<PrinterCategory>();
                        
                        foreach (var entry in categoryCheckboxes)
                        {
                            if (entry.Value.IsChecked == true)
                            {
                                var category = categories.FirstOrDefault(c => c.Id == entry.Key);
                                if (category != null)
                                {
                                    mappings.Add(new PrinterCategory
                                    {
                                        PrinterId = selectedPrinter.Name,
                                        PrinterName = selectedPrinter.Name,
                                        CategoryId = category.Id,
                                        CategoryName = category.ShortName
                                    });
                                }
                            }
                        }
                        
                        // Save mappings
                        await _productCategoryService.SavePrinterCategoryMappingsAsync(selectedPrinter.Name, mappings);
                        
                        MessageBox.Show("Printer kategoriyalarga muvaffaqiyatli biriktirildi.", "Muvaffaqiyatli", MessageBoxButton.OK, MessageBoxImage.Information);
                        categoryWindow.Close();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Xatolik yuz berdi: {ex.Message}", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };
                
                cancelButton.Click += (s, args) =>
                {
                    categoryWindow.Close();
                };
                
                // Hide loading indicator
                loadingGrid.Visibility = Visibility.Collapsed;
                UpdateStatusBar("Kategoriyalarni tanlang va saqlang.");
                
                // Show dialog
                categoryWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                loadingGrid.Visibility = Visibility.Collapsed;
                MessageBox.Show($"Xatolik yuz berdi: {ex.Message}", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatusBar("Xatolik yuz berdi.");
            }
        }
        
        /// <summary>
        /// Adds the selected printer to the saved printers list
        /// </summary>
        private void btnAddPrinter_Click(object sender, RoutedEventArgs e)
        {
            if (lvPrinters.SelectedItem != null)
            {
                var selectedPrinter = (Restaurants.Services.PrinterInfo)lvPrinters.SelectedItem;
                
                // Check if this printer is already saved
                bool alreadySaved = false;
                
                // We'll use the Name property to check if it's already saved
                string json = "";
                List<Restaurants.Services.PrinterInfo> savedPrinters = new List<Restaurants.Services.PrinterInfo>();
                
                if (File.Exists(PRINTERS_FILE_PATH))
                {
                    try
                    {
                        json = File.ReadAllText(PRINTERS_FILE_PATH);
                        savedPrinters = System.Text.Json.JsonSerializer.Deserialize<List<Restaurants.Services.PrinterInfo>>(json);
                        
                        if (savedPrinters != null)
                        {
                            foreach (var printer in savedPrinters)
                            {
                                if (printer.Name == selectedPrinter.Name)
                                {
                                    alreadySaved = true;
                                    break;
                                }
                            }
                        }
                        else
                        {
                            savedPrinters = new List<Restaurants.Services.PrinterInfo>();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"saqlangan printerlarni yuklashda xatolik: {ex.Message}");
                        savedPrinters = new List<Restaurants.Services.PrinterInfo>();
                    }
                }
                
                if (!alreadySaved)
                {
                    // Add the printer to the saved list and save it
                    savedPrinters.Add(selectedPrinter);
                    
                    try
                    {
                        var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                        json = System.Text.Json.JsonSerializer.Serialize(savedPrinters, options);
                        File.WriteAllText(PRINTERS_FILE_PATH, json);
                        
                        UpdateStatusBar($"printer saqlandi: {selectedPrinter.Name}");
                        MessageBox.Show($"Printer '{selectedPrinter.Name}' saqlangan printerlar ro'yxatiga qo'shildi.", 
                            "Muvaffaqiyatli", MessageBoxButton.OK, MessageBoxImage.Information);
                        
                        // Update UI to show this printer is now saved
                        UpdateDefaultPrinterIndicator();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"printerni saqlashda xatolik: {ex.Message}");
                        MessageBox.Show($"Printerni saqlashda xatolik yuz berdi: {ex.Message}", 
                            "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    UpdateStatusBar($"printer allaqachon saqlangan: {selectedPrinter.Name}");
                    MessageBox.Show($"Printer '{selectedPrinter.Name}' allaqachon saqlangan printerlar ro'yxatida mavjud.", 
                        "Ma'lumot", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                UpdateStatusBar("printer tanlanmagan");
                MessageBox.Show("Iltimos, avval printerni tanlang.", 
                    "Ogohlantirish", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        
        #endregion

        #region Printer Persistence
        
        private void LoadSavedPrinters()
        {
            try
            {
                _isLoadingPrinters = true;
                
                if (File.Exists(PRINTERS_FILE_PATH))
                {
                    string json = File.ReadAllText(PRINTERS_FILE_PATH);
                    var printers = System.Text.Json.JsonSerializer.Deserialize<List<Restaurants.Services.PrinterInfo>>(json);
                    
                    if (printers != null && printers.Count > 0)
                    {
                        foreach (var printer in printers)
                        {
                            lvPrinters.Items.Add(printer);
                        }
                        
                        noPrintersPanel.Visibility = Visibility.Collapsed;
                        UpdateStatusBar($"{printers.Count} ta saqlangan printer yuklandi");
                        
                        // Update the UI for default printer
                        UpdateDefaultPrinterVisual();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"printerlarni yuklashda xatolik: {ex.Message}");
            }
            finally
            {
                _isLoadingPrinters = false;
            }
        }
        
        private void SavePrinters()
        {
            try
            {
                var printers = new List<Restaurants.Services.PrinterInfo>();
                
                foreach (Restaurants.Services.PrinterInfo printer in lvPrinters.Items)
                {
                    printers.Add(printer);
                }
                
                var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                string json = System.Text.Json.JsonSerializer.Serialize(printers, options);
                File.WriteAllText(PRINTERS_FILE_PATH, json);
                
                System.Diagnostics.Debug.WriteLine($"{printers.Count} ta printer saqlandi");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"printerlarni saqlashda xatolik: {ex.Message}");
            }
        }
        
        private void RemovePrinter(Restaurants.Services.PrinterInfo printer)
        {
            lvPrinters.Items.Remove(printer);
            
            if (lvPrinters.Items.Count == 0)
            {
                noPrintersPanel.Visibility = Visibility.Visible;
            }
            
            SavePrinters();
            UpdateStatusBar($"printer o'chirildi: {printer.Name}");
        }
        
        #endregion

        #region Order Printing

        /// <summary>
        /// Gets the default printer from the list
        /// </summary>
        /// <returns>The default printer or null if no default printer is set</returns>
        public Restaurants.Services.PrinterInfo GetDefaultPrinter()
        {
            foreach (Restaurants.Services.PrinterInfo printer in lvPrinters.Items)
            {
                if (printer.IsDefault)
                {
                    System.Diagnostics.Debug.WriteLine($"default printer topildi: {printer.Name}");
                    return printer;
                }
            }
            
            System.Diagnostics.Debug.WriteLine("default printer topilmadi");
            return null;
        }

        /// <summary>
        /// Prints an order using the specified printer or the default printer if not specified
        /// </summary>
        public async Task<bool> PrintOrderAsync(PrintOrder order, Restaurants.Services.PrinterInfo specificPrinter = null)
        {
            try
            {
                if (order == null)
                {
                    System.Diagnostics.Debug.WriteLine("buyurtma null");
                    MessageBox.Show("Buyurtma ma'lumotlari mavjud emas", 
                        "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
                
                if (order.Orders == null || !order.Orders.Any())
                {
                    System.Diagnostics.Debug.WriteLine("buyurtma elementlari mavjud emas");
                    MessageBox.Show("Buyurtma elementlari mavjud emas", 
                        "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
                
                // Use the specified printer if provided, otherwise use the default printer
                XPrinter printer = null;
                
                try
                {
                    if (specificPrinter != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"ko'rsatilgan printer ishlatilmoqda: {specificPrinter.Name}");
                        printer = CreatePrinter(specificPrinter);
                    }
                    else
                    {
                        // Find the default printer
                        var defaultPrinter = GetDefaultPrinter();
                        
                        if (defaultPrinter == null)
                        {
                            System.Diagnostics.Debug.WriteLine("default printer topilmadi");
                            MessageBox.Show("Default printer topilmadi. Iltimos, avval default printer tanlang.", 
                                "Printer xatoligi", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return false;
                        }
                        
                        System.Diagnostics.Debug.WriteLine($"default printer ishlatilmoqda: {defaultPrinter.Name}");
                        printer = CreatePrinter(defaultPrinter);
                    }
                    
                    if (printer == null)
                    {
                        System.Diagnostics.Debug.WriteLine("printer yaratib bo'lmadi");
                        MessageBox.Show("Printer yaratib bo'lmadi. Iltimos, printer sozlamalarini tekshiring.", 
                            "Printer xatoligi", MessageBoxButton.OK, MessageBoxImage.Error);
                        return false;
                    }
                    
                    // Print the order
                    await printer.PrintAsync(order);
                    
                    System.Diagnostics.Debug.WriteLine("buyurtma muvaffaqiyatli chop etildi");
                    return true;
                }
                finally
                {
                    // Dispose the printer if it was created
                    printer?.Dispose();
                }
            }
            catch (SocketException ex)
            {
                System.Diagnostics.Debug.WriteLine($"tarmoq xatoligi: {ex.Message}");
                MessageBox.Show($"Printer bilan bog'lanishda xatolik yuz berdi: {ex.Message}", 
                    "Tarmoq xatoligi", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"buyurtmani chop etishda xatolik: {ex.Message}");
                MessageBox.Show($"Buyurtmani chop etishda xatolik yuz berdi: {ex.Message}", 
                    "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// Prints a kitchen order using the specified printer or the default printer if not specified
        /// </summary>
        public async Task<bool> PrintKitchenOrderAsync(ContractorOrder order, Restaurants.Services.PrinterInfo specificPrinter = null)
        {
            try
            {
                if (order == null)
                {
                    System.Diagnostics.Debug.WriteLine("oshxona buyurtmasi null");
                    MessageBox.Show("Oshxona buyurtmasi ma'lumotlari mavjud emas", 
                        "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
                
                if (order.Tables == null || !order.Tables.Any())
                {
                    System.Diagnostics.Debug.WriteLine("oshxona buyurtmasi elementlari mavjud emas");
                    MessageBox.Show("Oshxona buyurtmasi elementlari mavjud emas", 
                        "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
                
                // Use the specified printer if provided, otherwise use the default printer
                XPrinter printer = null;
                
                try
                {
                    if (specificPrinter != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"ko'rsatilgan printer ishlatilmoqda: {specificPrinter.Name}");
                        printer = CreatePrinter(specificPrinter);
                    }
                    else
                    {
                        // Find the default printer
                        var defaultPrinter = GetDefaultPrinter();
                        
                        if (defaultPrinter == null)
                        {
                            System.Diagnostics.Debug.WriteLine("default printer topilmadi");
                            MessageBox.Show("Default printer topilmadi. Iltimos, avval default printer tanlang.", 
                                "Printer xatoligi", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return false;
                        }
                        
                        System.Diagnostics.Debug.WriteLine($"default printer ishlatilmoqda: {defaultPrinter.Name}");
                        printer = CreatePrinter(defaultPrinter);
                    }
                    
                    if (printer == null)
                    {
                        System.Diagnostics.Debug.WriteLine("printer yaratib bo'lmadi");
                        MessageBox.Show("Printer yaratib bo'lmadi. Iltimos, printer sozlamalarini tekshiring.", 
                            "Printer xatoligi", MessageBoxButton.OK, MessageBoxImage.Error);
                        return false;
                    }
                    
                    // Print the kitchen order
                    await printer.PrintKitchenOrderAsync(order);
                    
                    System.Diagnostics.Debug.WriteLine("oshxona buyurtmasi muvaffaqiyatli chop etildi");
                    return true;
                }
                finally
                {
                    // Dispose the printer if it was created
                    printer?.Dispose();
                }
            }
            catch (SocketException ex)
            {
                System.Diagnostics.Debug.WriteLine($"tarmoq xatoligi: {ex.Message}");
                MessageBox.Show($"Printer bilan bog'lanishda xatolik yuz berdi: {ex.Message}", 
                    "Tarmoq xatoligi", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"oshxona buyurtmasini chop etishda xatolik: {ex.Message}");
                MessageBox.Show($"Oshxona buyurtmasini chop etishda xatolik yuz berdi: {ex.Message}", 
                    "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        #endregion

        #region IDisposable Implementation

        private bool _disposed = false;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed resources
                    _connection?.DisposeAsync();
                    _printerDiscoveryService?.Dispose();
                    _printer?.Dispose();
                }

                // Free unmanaged resources
                _disposed = true;
            }
        }

        #endregion

        /// <summary>
        /// Updates the visual representation of the default printer in the UI
        /// </summary>
        private void UpdateDefaultPrinterVisual()
        {
            try
            {
                // Update all items to normal style first
                foreach (Restaurants.Services.PrinterInfo printer in lvPrinters.Items)
                {
                    var listViewItem = (ListViewItem)lvPrinters.ItemContainerGenerator.ContainerFromItem(printer);
                    if (listViewItem != null)
                    {
                        listViewItem.FontWeight = FontWeights.Normal;
                        listViewItem.Background = Brushes.Transparent;
                    }
                }
                
                // Find and highlight the default printer
                foreach (Restaurants.Services.PrinterInfo printer in lvPrinters.Items)
                {
                    if (printer.IsDefault)
                    {
                        var listViewItem = (ListViewItem)lvPrinters.ItemContainerGenerator.ContainerFromItem(printer);
                        if (listViewItem != null)
                        {
                            listViewItem.FontWeight = FontWeights.Bold;
                            listViewItem.Background = new SolidColorBrush(Color.FromRgb(232, 245, 233)); // Light green background
                            
                            // Update the button text to show this is default
                            btnSetDefault.Content = "asosiy printer (tanlangan)";
                            btnSetDefault.IsEnabled = false;
                        }
                        
                        break; // Only one default printer
                    }
                }
                
                // If no default printer is set, update the button text
                bool hasDefault = lvPrinters.Items.Cast<Restaurants.Services.PrinterInfo>().Any(p => p.IsDefault);
                if (!hasDefault && lvPrinters.SelectedItem != null)
                {
                    btnSetDefault.Content = "asosiy printer qilib belgilash";
                    btnSetDefault.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"updatedefaultprintervisual xatolik: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates a printer instance based on the printer information
        /// </summary>
        /// <param name="printerInfo">The printer information</param>
        /// <returns>An XPrinter instance or null if creation failed</returns>
        private XPrinter CreatePrinter(Restaurants.Services.PrinterInfo printerInfo)
        {
            if (printerInfo == null)
            {
                System.Diagnostics.Debug.WriteLine("printer ma'lumotlari null");
                return null;
            }
            
            try
            {
                XPrinter printer;
                
                if (printerInfo.IsNetworkPrinter && !string.IsNullOrEmpty(printerInfo.IpAddress))
                {
                    // Create network printer instance
                    System.Diagnostics.Debug.WriteLine($"tarmoq printeriga bog'lanish: {printerInfo.IpAddress}:{printerInfo.Port}");
                    printer = new XPrinter(printerInfo.IpAddress, printerInfo.Port > 0 ? printerInfo.Port : 9100);
                    
                    // Set timeout for network printer (5 seconds)
                    printer.SetConnectionTimeout(5000);
                }
                else
                {
                    // For local printers
                    System.Diagnostics.Debug.WriteLine($"lokal printer uchun: {printerInfo.Name}");
                    
                    // Create a local XPrinter instance
                    printer = new XPrinter();
                    
                    // Set the printer name for the XPrinter instance
                    printer.SetLocalPrinterName(printerInfo.Name);
                }
                
                return printer;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"printer yaratishda xatolik: {ex.Message}");
                return null;
            }
        }

        private void SetDefaultPrinter()
        {
            if (lvPrinters.SelectedItem != null)
            {
                // Clear default flag for all printers
                foreach (Restaurants.Services.PrinterInfo printer in lvPrinters.Items)
                {
                    printer.IsDefault = false;
                }
                
                // Set selected printer as default
                var selectedPrinter = (Restaurants.Services.PrinterInfo)lvPrinters.SelectedItem;
                selectedPrinter.IsDefault = true;
                
                MessageBox.Show($"{selectedPrinter.Name} printer asosiy printer sifatida o'rnatildi",
                    "Asosiy printer", MessageBoxButton.OK, MessageBoxImage.Information);
                
                // Save the changes
                SavePrinters();
                
                // Update UI to show default printer
                UpdateDefaultPrinterVisual();
                
                // Update status bar
                UpdateStatusBar($"{selectedPrinter.Name} printer default sifatida o'rnatildi");
            }
        }

        /// <summary>
        /// Updates the UI to indicate which printer is the default
        /// </summary>
        private void UpdateDefaultPrinterIndicator()
        {
            try
            {
                // Load saved printers
                if (File.Exists(PRINTERS_FILE_PATH))
                {
                    string json = File.ReadAllText(PRINTERS_FILE_PATH);
                    var savedPrinters = System.Text.Json.JsonSerializer.Deserialize<List<Restaurants.Services.PrinterInfo>>(json);
                    
                    if (savedPrinters != null)
                    {
                        // Load default printer
                        string defaultPrinterJson = "";
                        Restaurants.Services.PrinterInfo defaultPrinter = null;
                        
                        if (File.Exists("default_printer.json"))
                        {
                            defaultPrinterJson = File.ReadAllText("default_printer.json");
                            defaultPrinter = System.Text.Json.JsonSerializer.Deserialize<Restaurants.Services.PrinterInfo>(defaultPrinterJson);
                        }
                        
                        // Update UI for each printer in the list
                        foreach (Restaurants.Services.PrinterInfo printer in lvPrinters.Items)
                        {
                            var item = lvPrinters.ItemContainerGenerator.ContainerFromItem(printer) as ListViewItem;
                            if (item != null)
                            {
                                bool isSaved = savedPrinters.Any(p => p.Name == printer.Name);
                                bool isDefault = defaultPrinter != null && defaultPrinter.Name == printer.Name;
                                
                                // Update background color based on status
                                if (isDefault)
                                {
                                    item.Background = new SolidColorBrush(Color.FromRgb(220, 255, 220)); // Light green for default
                                }
                                else if (isSaved)
                                {
                                    item.Background = new SolidColorBrush(Color.FromRgb(240, 240, 255)); // Light blue for saved
                                }
                                else
                                {
                                    item.Background = new SolidColorBrush(Colors.Transparent);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateDefaultPrinterIndicator xatolik: {ex.Message}");
            }
        }
    }
}

