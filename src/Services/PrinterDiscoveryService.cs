using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Management;
using System.IO;
using System.Windows;
using System.Drawing.Printing;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Restaurants.Services
{
    public class PrinterDiscoveryService : IDisposable
    {
        private readonly BackgroundWorker _networkScanWorker;
        private readonly List<PrinterInfo> _discoveredPrinters = new List<PrinterInfo>();
        private bool _disposed = false;
        
        public event EventHandler<PrinterDiscoveredEventArgs> PrinterDiscovered;
        public event EventHandler<PrinterScanCompletedEventArgs> ScanCompleted;
        
        public PrinterDiscoveryService()
        {
            _networkScanWorker = new BackgroundWorker
            {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };
            
            _networkScanWorker.DoWork += NetworkScanWorker_DoWork;
            _networkScanWorker.ProgressChanged += NetworkScanWorker_ProgressChanged;
            _networkScanWorker.RunWorkerCompleted += NetworkScanWorker_RunWorkerCompleted;
            
            System.Diagnostics.Debug.WriteLine("printerdiscoveryservice yaratildi");
        }
        
        /// <summary>
        /// Start discovering printers on both USB and network
        /// </summary>
        public void StartDiscovery()
        {
            System.Diagnostics.Debug.WriteLine("startdiscovery boshlanmoqda");
            _discoveredPrinters.Clear();
            
            // Discover USB printers synchronously
            DiscoverUsbPrinters();
            
            // Start network scan asynchronously
            if (!_networkScanWorker.IsBusy)
            {
                System.Diagnostics.Debug.WriteLine("tarmoq skanerlash boshlanmoqda");
                _networkScanWorker.RunWorkerAsync();
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("tarmoq skanerlash allaqachon ishlamoqda");
            }
        }
        
        /// <summary>
        /// Start discovering only local printers (faster)
        /// </summary>
        public void StartLocalDiscovery()
        {
            System.Diagnostics.Debug.WriteLine("startlocaldiscovery boshlanmoqda");
            _discoveredPrinters.Clear();
            
            // Discover only USB printers synchronously
            DiscoverUsbPrinters();
            
            // Complete the scan immediately
            System.Diagnostics.Debug.WriteLine($"mahalliy printer qidirish tugallandi, jami printerlar: {_discoveredPrinters.Count}");
            OnScanCompleted(false);
        }
        
        /// <summary>
        /// Stop the printer discovery process
        /// </summary>
        public void StopDiscovery()
        {
            System.Diagnostics.Debug.WriteLine("stopdiscovery chaqirildi");
            if (_networkScanWorker.IsBusy)
            {
                _networkScanWorker.CancelAsync();
                System.Diagnostics.Debug.WriteLine("tarmoq skanerlash to'xtatildi");
            }
        }
        
        /// <summary>
        /// Get list of all discovered printers
        /// </summary>
        public List<PrinterInfo> GetDiscoveredPrinters()
        {
            System.Diagnostics.Debug.WriteLine($"getdiscoveredprinters chaqirildi: {_discoveredPrinters.Count} printerlar");
            return _discoveredPrinters;
        }
        
        /// <summary>
        /// Discover locally connected USB printers
        /// </summary>
        private void DiscoverUsbPrinters()
        {
            System.Diagnostics.Debug.WriteLine("discoverusbprinters boshlanmoqda");
            try
            {
                // Get all printers using System.Drawing.Printing - this is faster than WMI
                foreach (string printerName in PrinterSettings.InstalledPrinters)
                {
                    System.Diagnostics.Debug.WriteLine($"printer topildi: {printerName}");
                    PrinterSettings settings = new PrinterSettings { PrinterName = printerName };
                    
                    var printerInfo = new PrinterInfo
                    {
                        Name = printerName,
                        IsDefault = settings.IsDefaultPrinter,
                        ConnectionType = DetermineConnectionType(printerName),
                        Status = settings.IsValid ? "Tayyor" : "Mavjud emas",
                        DriverName = GetPrinterDriverName(printerName),
                        Description = GetPrinterDescription(printerName),
                        Location = "Lokal qurilma",
                        IsNetworkPrinter = printerName.StartsWith("\\\\"),
                        Model = GetPrinterModel(printerName)
                    };
                    
                    System.Diagnostics.Debug.WriteLine($"printer ma'lumotlari: {printerInfo.Name}, {printerInfo.ConnectionType}, {printerInfo.Status}");
                    _discoveredPrinters.Add(printerInfo);
                    OnPrinterDiscovered(printerInfo);
                }
                
                // Skip the WMI query to make discovery faster
                // The PrinterSettings.InstalledPrinters already gives us all the printers we need
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"usb printerlarni aniqlashda xatolik: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"stack trace: {ex.StackTrace}");
            }
        }

        private void NetworkScanWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("networkscanworker_dowork boshlanmoqda");
            var worker = sender as BackgroundWorker;
            
            try
            {
                // Scan network printers using WMI
                ScanWmiNetworkPrinters(worker);
                
                if (worker.CancellationPending)
                {
                    e.Cancel = true;
                    return;
                }
                
                // Scan IP ranges for printers
                ScanNetworkIpRange(worker);
                
                // Try to discover through Windows Spooler
                ScanWindowsSpoolerNetworkPrinters(worker);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"tarmoq printerlarini aniqlashda xatolik: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"stack trace: {ex.StackTrace}");
            }
        }
        
        private void NetworkScanWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            if (e.UserState is PrinterInfo printer)
            {
                System.Diagnostics.Debug.WriteLine($"yangi printer topildi: {printer.Name}");
                _discoveredPrinters.Add(printer);
                OnPrinterDiscovered(printer);
            }
        }
        
        private void NetworkScanWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"tarmoq skanerlash tugallandi, jami printerlar: {_discoveredPrinters.Count}");
            OnScanCompleted(e.Cancelled);
        }
        
        private void ScanWmiNetworkPrinters(BackgroundWorker worker)
        {
            System.Diagnostics.Debug.WriteLine("scanwminetworkprinters boshlanmoqda");
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Printer WHERE Network=TRUE"))
                {
                    var printers = searcher.Get();
                    System.Diagnostics.Debug.WriteLine($"wmi orqali topilgan tarmoq printerlari soni: {printers.Count}");
                    
                    foreach (var printer in printers)
                    {
                        if (worker.CancellationPending) return;
                        
                        string printerName = printer["Name"]?.ToString();
                        if (string.IsNullOrEmpty(printerName)) continue;
                        
                        System.Diagnostics.Debug.WriteLine($"wmi orqali topilgan tarmoq printeri: {printerName}");
                        
                        // Skip if already added
                        if (_discoveredPrinters.Any(p => p.Name == printerName))
                        {
                            System.Diagnostics.Debug.WriteLine($"printer allaqachon qo'shilgan: {printerName}");
                            continue;
                        }
                            
                        var printerInfo = new PrinterInfo
                        {
                            Name = printerName,
                            IsDefault = (bool)printer["Default"],
                            ConnectionType = "Network",
                            Status = TranslatePrinterStatus((uint)printer["PrinterStatus"]),
                            DriverName = printer["DriverName"]?.ToString(),
                            Description = printer["Caption"]?.ToString(),
                            Location = printer["Location"]?.ToString() ?? "Tarmoq",
                            IsNetworkPrinter = true,
                            IpAddress = ExtractIpFromPrinterName(printerName),
                            Model = printer["PortName"]?.ToString()
                        };
                        
                        System.Diagnostics.Debug.WriteLine($"wmi tarmoq printer ma'lumotlari: {printerInfo.Name}, {printerInfo.IpAddress}");
                        worker.ReportProgress(0, printerInfo);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"wmi orqali tarmoq printerlarini aniqlashda xatolik: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"stack trace: {ex.StackTrace}");
            }
        }
        
        private void ScanWindowsSpoolerNetworkPrinters(BackgroundWorker worker)
        {
            System.Diagnostics.Debug.WriteLine("scanwindowsspoolernetworkprinters boshlanmoqda");
            try
            {
                // Already handled by PrinterSettings.InstalledPrinters in DiscoverUsbPrinters method
                System.Diagnostics.Debug.WriteLine("bu metod allaqachon discoverusbprinters metodida bajarilgan");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"windows spooler orqali tarmoq printerlarini aniqlashda xatolik: {ex.Message}");
            }
        }
        
        private void ScanNetworkIpRange(BackgroundWorker worker)
        {
            System.Diagnostics.Debug.WriteLine("scannetworkiprange boshlanmoqda");
            try
            {
                // Get local IP addresses and subnets
                var localIPs = GetLocalIPAddresses();
                System.Diagnostics.Debug.WriteLine($"lokal ip manzillar: {string.Join(", ", localIPs)}");
                
                foreach (var localIP in localIPs)
                {
                    if (worker.CancellationPending) return;
                    
                    // Determine IP range to scan (assuming /24 subnet)
                    string baseIP = GetBaseIP(localIP);
                    System.Diagnostics.Debug.WriteLine($"ip manzil bazasi: {baseIP}");
                    
                    // Scan range for printer ports (9100 - standard RAW print port)
                    for (int i = 1; i <= 254; i++)
                    {
                        if (worker.CancellationPending) return;
                        
                        string ip = $"{baseIP}.{i}";
                        
                        // Skip local IP
                        if (ip == localIP) continue;
                        
                        System.Diagnostics.Debug.WriteLine($"ip manzilni tekshirish: {ip}");
                        if (IsPortOpen(ip, 9100, 200))  // 200ms timeout
                        {
                            System.Diagnostics.Debug.WriteLine($"9100 porti ochiq: {ip}");
                            var printerInfo = new PrinterInfo
                            {
                                Name = $"RAW:{ip}:9100",
                                ConnectionType = "Ethernet",
                                IpAddress = ip,
                                Status = "Mavjud - Ulanmagan",
                                Description = "Tarmoqda topilgan termal printer",
                                Location = "Tarmoq",
                                IsNetworkPrinter = true,
                                Model = "Termal printer (ESC/POS)",
                                Port = 9100
                            };
                            
                            // Skip if already added
                            if (_discoveredPrinters.Any(p => p.IpAddress == ip))
                            {
                                System.Diagnostics.Debug.WriteLine($"printer allaqachon qo'shilgan: {ip}");
                                continue;
                            }
                                
                            System.Diagnostics.Debug.WriteLine($"yangi tarmoq printeri qo'shilmoqda: {ip}");
                            worker.ReportProgress(0, printerInfo);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ip manzilini tekshirishda xatolik: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"stack trace: {ex.StackTrace}");
            }
        }
        
        #region Helper Methods
        
        private string DetermineConnectionType(string printerName)
        {
            if (printerName.StartsWith("\\\\"))
                return "Network";
                
            try
            {
                using (var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_Printer WHERE Name='{printerName.Replace("\\", "\\\\")}'"))
                {
                    foreach (var printer in searcher.Get())
                    {
                        string portName = printer["PortName"]?.ToString() ?? "";
                        
                        if (portName.StartsWith("USB"))
                            return "USB";
                        else if (portName.Contains("COM"))
                            return "COM port";
                        else if (portName.Contains("LPT"))
                            return "LPT port";
                        else if (portName.Contains("IP_"))
                            return "Ethernet";
                        else if (portName.StartsWith("WSD"))
                            return "WSD";
                        else
                            return portName;
                    }
                }
            }
            catch
            {
                // Fallback
            }
            
            return "Unknown";
        }
        
        private string GetPrinterDriverName(string printerName)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_Printer WHERE Name='{printerName.Replace("\\", "\\\\")}'"))
                {
                    foreach (var printer in searcher.Get())
                    {
                        return printer["DriverName"]?.ToString() ?? "Unknown Driver";
                    }
                }
            }
            catch
            {
                // Fallback
            }
            
            return "Unknown Driver";
        }
        
        private string GetPrinterDescription(string printerName)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_Printer WHERE Name='{printerName.Replace("\\", "\\\\")}'"))
                {
                    foreach (var printer in searcher.Get())
                    {
                        return printer["Caption"]?.ToString() ?? printerName;
                    }
                }
            }
            catch
            {
                // Fallback
            }
            
            return printerName;
        }
        
        private string GetPrinterModel(string printerName)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_Printer WHERE Name='{printerName.Replace("\\", "\\\\")}'"))
                {
                    foreach (var printer in searcher.Get())
                    {
                        var model = printer["DriverName"]?.ToString() ?? "Unknown Model";
                        if (model.Contains(" "))
                        {
                            // Try to extract model name from driver
                            string[] parts = model.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2)
                                return string.Join(" ", parts.Skip(1));
                        }
                        return model;
                    }
                }
            }
            catch
            {
                // Fallback
            }
            
            return "Unknown Model";
        }
        
        private List<string> GetLocalIPAddresses()
        {
            List<string> result = new List<string>();
            
            try
            {
                // Get all network interfaces
                NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
                foreach (NetworkInterface adapter in adapters)
                {
                    // Skip loopback, non-operational, and non-Ethernet interfaces
                    if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        adapter.OperationalStatus != OperationalStatus.Up ||
                        adapter.NetworkInterfaceType != NetworkInterfaceType.Ethernet &&
                        adapter.NetworkInterfaceType != NetworkInterfaceType.Wireless80211)
                        continue;
                        
                    // Get IP properties
                    IPInterfaceProperties adapterProperties = adapter.GetIPProperties();
                    
                    // Get IPv4 addresses
                    foreach (UnicastIPAddressInformation ip in adapterProperties.UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            result.Add(ip.Address.ToString());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"lokal ip manzillarini olishda xatolik: {ex.Message}");
            }
            
            return result;
        }
        
        private string GetBaseIP(string ipAddress)
        {
            // Assuming a /24 subnet
            string[] octets = ipAddress.Split('.');
            return $"{octets[0]}.{octets[1]}.{octets[2]}";
        }
        
        private bool IsPortOpen(string host, int port, int timeoutMs)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var result = client.BeginConnect(host, port, null, null);
                    var success = result.AsyncWaitHandle.WaitOne(timeoutMs);
                    if (success)
                    {
                        try
                        {
                            client.EndConnect(result);
                            return true;
                        }
                        catch
                        {
                            return false;
                        }
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            catch
            {
                return false;
            }
        }
        
        private string ExtractIpFromPrinterName(string printerName)
        {
            try
            {
                // Format like \\SERVER\PRINTER or IP_192.168.1.100
                if (printerName.StartsWith("\\\\"))
                {
                    string[] parts = printerName.Split('\\');
                    if (parts.Length > 2)
                    {
                        string serverName = parts[2];
                        if (IPAddress.TryParse(serverName, out _))
                            return serverName;
                            
                        // Try to resolve server name to IP
                        try
                        {
                            IPHostEntry entry = Dns.GetHostEntry(serverName);
                            if (entry.AddressList.Length > 0)
                                return entry.AddressList[0].ToString();
                        }
                        catch
                        {
                            // Cannot resolve
                        }
                    }
                }
                else if (printerName.Contains("IP_"))
                {
                    int ipIndex = printerName.IndexOf("IP_");
                    if (ipIndex >= 0)
                    {
                        string ipPart = printerName.Substring(ipIndex + 3);
                        // Try to extract IP address
                        foreach (var part in ipPart.Split('_', ' ', ','))
                        {
                            if (IPAddress.TryParse(part, out _))
                                return part;
                        }
                    }
                }
            }
            catch
            {
                // Ignore
            }
            
            return "Unknown";
        }
        
        private string TranslatePrinterStatus(uint status)
        {
            switch (status)
            {
                case 1: return "Xato";
                case 2: return "Xato";
                case 3: return "Band";
                case 4: return "Tayyor";
                case 5: return "Chop etilmoqda";
                case 6: return "Qog'oz yo'q";
                case 7: return "Qog'oz tugayapti";
                default: return "Noma'lum holat";
            }
        }
        
        private void OnPrinterDiscovered(PrinterInfo printer)
        {
            System.Diagnostics.Debug.WriteLine($"onprinterdiscovered chaqirildi: {printer.Name}");
            PrinterDiscovered?.Invoke(this, new PrinterDiscoveredEventArgs { Printer = printer });
        }
        
        private void OnScanCompleted(bool cancelled)
        {
            System.Diagnostics.Debug.WriteLine($"onscancompleted chaqirildi, cancelled: {cancelled}, printerlar soni: {_discoveredPrinters.Count}");
            ScanCompleted?.Invoke(this, new PrinterScanCompletedEventArgs { 
                Cancelled = cancelled, 
                DiscoveredPrinters = _discoveredPrinters
            });
        }
        
        #endregion

        #region IDisposable Implementation

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
                    if (_networkScanWorker.IsBusy)
                    {
                        _networkScanWorker.CancelAsync();
                    }
                    
                    // Remove event handlers
                    _networkScanWorker.DoWork -= NetworkScanWorker_DoWork;
                    _networkScanWorker.ProgressChanged -= NetworkScanWorker_ProgressChanged;
                    _networkScanWorker.RunWorkerCompleted -= NetworkScanWorker_RunWorkerCompleted;
                    
                    System.Diagnostics.Debug.WriteLine("printerdiscoveryservice disposed");
                }
                
                // Free unmanaged resources
                
                _disposed = true;
            }
        }
        
        ~PrinterDiscoveryService()
        {
            Dispose(false);
        }

        #endregion
    }
    
    public class PrinterInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public int Port { get; set; } = 0;
        public string Status { get; set; } = string.Empty;
        public string ConnectionType { get; set; } = string.Empty;
        public bool IsNetworkPrinter { get; set; }
        public bool IsDefault { get; set; }
        public string DriverName { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        
        public override string ToString()
        {
            return Name;
        }
    }
    
    public class PrinterDiscoveredEventArgs : EventArgs
    {
        public PrinterInfo Printer { get; set; } = new PrinterInfo();
    }
    
    public class PrinterScanCompletedEventArgs : EventArgs
    {
        public bool Cancelled { get; set; }
        public List<PrinterInfo> DiscoveredPrinters { get; set; } = new List<PrinterInfo>();
    }
} 