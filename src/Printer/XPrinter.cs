using Restaurants.Class.ContractorOrder_Get;
using Restaurants.Class.Printer;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace Restaurants.Printer;

// Interfaces for type safety
public interface IPrintableOrderItem
{
    string ProductShortName { get; }
    decimal Quantity { get; }
    decimal EstimatedPrice { get; }
    decimal Amount { get; }
}

public interface IPrintableKitchenItem
{
    string ProductShortName { get; }
    decimal Quantity { get; }
    int OrderNumber { get; }
    string ContractorRequirement { get; }
    string Details { get; }
}

public class XPrinter : IDisposable
{
    private IntPtr printer;
    private int openStatus = -100;
    private bool disposed = false;

    // Ethernet support
    private TcpClient tcpClient;
    private NetworkStream networkStream;
    private string printerIpAddress;
    private int printerPort;
    private bool isEthernetMode = false;
    private int connectionTimeout = 3000; // Default timeout 3 seconds

    // Encoding support
    private Encoding printerEncoding;
    private int cyrillicCodePage = 6; // Default code page for Cyrillic

    // Constants
    private const int MAX_LINE_WIDTH = 48;
    private const string COMPANY_NAME = /*"710 BUXORO KAFE"*/"";
    private const string SEPARATOR_FULL = "================================================";
    private const string SEPARATOR_PARTIAL = "------------------------------------------------";

    private string _localPrinterName = "";

    #region Constructors

    public XPrinter()
    {
        InitializeEncoding();
        InitializeUSBPrinter();
    }

    public XPrinter(string ipAddress, int port = 9100)
    {
        InitializeEncoding();
        this.printerIpAddress = ipAddress;
        this.printerPort = port;
        this.isEthernetMode = true;
    }

    public XPrinter(int codePage = 6)
    {
        this.cyrillicCodePage = codePage;
        InitializeEncoding();
        InitializeUSBPrinter();
    }

    public XPrinter(string ipAddress, int port = 9100, int codePage = 6)
    {
        this.cyrillicCodePage = codePage;
        InitializeEncoding();
        this.printerIpAddress = ipAddress;
        this.printerPort = port;
        this.isEthernetMode = true;
    }

    #endregion

    #region Encoding Initialization

    private void InitializeEncoding()
    {
        try
        {
            // Register encoding provider for code page support
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // Try different encodings in order of preference - prioritize Cyrillic encodings
            // The correct encoding depends on the printer's code page
            string encodingName;

            switch (cyrillicCodePage)
            {
                case 17: // CP866
                    encodingName = "cp866";
                    break;
                case 7: // CP855
                    encodingName = "ibm855";
                    break;
                case 8: // CP855
                    encodingName = "ibm855";
                    break;
                case 6: // Default Cyrillic
                default:
                    encodingName = "windows-1251";
                    break;
            }

            try
            {
                printerEncoding = Encoding.GetEncoding(encodingName);
                System.Diagnostics.Debug.WriteLine($"successfully initialized printer with encoding: {encodingName} for code page {cyrillicCodePage}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"failed to initialize primary encoding {encodingName}: {ex.Message}");

                // Try fallback encodings
                var fallbackEncodings = new[] { "windows-1251", "cp866", "ibm855", "windows-1252", "utf8" };

                foreach (var fallbackEncoding in fallbackEncodings)
                {
                    try
                    {
                        printerEncoding = Encoding.GetEncoding(fallbackEncoding);
                        System.Diagnostics.Debug.WriteLine($"using fallback encoding: {fallbackEncoding}");
                        break;
                    }
                    catch (Exception fallbackEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"failed to initialize fallback encoding {fallbackEncoding}: {fallbackEx.Message}");
                    }
                }
            }

            // Fallback to UTF-8 if nothing works
            if (printerEncoding == null)
            {
                System.Diagnostics.Debug.WriteLine("falling back to utf-8 encoding");
                printerEncoding = Encoding.UTF8;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"encoding initialization error: {ex.Message}");
            printerEncoding = Encoding.UTF8;
        }
    }

    #endregion

    #region DLL Imports

    [DllImport("printer.sdk.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.StdCall)]
    public static extern IntPtr InitPrinter(string model);

    [DllImport("printer.sdk.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.StdCall)]
    public static extern int OpenPort(IntPtr intPtr, string port);

    [DllImport("printer.sdk.dll", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
    public static extern int PrintText(IntPtr intPtr, string data, int alignment, int textSize);

    // Alternative method that accepts byte array for better encoding control
    [DllImport("printer.sdk.dll", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
    public static extern int PrintBytes(IntPtr intPtr, byte[] data, int length, int alignment, int textSize);

    [DllImport("printer.sdk.dll", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
    public static extern int FeedLine(IntPtr intPtr, int lines);

    [DllImport("printer.sdk.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.StdCall)]
    public static extern int ClosePort(IntPtr intPtr);

    [DllImport("printer.sdk.dll", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
    public static extern int CutPaperWithDistance(IntPtr intPtr, int distance);

    #endregion

    #region Initialization Methods

    private void InitializeUSBPrinter()
    {
        try
        {
            this.printer = InitPrinter("");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"USB printer initializatsiyasida xato: {ex.Message}", ex);
        }
    }

    private async Task<bool> InitializeEthernetConnection()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"initializeethenetconnection boshlanmoqda: {printerIpAddress}:{printerPort}");

            if (string.IsNullOrEmpty(printerIpAddress))
            {
                System.Diagnostics.Debug.WriteLine("printer ip manzili ko'rsatilmagan");
                throw new ArgumentException("Printer IP manzili ko'rsatilmagan");
            }

            // Close any existing connection
            CloseConnection();

            // Create a new TCP client with the specified timeout
            tcpClient = new TcpClient();

            // Try to connect with timeout
            var connectTask = tcpClient.ConnectAsync(printerIpAddress, printerPort);
            var timeoutTask = Task.Delay(connectionTimeout);

            // Wait for either connection or timeout
            var completedTask = await Task.WhenAny(connectTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                // Connection timed out
                System.Diagnostics.Debug.WriteLine("printer ulanishi vaqt tugashi");
                throw new TimeoutException($"Printer ulanishi vaqt tugashi: {printerIpAddress}:{printerPort}");
            }

            // Check if the connection task completed successfully
            await connectTask; // This will throw if the connection failed

            // Get the network stream
            networkStream = tcpClient.GetStream();

            // Set timeouts for read/write operations
            networkStream.ReadTimeout = connectionTimeout;
            networkStream.WriteTimeout = connectionTimeout;

            System.Diagnostics.Debug.WriteLine("ethernet ulanishi muvaffaqiyatli o'rnatildi");
            isEthernetMode = true;
            return true;
        }
        catch (SocketException ex)
        {
            System.Diagnostics.Debug.WriteLine($"socket xatoligi: {ex.Message}");
            CloseConnection();
            throw new InvalidOperationException($"Printerga ulanib bo'lmadi: {printerIpAddress}:{printerPort}. Printer yoqilganligini va tarmoqqa ulanganligini tekshiring.", ex);
        }
        catch (TimeoutException ex)
        {
            System.Diagnostics.Debug.WriteLine($"vaqt tugashi xatoligi: {ex.Message}");
            CloseConnection();
            throw new TimeoutException($"Printerga ulanish vaqti tugadi: {printerIpAddress}:{printerPort}. Printer yoqilganligini va tarmoqqa ulanganligini tekshiring.", ex);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ethernet ulanishida xatolik: {ex.Message}");
            CloseConnection();
            throw new InvalidOperationException($"Ethernet ulanishida xatolik: {ex.Message}", ex);
        }
    }

    #endregion

    #region Connection Management

    private async Task<int> OpenPortAsync()
    {
        try
        {
            if (isEthernetMode)
            {
                System.Diagnostics.Debug.WriteLine("ethernet rejimida portni ochish");
                bool connected = await InitializeEthernetConnection();
                return connected ? 0 : -1;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("usb rejimida portni ochish");

                if (!string.IsNullOrEmpty(_localPrinterName))
                {
                    System.Diagnostics.Debug.WriteLine($"lokal printer ishlatilmoqda: {_localPrinterName}");
                    // For local printers with specific name
                    if (this.openStatus == 0)
                        return this.openStatus;

                    this.openStatus = MainWindow.OpenPort(this.printer, _localPrinterName);
                    System.Diagnostics.Debug.WriteLine($"port ochish natijasi: {this.openStatus}");
                    return this.openStatus;
                }
                else
                {
                    // Default USB port
                    if (this.openStatus == 0)
                        return this.openStatus;

                    this.openStatus = MainWindow.OpenPort(this.printer, "USB,USB001");
                    System.Diagnostics.Debug.WriteLine($"port ochish natijasi: {this.openStatus}");
                    return this.openStatus;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"port ochishda xatolik: {ex.Message}");
            throw new InvalidOperationException($"Port ochishda xato: {ex.Message}", ex);
        }
    }

    private void CloseConnection()
    {
        try
        {
            if (isEthernetMode)
            {
                networkStream?.Close();
                tcpClient?.Close();
            }
            else if (this.openStatus == 0)
            {
                ClosePort(this.printer);
                this.openStatus = -100;
            }
        }
        catch (Exception ex)
        {
            // Log error but don't throw during cleanup
            System.Diagnostics.Debug.WriteLine($"Connection yopishda xato: {ex.Message}");
        }
    }

    #endregion

    #region Print Methods

    // Generic versions for type safety
    public async Task PrintAsync<T>(PrintOrder printOrder) where T : class
    {
        if (printOrder == null)
            throw new ArgumentNullException(nameof(printOrder));

        string textToPrint = BuildPrintText(printOrder);
        await ExecutePrintAsync(textToPrint, "Chek muvaffaqiyatli chop etildi");
    }

    public async Task PrintAsync<T>(ContractorOrder printOrder) where T : class
    {
        if (printOrder == null)
            throw new ArgumentNullException(nameof(printOrder));

        string textToPrint = BuildKitchenPrintText(printOrder);
        await ExecutePrintAsync(textToPrint, "Bosib chiqarish muvaffaqiyatli yakunlandi.");
    }

    // Original methods for backward compatibility
    public async Task PrintAsync(PrintOrder printOrder)
    {
        System.Diagnostics.Debug.WriteLine($"printasync(printorder) boshlanmoqda: {printOrder?.CheckNumber ?? "null"}");

        try
        {
            if (printOrder == null)
                throw new ArgumentNullException(nameof(printOrder));

            string textToPrint = BuildPrintText(printOrder);
            System.Diagnostics.Debug.WriteLine($"printasync(printorder): text uzunligi {textToPrint?.Length ?? 0} belgi");
            await ExecutePrintAsync(textToPrint, "Chek muvaffaqiyatli chop etildi");
            System.Diagnostics.Debug.WriteLine("printasync(printorder) muvaffaqiyatli yakunlandi");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"printasync(printorder) xatoligi: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"stack trace: {ex.StackTrace}");
            throw;
        }
    }

    public async Task PrintAsync(ContractorOrder printOrder)
    {
        System.Diagnostics.Debug.WriteLine($"printasync(contractororder) boshlanmoqda: {printOrder?.DocNumber ?? "null"}");

        try
        {
            if (printOrder == null)
                throw new ArgumentNullException(nameof(printOrder));

            string textToPrint = BuildKitchenPrintText(printOrder);
            System.Diagnostics.Debug.WriteLine($"printasync(contractororder): text uzunligi {textToPrint?.Length ?? 0} belgi");
            await ExecutePrintAsync(textToPrint, "Bosib chiqarish muvaffaqiyatli yakunlandi.");
            System.Diagnostics.Debug.WriteLine("printasync(contractororder) muvaffaqiyatli yakunlandi");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"printasync(contractororder) xatoligi: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"stack trace: {ex.StackTrace}");
            throw;
        }
    }

    // Synchronous versions for backward compatibility
    public void PrintText(PrintOrder printOrder)
    {
        Task.Run(async () => await PrintAsync(printOrder)).Wait();
    }

    public void PrintText(ContractorOrder printOrder)
    {
        Task.Run(async () => await PrintAsync(printOrder)).Wait();
    }

    public async Task ExecutePrintAsync(string textToPrint, string successMessage)
    {
        System.Diagnostics.Debug.WriteLine($"executeprintasync boshlanmoqda, text uzunligi: {textToPrint?.Length ?? 0}");

        try
        {
            int connectionResult = await OpenPortAsync();
            System.Diagnostics.Debug.WriteLine($"openportasync natijasi: {connectionResult}");

            if (connectionResult != 0)
            {
                System.Diagnostics.Debug.WriteLine("printer tayyor emas");
                ShowError("Printer tayyor emas.");
                return;
            }

            if (isEthernetMode)
            {
                System.Diagnostics.Debug.WriteLine("ethernet rejimida chop etilmoqda");
                await PrintToEthernetAsync(textToPrint);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("usb rejimida chop etilmoqda");
                PrintToUSB(textToPrint);
            }

            System.Diagnostics.Debug.WriteLine($"chop etish muvaffaqiyatli: {successMessage}");
            ShowSuccess(successMessage);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"executeprintasync xatoligi: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"stack trace: {ex.StackTrace}");
            ShowError($"Chop etishda xato: {ex.Message}");
        }
        finally
        {
            System.Diagnostics.Debug.WriteLine("ulanish yopilmoqda");
            CloseConnection();
            System.Diagnostics.Debug.WriteLine("executeprintasync yakunlandi");
        }
    }

    #region Printer Commands

    private void SendCyrillicInit()
    {
        try
        {
            // For many ESC/POS printers, need to send multiple commands to properly set up Cyrillic

            // 1. Initialize printer
            byte[] initCommand = { 0x1B, 0x40 }; // ESC @

            // 2. Character code page selection
            byte[] selectCodePage;
            if (cyrillicCodePage == 17)
            {
                // For CP866 (Russian) - many printers use code page 17
                selectCodePage = new byte[] { 0x1B, 0x74, 17 }; // ESC t 17
            }
            else if (cyrillicCodePage == 7)
            {
                // For CP855 (Cyrillic)
                selectCodePage = new byte[] { 0x1B, 0x74, 7 }; // ESC t 7
            }
            else
            {
                // Default to windows-1251
                selectCodePage = new byte[] { 0x1B, 0x74, 6 }; // ESC t 6
            }

            // 3. International character set selection (country code)
            byte[] intlCharSet = { 0x1B, 0x52, 10 }; // ESC R 10 (Russia)

            // 4. Define code page if custom page is needed
            // Some printers require this for Cyrillic
            byte[] defineCodePage = { 0x1B, 0x28, 0x74, 3, 0, 3, 0 }; // ESC ( t - Define character code

            // 5. Character size - normal
            byte[] charSize = { 0x1D, 0x21, 0 }; // GS ! 0

            // 6. Font selection - usually font A (0) is better for Cyrillic
            byte[] fontSelect = { 0x1B, 0x4D, 0 }; // ESC M 0

            // Combine essential commands
            byte[] combinedCommands = new byte[initCommand.Length + selectCodePage.Length + intlCharSet.Length];
            Buffer.BlockCopy(initCommand, 0, combinedCommands, 0, initCommand.Length);
            Buffer.BlockCopy(selectCodePage, 0, combinedCommands, initCommand.Length, selectCodePage.Length);
            Buffer.BlockCopy(intlCharSet, 0, combinedCommands, initCommand.Length + selectCodePage.Length, intlCharSet.Length);

            if (isEthernetMode && networkStream != null)
            {
                // For Ethernet mode
                networkStream.Write(initCommand, 0, initCommand.Length);
                networkStream.Write(selectCodePage, 0, selectCodePage.Length);
                networkStream.Write(intlCharSet, 0, intlCharSet.Length);
                networkStream.Write(charSize, 0, charSize.Length);
                networkStream.Write(fontSelect, 0, fontSelect.Length);
                System.Diagnostics.Debug.WriteLine($"sent cyrillic init commands over network (code page: {cyrillicCodePage})");
            }
            else if (!isEthernetMode && printer != IntPtr.Zero)
            {
                // For USB mode - use PrintBytes with zeroes for alignment and size
                PrintBytes(printer, initCommand, initCommand.Length, 0, 0);
                PrintBytes(printer, selectCodePage, selectCodePage.Length, 0, 0);
                PrintBytes(printer, intlCharSet, intlCharSet.Length, 0, 0);
                PrintBytes(printer, charSize, charSize.Length, 0, 0);
                PrintBytes(printer, fontSelect, fontSelect.Length, 0, 0);
                System.Diagnostics.Debug.WriteLine($"sent cyrillic init commands over usb (code page: {cyrillicCodePage})");
            }

            // Try additional initialization for stubborn printers
            try
            {
                // Direct print a short text with special encoding
                // This can sometimes "kick" the printer into the correct mode
                if (!isEthernetMode && printer != IntPtr.Zero)
                {
                    // Special command sequence that works on some printers
                    byte[] specialInit = {
                        0x1B, 0x74, 17,   // Select Code Page 17 (CP866)
                        0x1B, 0x52, 10    // Select Country 10 (Russia)
                    };
                    PrintBytes(printer, specialInit, specialInit.Length, 0, 0);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"auxiliary init failed: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"failed to send initialization commands: {ex.Message}");
        }
    }

    // Helper to send command bytes via PrintBytes for USB mode
    private void SendCommand(byte[] command)
    {
        if (printer != IntPtr.Zero)
        {
            try
            {
                PrintBytes(printer, command, command.Length, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"send command failed: {ex.Message}");
            }
        }
    }

    #endregion

    private async Task PrintToEthernetAsync(string textToPrint)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"printtoethernetasync boshlanmoqda, text uzunligi: {textToPrint?.Length ?? 0}");

            if (networkStream == null || tcpClient == null || !tcpClient.Connected)
            {
                System.Diagnostics.Debug.WriteLine("tarmoq oqimi mavjud emas yoki printer ulanmagan, qayta ulanish");
                await InitializeEthernetConnection();
            }

            // Send initialization and character set commands
            SendCyrillicInit();

            // Convert text to bytes using the configured encoding
            byte[] textBytes = printerEncoding.GetBytes(textToPrint);

            // Write to the network stream
            await networkStream.WriteAsync(textBytes, 0, textBytes.Length);
            await networkStream.FlushAsync();

            // Feed one line and cut paper
            byte[] feedAndCutCommand = new byte[] { 0x1B, 0x64, 0x01, 0x1D, 0x56, 0x41, 0x00 };
            await networkStream.WriteAsync(feedAndCutCommand, 0, feedAndCutCommand.Length);
            await networkStream.FlushAsync();

            System.Diagnostics.Debug.WriteLine("ethernet orqali chop etish muvaffaqiyatli yakunlandi");
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine($"io xatoligi: {ex.Message}");
            throw new InvalidOperationException($"Printer bilan aloqa qilishda xatolik. Printer yoqilganligini tekshiring.", ex);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ethernet orqali chop etishda xatolik: {ex.Message}");
            throw new InvalidOperationException($"Ethernet orqali chop etishda xatolik: {ex.Message}", ex);
        }
    }

    private void PrintToUSB(string textToPrint)
    {
        try
        {

            // Ensure the printer is initialized
            if (this.printer == IntPtr.Zero)
            {
                ShowError("printer initializing failed. please check the printer connection.");
                return;
            }

            // Send initialization and character set commands
            SendCyrillicInit();

            // Convert text to bytes using the configured encoding
            byte[] textBytes = printerEncoding.GetBytes(textToPrint);

            // Try to use byte array method if available, otherwise fallback to string
            int result;
            try
            {
                result = PrintBytes(this.printer, textBytes, textBytes.Length, 0, 0);
            }
            catch (EntryPointNotFoundException)
            {
                // Fallback to string method if PrintBytes is not available
                result = PrintText(this.printer, textToPrint, 0, 0);
            }

            if (result != 0)
            {
                ShowError($"chop etishda xato: {result}");
                return;
            }

            // Feed one line after printing
            FeedLine(this.printer, 1);

            // Cut paper
            int cutResult = CutPaperWithDistance(this.printer, 1);
            if (cutResult != 0)
            {
                System.Diagnostics.Debug.WriteLine("qog'ozni kesishda xato.");
            }
        }
        catch (Exception ex)
        {
            ShowError($"usb orqali chop etishda xato: {ex.Message}");
        }
    }

    #endregion

    #region Text Building Methods

    private static string BuildPrintText(PrintOrder order)
    {
        System.Diagnostics.Debug.WriteLine($"buildprinttext boshlanmoqda: {order?.CheckNumber ?? "null"}");

        try
        {
            var sb = new StringBuilder();

            // Header
            AppendCenteredText(sb, COMPANY_NAME);
            sb.AppendLine();

            // Order Details
            sb.AppendLine($"Zakaz N#: {order.CheckNumber}");
            sb.AppendLine($"Ofitsiant: {order.WaiterName}");

            try
            {
                var orderDate = DateTime.ParseExact(order.OrderDate, "dd.MM.yyyy", null);
                var orderTime = DateTime.ParseExact(order.OrderTime, "HH:mm", null);
                sb.AppendLine($"Sana: {orderDate:dd.MM.yyyy} Vaqt: {orderTime:HH:mm}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"sana/vaqtni formatlashda xatolik: {ex.Message}");
                sb.AppendLine($"Sana: {order.OrderDate} Vaqt: {order.OrderTime}");
            }

            sb.AppendLine($"Stol: {order.TableNumber}");
            sb.AppendLine();
            sb.AppendLine(SEPARATOR_FULL);

            // Order items
            if (order.Orders == null || !order.Orders.Any())
            {
                System.Diagnostics.Debug.WriteLine("buyurtma elementlari mavjud emas");
                sb.AppendLine("Buyurtma elementlari mavjud emas");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"buyurtma elementlari soni: {order.Orders.Count}");
                AppendOrderItems(sb, order.Orders?.Cast<object>());
            }

            sb.AppendLine(SEPARATOR_FULL);
            sb.AppendLine();

            // Totals
            AppendOrderTotals(sb, order);

            sb.AppendLine(SEPARATOR_FULL);
            AppendJustifiedLine(sb, "Jami:", $"{FormatAmount(order.GrandTotal)} UZS");

            AppendCenteredText(sb, "\n\nXaridingiz uchun rahmat!\n\n");

            System.Diagnostics.Debug.WriteLine("buildprinttext muvaffaqiyatli yakunlandi");
            return sb.ToString();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"buildprinttext xatoligi: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"stack trace: {ex.StackTrace}");

            // Return a simple error message that can be printed
            var errorSb = new StringBuilder();
            errorSb.AppendLine("=== XATOLIK ===");
            errorSb.AppendLine($"Buyurtma chop etishda xatolik: {ex.Message}");
            errorSb.AppendLine($"Vaqt: {DateTime.Now}");
            errorSb.AppendLine("===============");
            return errorSb.ToString();
        }
    }

    private static string BuildKitchenPrintText(ContractorOrder order)
    {
        System.Diagnostics.Debug.WriteLine($"buildkitchenprinttext boshlanmoqda: {order?.DocNumber ?? "null"}");

        try
        {
            var sb = new StringBuilder();

            // Header
            sb.AppendLine();
            AppendCenteredText(sb, "YANGI BUYURTMA!");
            sb.AppendLine(SEPARATOR_FULL);

            // Order Information
            sb.AppendLine($"Zakaz N#: {order.DocNumber}");
            sb.AppendLine($"Ofitsiant: {order.Responsible}");

            try
            {
                var docDate = DateTime.Parse(order.DocDate);
                var docTime = DateTime.Parse(order.DocTime);
                sb.AppendLine($"Sana: {docDate:dd.MM.yyyy} Vaqt: {docTime:HH:mm}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"sana/vaqtni formatlashda xatolik: {ex.Message}");
                sb.AppendLine($"Sana: {order.DocDate} Vaqt: {order.DocTime}");
            }

            // Tables information
            if (order.Tables?.Count > 0)
            {
                try
                {
                    var tableNumbers = order.Tables.Select(t => t.OrderNumber.ToString()).ToArray();
                    sb.AppendLine($"Stol: {string.Join(", ", tableNumbers)}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"stol raqamlarini olishda xatolik: {ex.Message}");
                    sb.AppendLine("Stol: Noma'lum");
                }
            }

            sb.AppendLine(SEPARATOR_FULL);

            // Kitchen items header
            sb.AppendLine("  TAOM NOMI                      MIQDORI");
            sb.AppendLine(SEPARATOR_PARTIAL);

            // Kitchen items
            if (order.Tables == null || !order.Tables.Any())
            {
                System.Diagnostics.Debug.WriteLine("oshxona elementlari mavjud emas");
                sb.AppendLine("Oshxona elementlari mavjud emas");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"oshxona elementlari soni: {order.Tables.Count}");
                AppendKitchenItems(sb, order.Tables?.Cast<object>());
            }

            sb.AppendLine(SEPARATOR_FULL);
            sb.AppendLine($"Jami taomlar soni: {order.Tables?.Count ?? 0}");
            sb.AppendLine($"Chek chop etildi: {DateTime.Now:HH:mm:ss}");

            System.Diagnostics.Debug.WriteLine("buildkitchenprinttext muvaffaqiyatli yakunlandi");
            return sb.ToString();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"buildkitchenprinttext xatoligi: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"stack trace: {ex.StackTrace}");

            // Return a simple error message that can be printed
            var errorSb = new StringBuilder();
            errorSb.AppendLine("=== XATOLIK ===");
            errorSb.AppendLine($"Oshxona buyurtmasini chop etishda xatolik: {ex.Message}");
            errorSb.AppendLine($"Vaqt: {DateTime.Now}");
            errorSb.AppendLine("===============");
            return errorSb.ToString();
        }
    }

    #endregion

    #region Helper Methods

    private static void AppendOrderItems(StringBuilder sb, IEnumerable<object> orders)
    {
        if (orders == null)
        {
            System.Diagnostics.Debug.WriteLine("appendorderitems: orders is null");
            return;
        }

        try
        {
            var orderList = orders.ToList();
            System.Diagnostics.Debug.WriteLine($"appendorderitems: {orderList.Count} ta element");

            for (int i = 0; i < orderList.Count; i++)
            {
                try
                {
                    var item = orderList[i];
                    System.Diagnostics.Debug.WriteLine($"element {i + 1} turini tekshirish: {item?.GetType().Name ?? "null"}");

                    // Use reflection to get properties
                    var productName = GetPropertyValue(item, "ProductShortName")?.ToString() ?? "";
                    var quantity = GetPropertyValue(item, "Quantity");
                    var estimatedPrice = GetPropertyValue(item, "EstimatedPrice");
                    var amount = GetPropertyValue(item, "Amount");

                    System.Diagnostics.Debug.WriteLine($"element {i + 1} ma'lumotlari: {productName}, {quantity}, {estimatedPrice}, {amount}");

                    string formattedProductName = FormatProductName(productName, 25);
                    string calculation = $"{quantity} * {FormatAmount(Convert.ToDecimal(estimatedPrice))} = {FormatAmount(Convert.ToDecimal(amount))}";

                    AppendJustifiedLine(sb, formattedProductName, calculation);

                    if (i < orderList.Count - 1)
                        sb.AppendLine(SEPARATOR_PARTIAL);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"element {i + 1} ni qayta ishlashda xatolik: {ex.Message}");
                    sb.AppendLine($"Element {i + 1}: Xatolik - {ex.Message}");
                    if (i < orderList.Count - 1)
                        sb.AppendLine(SEPARATOR_PARTIAL);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"appendorderitems xatoligi: {ex.Message}");
            sb.AppendLine($"Buyurtma elementlarini qayta ishlashda xatolik: {ex.Message}");
        }
    }

    private static void AppendOrderTotals(StringBuilder sb, PrintOrder order)
    {
        AppendJustifiedLine(sb, "Summa:", $"{FormatAmount(order.TotalAmount)} UZS");
        AppendJustifiedLine(sb, $"Xizmat haqi ({order.AdditionalPercentage}%):", $"{FormatAmount(order.ServiceFee)} UZS");

        if (order.DiscountAmount > 0)
        {
            string discountLabel = order.IsDiscountPercentage
                ? $"Chegirma ({order.DiscountPercentage}%):"
                : "Chegirma:";
            AppendJustifiedLine(sb, discountLabel, $"-{FormatAmount(order.DiscountAmount)} UZS");
        }

        switch (order.PaymentTypeId)
        {
            case 1:
                order.PaymentTypeText = "Naqd";
                break;
            case 2:
                order.PaymentTypeText = "Plastik karta";
                break;
            case 3:
                order.PaymentTypeText = "Naqd pulsiz to'lov";
                break;
            case 4:
                order.PaymentTypeText = "Plastic to plastic";
                break;
            default:
                order.PaymentTypeText = "Noma'lum to'lov turi";
                break;
        }

        AppendJustifiedLine(sb, "To'lov turi:", order.PaymentTypeText);
    }

    private static void AppendKitchenItems(StringBuilder sb, IEnumerable<object> tables)
    {
        if (tables == null)
        {
            System.Diagnostics.Debug.WriteLine("appendkitchenitems: tables is null");
            return;
        }

        try
        {
            var tableList = tables.ToList();
            System.Diagnostics.Debug.WriteLine($"appendkitchenitems: {tableList.Count} ta element");

            for (int i = 0; i < tableList.Count; i++)
            {
                try
                {
                    var item = tableList[i];
                    System.Diagnostics.Debug.WriteLine($"element {i + 1} turini tekshirish: {item?.GetType().Name ?? "null"}");

                    // Use reflection to get properties
                    var productName = GetPropertyValue(item, "ProductShortName")?.ToString() ?? "";
                    var quantity = GetPropertyValue(item, "Quantity");
                    var contractorRequirement = GetPropertyValue(item, "ContractorRequirement")?.ToString();
                    var details = GetPropertyValue(item, "Details")?.ToString();

                    System.Diagnostics.Debug.WriteLine($"element {i + 1} ma'lumotlari: {productName}, {quantity}");

                    string sequenceNumber = $"{i + 1:D2}.";
                    string formattedProductName = FormatProductName(productName, 30);
                    string quantityStr = Convert.ToDecimal(quantity).ToString("0.###");

                    sb.AppendLine($"{sequenceNumber} {formattedProductName} {quantityStr}");

                    // Add special instructions if any
                    AppendInstructionIfExists(sb, contractorRequirement);
                    AppendInstructionIfExists(sb, details);

                    if (i < tableList.Count - 1)
                        sb.AppendLine(SEPARATOR_PARTIAL);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"element {i + 1} ni qayta ishlashda xatolik: {ex.Message}");
                    sb.AppendLine($"Element {i + 1}: Xatolik - {ex.Message}");
                    if (i < tableList.Count - 1)
                        sb.AppendLine(SEPARATOR_PARTIAL);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"appendkitchenitems xatoligi: {ex.Message}");
            sb.AppendLine($"Oshxona elementlarini qayta ishlashda xatolik: {ex.Message}");
        }
    }

    private static object GetPropertyValue(object obj, string propertyName)
    {
        try
        {
            if (obj == null)
            {
                System.Diagnostics.Debug.WriteLine($"getpropertyvalue: obj is null for {propertyName}");
                return null;
            }

            var property = obj.GetType().GetProperty(propertyName);
            if (property == null)
            {
                System.Diagnostics.Debug.WriteLine($"getpropertyvalue: property {propertyName} not found in {obj.GetType().Name}");
                return null;
            }

            var value = property.GetValue(obj);
            System.Diagnostics.Debug.WriteLine($"getpropertyvalue: {propertyName} = {value}");
            return value;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"getpropertyvalue xatoligi: {ex.Message} for {propertyName}");
            return null;
        }
    }

    private static void AppendInstructionIfExists(StringBuilder sb, string instruction)
    {
        if (!string.IsNullOrEmpty(instruction))
        {
            sb.AppendLine($"   > {instruction}");
        }
    }

    private static void AppendCenteredText(StringBuilder sb, string text)
    {
        int padding = Math.Max(0, (MAX_LINE_WIDTH - text.Length) / 2);
        sb.AppendLine($"{new string(' ', padding)}{text}");
    }

    private static void AppendJustifiedLine(StringBuilder sb, string left, string right)
    {
        int dotsCount = Math.Max(1, MAX_LINE_WIDTH - left.Length - right.Length);
        sb.AppendLine($"{left}{new string(' ', dotsCount)}{right}");
    }

    private static string FormatProductName(string name, int maxLength)
    {
        if (string.IsNullOrEmpty(name))
            return string.Empty.PadRight(maxLength);

        return name.Length <= maxLength
            ? name.PadRight(maxLength)
            : name.Substring(0, maxLength - 3) + "...";
    }

    private static string FormatAmount(decimal amount)
    {
        return ((int)amount).ToString("N0", new NumberFormatInfo
        {
            NumberGroupSizes = new[] { 3 },
            NumberGroupSeparator = " "
        });
    }

    private static void ShowError(string message)
    {
        MessageBox.Show(message, "Xato", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static void ShowSuccess(string message)
    {
        MessageBox.Show(message, "Ma'lumot", MessageBoxButton.OK, MessageBoxImage.Information);
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
        if (!disposed)
        {
            if (disposing)
            {
                CloseConnection();
                networkStream?.Dispose();
                tcpClient?.Dispose();
            }
            disposed = true;
        }
    }

    ~XPrinter()
    {
        Dispose(false);
    }

    #endregion

    #region Test Methods

    /// <summary>
    /// Prints a test page for testing Cyrillic character support with different code pages
    /// </summary>
    public async Task PrintCyrillicTestAsync()
    {
        var testText = new StringBuilder();
        testText.AppendLine("===== CYRILLIC TEST PAGE =====");
        testText.AppendLine($"Code page: {cyrillicCodePage}");
        testText.AppendLine($"Encoding: {printerEncoding.EncodingName}");
        testText.AppendLine();
        testText.AppendLine("Russian: Это тест");
        testText.AppendLine("Uzbek: Сизнинг буюртмангиз");
        testText.AppendLine("Numbers: 1234567890");
        testText.AppendLine("Symbols: !@#$%^&*()_+");
        testText.AppendLine("=========================");

        await ExecutePrintAsync(testText.ToString(), "test page printed");
    }

    /// <summary>
    /// Sets a different code page and encoding for Cyrillic support
    /// </summary>
    public void SetCyrillicCodePage(int codePage)
    {
        this.cyrillicCodePage = codePage;
        InitializeEncoding();
        System.Diagnostics.Debug.WriteLine($"changed code page to: {codePage}");
    }

    /// <summary>
    /// Test all available Cyrillic code pages to find the one that works
    /// </summary>
    public async Task TestAllCyrillicPagesAsync()
    {
        var codePages = new[] { 6, 7, 8, 17 };
        var testText = new StringBuilder();

        testText.AppendLine("===== CYRILLIC CODE PAGE TEST =====");

        foreach (var codePage in codePages)
        {
            this.cyrillicCodePage = codePage;
            InitializeEncoding();

            testText.AppendLine($"--- Code Page: {codePage} ---");
            testText.AppendLine($"Encoding: {printerEncoding.EncodingName}");
            testText.AppendLine("Text: Сизнинг буюртмангиз");
            testText.AppendLine();
        }

        testText.AppendLine("================================");

        await ExecutePrintAsync(testText.ToString(), "code page test printed");

        // Reset to default
        this.cyrillicCodePage = 6;
        InitializeEncoding();
    }

    /// <summary>
    /// Quick test method that prints the same text with multiple code pages for comparison
    /// </summary>
    public async Task QuickCyrillicTestAsync()
    {
        try
        {
            int connectionResult = await OpenPortAsync();
            if (connectionResult != 0)
            {
                ShowError("printer tayyor emas.");
                return;
            }

            StringBuilder sb = new StringBuilder();

            // Test text to print with different code pages
            string testMessage = "Сизнинг буюртмангиз тайёр";
            byte[] codePages = { 5, 6, 7, 8, 16, 17, 18, 19 };

            foreach (byte codePage in codePages)
            {
                // Initialize printer
                byte[] initCommand = { 0x1B, 0x40 };

                // Set code page
                byte[] selectCodePage = { 0x1B, 0x74, codePage };

                // Set international character set (10 = Russia)
                byte[] intlCharSet = { 0x1B, 0x52, 0x0A };

                // Add page header
                string header = $"\n--- CODE PAGE {codePage} ---\n";
                byte[] headerBytes = printerEncoding.GetBytes(header);

                // Convert test text
                byte[] textBytes = printerEncoding.GetBytes($"{testMessage}\n\n");

                if (isEthernetMode && networkStream != null)
                {
                    await networkStream.WriteAsync(initCommand, 0, initCommand.Length);
                    await networkStream.WriteAsync(selectCodePage, 0, selectCodePage.Length);
                    await networkStream.WriteAsync(intlCharSet, 0, intlCharSet.Length);
                    await networkStream.WriteAsync(headerBytes, 0, headerBytes.Length);
                    await networkStream.WriteAsync(textBytes, 0, textBytes.Length);
                }
                else
                {
                    // Combine commands for USB mode
                    byte[] combinedCmds = new byte[initCommand.Length + selectCodePage.Length + intlCharSet.Length];
                    Buffer.BlockCopy(initCommand, 0, combinedCmds, 0, initCommand.Length);
                    Buffer.BlockCopy(selectCodePage, 0, combinedCmds, initCommand.Length, selectCodePage.Length);
                    Buffer.BlockCopy(intlCharSet, 0, combinedCmds, initCommand.Length + selectCodePage.Length, intlCharSet.Length);

                    SendCommand(combinedCmds);
                    PrintBytes(printer, headerBytes, headerBytes.Length, 0, 0);
                    PrintBytes(printer, textBytes, textBytes.Length, 0, 0);
                }
            }

            // Cut paper at end
            byte[] cutCommand = { 0x1D, 0x56, 0x01 };

            if (isEthernetMode && networkStream != null)
            {
                await networkStream.WriteAsync(cutCommand, 0, cutCommand.Length);
                await networkStream.FlushAsync();
            }
            else
            {
                CutPaperWithDistance(printer, 1);
            }

            ShowSuccess("kod sahifalari sinovdan o'tkazildi");
        }
        catch (Exception ex)
        {
            ShowError($"sinov varag'ini chop etishda xato: {ex.Message}");
        }
        finally
        {
            CloseConnection();
        }
    }

    /// <summary>
    /// Comprehensive test method to find the correct Cyrillic encoding combination
    /// Tests different combinations of code pages, international character sets, and encoding
    /// </summary>
    public async Task ComprehensiveCyrillicTestAsync()
    {
        try
        {
            int connectionResult = await OpenPortAsync();
            if (connectionResult != 0)
            {
                ShowError("printer tayyor emas.");
                return;
            }

            // Test with common Cyrillic code pages
            byte[] codePages = { 6, 7, 8, 17, 18, 19 };
            // International character sets (8=Russia, 10=Russia)
            byte[] charSets = { 0x08, 0x0A };
            // Encodings to try
            string[] encodings = { "windows-1251", "cp866", "ibm855", "koi8-r" };

            // Register all code page encodings
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // Initialize printer before each test
            byte[] initCommand = { 0x1B, 0x40 };

            // Test header
            byte[] headerBytes = Encoding.ASCII.GetBytes("\n=== CYRILLIC ENCODING TEST ===\n\n");

            if (isEthernetMode && networkStream != null)
            {
                await networkStream.WriteAsync(initCommand, 0, initCommand.Length);
                await networkStream.WriteAsync(headerBytes, 0, headerBytes.Length);
            }
            else
            {
                SendCommand(initCommand);
                PrintBytes(printer, headerBytes, headerBytes.Length, 0, 0);
            }

            // Test text in Cyrillic
            string testText = "Сизнинг буюртмангиз тайёр!";

            // Try every combination
            foreach (byte codePage in codePages)
            {
                foreach (byte charSet in charSets)
                {
                    foreach (string encodingName in encodings)
                    {
                        try
                        {
                            // Reset printer
                            if (isEthernetMode && networkStream != null)
                            {
                                await networkStream.WriteAsync(initCommand, 0, initCommand.Length);
                            }
                            else
                            {
                                SendCommand(initCommand);
                            }

                            // Set code page
                            byte[] selectCodePage = { 0x1B, 0x74, codePage };

                            // Set international character set
                            byte[] intlCharSet = { 0x1B, 0x52, charSet };

                            // Try to get encoding
                            Encoding currentEncoding;
                            try
                            {
                                currentEncoding = Encoding.GetEncoding(encodingName);
                            }
                            catch
                            {
                                // Skip if encoding not supported
                                continue;
                            }

                            // Create section header
                            string sectionHeader = $"\n--- Code Page: {codePage}, CharSet: {charSet}, Encoding: {encodingName} ---\n";
                            byte[] sectionBytes = Encoding.ASCII.GetBytes(sectionHeader);

                            // Convert test text with current encoding
                            byte[] textBytes = currentEncoding.GetBytes(testText + "\n\n");

                            // Send commands to printer
                            if (isEthernetMode && networkStream != null)
                            {
                                await networkStream.WriteAsync(selectCodePage, 0, selectCodePage.Length);
                                await networkStream.WriteAsync(intlCharSet, 0, intlCharSet.Length);
                                await networkStream.WriteAsync(sectionBytes, 0, sectionBytes.Length);
                                await networkStream.WriteAsync(textBytes, 0, textBytes.Length);
                            }
                            else
                            {
                                // Combine commands for USB mode to reduce function calls
                                byte[] combinedCmds = new byte[selectCodePage.Length + intlCharSet.Length];
                                Buffer.BlockCopy(selectCodePage, 0, combinedCmds, 0, selectCodePage.Length);
                                Buffer.BlockCopy(intlCharSet, 0, combinedCmds, selectCodePage.Length, intlCharSet.Length);

                                // Send commands and text
                                SendCommand(combinedCmds);
                                PrintBytes(printer, sectionBytes, sectionBytes.Length, 0, 0);
                                PrintBytes(printer, textBytes, textBytes.Length, 0, 0);
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"error in test: CP{codePage}, CS{charSet}, Enc{encodingName}: {ex.Message}");
                            // Continue with next test
                        }
                    }
                }
            }

            // Add final marker and cut the paper
            byte[] footerBytes = Encoding.ASCII.GetBytes("\n=== TEST COMPLETE ===\n\n");
            byte[] cutCommand = { 0x1D, 0x56, 0x01 };

            if (isEthernetMode && networkStream != null)
            {
                await networkStream.WriteAsync(footerBytes, 0, footerBytes.Length);
                await networkStream.WriteAsync(cutCommand, 0, cutCommand.Length);
                await networkStream.FlushAsync();
            }
            else
            {
                PrintBytes(printer, footerBytes, footerBytes.Length, 0, 0);
                CutPaperWithDistance(printer, 1);
            }

            ShowSuccess("keng qamrovli sinov yakunlandi. to'g'ri ishlayotgan birikma toping.");
        }
        catch (Exception ex)
        {
            ShowError($"sinov varag'ini chop etishda xato: {ex.Message}");
        }
        finally
        {
            CloseConnection();
        }
    }

    /// <summary>
    /// Direct test for a specific encoding and code page combination
    /// </summary>
    public async Task SpecificCyrillicTestAsync(int codePage, int charSet, string encodingName)
    {
        try
        {
            int connectionResult = await OpenPortAsync();
            if (connectionResult != 0)
            {
                ShowError("printer tayyor emas.");
                return;
            }

            // Register encoding provider
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // Get specified encoding
            Encoding testEncoding;
            try
            {
                testEncoding = Encoding.GetEncoding(encodingName);
            }
            catch (Exception ex)
            {
                ShowError($"encoding '{encodingName}' mavjud emas: {ex.Message}");
                return;
            }

            // Initialize printer
            byte[] initCommand = { 0x1B, 0x40 };

            // Set code page
            byte[] selectCodePage = { 0x1B, 0x74, (byte)codePage };

            // Set international character set
            byte[] intlCharSet = { 0x1B, 0x52, (byte)charSet };

            // Test text
            string header = $"\n=== TESTING SPECIFIC CONFIGURATION ===\n" +
                $"Code Page: {codePage}\n" +
                $"Int'l Char Set: {charSet}\n" +
                $"Encoding: {encodingName}\n\n";

            byte[] headerBytes = Encoding.ASCII.GetBytes(header);

            // Test text with Cyrillic characters
            string[] testStrings = {
                "Cyrillic test - Кириллица тест",
                "Russian - Русский текст",
                "Uzbek - Сизнинг буюртмангиз",
                "Extended - АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ",
                "Lowercase - абвгдеёжзийклмнопрстуфхцчшщъыьэюя",
                "Mixed - 1234-АБВГ-5678-абвг-90!@#$"
            };

            if (isEthernetMode && networkStream != null)
            {
                await networkStream.WriteAsync(initCommand, 0, initCommand.Length);
                await networkStream.WriteAsync(selectCodePage, 0, selectCodePage.Length);
                await networkStream.WriteAsync(intlCharSet, 0, intlCharSet.Length);
                await networkStream.WriteAsync(headerBytes, 0, headerBytes.Length);

                foreach (string test in testStrings)
                {
                    byte[] testBytes = testEncoding.GetBytes(test + "\n\n");
                    await networkStream.WriteAsync(testBytes, 0, testBytes.Length);
                }
            }
            else
            {
                // Combine commands
                byte[] combinedCmds = new byte[initCommand.Length + selectCodePage.Length + intlCharSet.Length];
                Buffer.BlockCopy(initCommand, 0, combinedCmds, 0, initCommand.Length);
                Buffer.BlockCopy(selectCodePage, 0, combinedCmds, initCommand.Length, selectCodePage.Length);
                Buffer.BlockCopy(intlCharSet, 0, combinedCmds, initCommand.Length + selectCodePage.Length, intlCharSet.Length);

                SendCommand(combinedCmds);
                PrintBytes(printer, headerBytes, headerBytes.Length, 0, 0);

                foreach (string test in testStrings)
                {
                    byte[] testBytes = testEncoding.GetBytes(test + "\n\n");
                    PrintBytes(printer, testBytes, testBytes.Length, 0, 0);
                }
            }

            // Cut paper
            byte[] cutCommand = { 0x1D, 0x56, 0x01 };

            if (isEthernetMode && networkStream != null)
            {
                await networkStream.WriteAsync(cutCommand, 0, cutCommand.Length);
                await networkStream.FlushAsync();
            }
            else
            {
                CutPaperWithDistance(printer, 1);
            }

            ShowSuccess($"sinov tugadi: codepage={codePage}, charset={charSet}, encoding={encodingName}");
        }
        catch (Exception ex)
        {
            ShowError($"sinov varag'ini chop etishda xato: {ex.Message}");
        }
        finally
        {
            CloseConnection();
        }
    }

    /// <summary>
    /// Updates printer settings once a good combination is found
    /// </summary>
    public void UpdateCyrillicSettings(int codePage, int charSet, string encodingName)
    {
        try
        {
            this.cyrillicCodePage = codePage;

            // Update app configuration or settings
            // (Implementation depends on how settings are stored)

            // Update current encoding
            try
            {
                printerEncoding = Encoding.GetEncoding(encodingName);
                System.Diagnostics.Debug.WriteLine($"updated to encoding: {encodingName}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"failed to set encoding {encodingName}: {ex.Message}");
            }

            ShowSuccess($"printer sozlamalari yangilandi: codepage={codePage}, charset={charSet}, encoding={encodingName}");
        }
        catch (Exception ex)
        {
            ShowError($"sozlamalarni yangilashda xato: {ex.Message}");
        }
    }

    /// <summary>
    /// Simple test that prints a sample with the most common Cyrillic code pages
    /// Uses simpler approach with fewer command combinations
    /// </summary>
    public async Task SimpleCyrillicTestAsync()
    {
        try
        {
            int connectionResult = await OpenPortAsync();
            if (connectionResult != 0)
            {
                ShowError("printer tayyor emas.");
                return;
            }

            // Most common Cyrillic code pages
            int[] commonCodePages = { 6, 7, 17 };

            // Test strings
            string testRussian = "Русский текст - Russian text";
            string testUzbek = "Сизнинг буюртмангиз - Your order";

            byte[] headerBytes = Encoding.ASCII.GetBytes("\n=== SIMPLE CYRILLIC TEST ===\n\n");
            PrintBytes(printer, headerBytes, headerBytes.Length, 0, 0);

            // Try each common code page
            foreach (int codePage in commonCodePages)
            {
                // Print header for this code page
                string pageHeader = $"\n--- CODE PAGE {codePage} ---\n";
                PrintText(printer, pageHeader, 0, 0);

                // Initialize and set code page
                byte[] initCmd = { 0x1B, 0x40 }; // ESC @
                SendCommand(initCmd);

                byte[] setCodePage = { 0x1B, 0x74, (byte)codePage }; // ESC t <n>
                SendCommand(setCodePage);

                // Try with Russian char set (10)
                byte[] setRussianCharSet = { 0x1B, 0x52, 0x0A }; // ESC R <n>
                SendCommand(setRussianCharSet);

                // Print test strings
                string charset10Header = "\nCharset 10 (Russian):\n";
                PrintText(printer, charset10Header, 0, 0);

                // Try different encodings
                string[] encodings = { "windows-1251", "cp866" };

                foreach (string encodingName in encodings)
                {
                    try
                    {
                        Encoding testEncoding = Encoding.GetEncoding(encodingName);
                        string encodingHeader = $"\nEncoding: {encodingName}\n";
                        PrintText(printer, encodingHeader, 0, 0);

                        byte[] russianBytes = testEncoding.GetBytes(testRussian + "\n");
                        byte[] uzbekBytes = testEncoding.GetBytes(testUzbek + "\n\n");

                        PrintBytes(printer, russianBytes, russianBytes.Length, 0, 0);
                        PrintBytes(printer, uzbekBytes, uzbekBytes.Length, 0, 0);
                    }
                    catch (Exception)
                    {
                        // Skip if encoding not available
                    }
                }
            }

            // Cut paper
            CutPaperWithDistance(printer, 1);

            ShowSuccess("simple cyrillic test completed");
        }
        catch (Exception ex)
        {
            ShowError($"sinov jarayonida xato: {ex.Message}");
        }
        finally
        {
            CloseConnection();
        }
    }

    /// <summary>
    /// Direct raw byte test method that bypasses encoding conversions
    /// Uses pre-encoded Cyrillic text for most reliable testing
    /// </summary>
    public async Task RawByteCyrillicTestAsync()
    {
        try
        {
            int connectionResult = await OpenPortAsync();
            if (connectionResult != 0)
            {
                ShowError("printer tayyor emas.");
                return;
            }

            // Initialize printer
            PrintText(printer, "\n\n=== RAW BYTE CYRILLIC TEST ===\n\n", 0, 0);

            // Predefined byte arrays for different code pages

            // CP866 (Code Page 17) encoded "Привет мир" (Hello World)
            byte[] cp866Hello = new byte[] {
                0x8F, 0xE0, 0xA8, 0xA2, 0xA5, 0xE2, 0x20, 0xAC, 0xA8, 0xE0 // "Привет мир" in CP866
            };

            // Windows-1251 (Code Page 6) encoded "Привет мир" 
            byte[] win1251Hello = new byte[] {
                0xcf, 0xf0, 0xe8, 0xe2, 0xe5, 0xf2, 0x20, 0xec, 0xe8, 0xf0 // "Привет мир" in Windows-1251
            };

            // CP866 (Code Page 17) encoded "Сизнинг буюртмангиз"
            byte[] cp866Order = new byte[] {
                0x91, 0xA8, 0xA7, 0xAD, 0xA8, 0xAD, 0xA3, 0x20, 0xA1, 0xE3,
                0xEE, 0xE0, 0xE2, 0xAC, 0xA0, 0xAD, 0xA3, 0xA8, 0xA7 // "Сизнинг буюртмангиз" in CP866
            };

            // Windows-1251 (Code Page 6) encoded "Сизнинг буюртмангиз"
            byte[] win1251Order = new byte[] {
                0xd1, 0xe8, 0xe7, 0xed, 0xe8, 0xed, 0xe3, 0x20, 0xe1, 0xf3,
                0xfe, 0xf0, 0xf2, 0xec, 0xe0, 0xed, 0xe3, 0xe8, 0xe7 // "Сизнинг буюртмангиз" in Windows-1251
            };

            // Test with Code Page 17 (CP866)
            byte[] initCmd = { 0x1B, 0x40 }; // Initialize printer
            byte[] cp17Cmd = { 0x1B, 0x74, 17 }; // Select code page 17 (CP866)
            byte[] charSet8 = { 0x1B, 0x52, 8 }; // International char set 8

            // Send commands
            SendCommand(initCmd);
            SendCommand(cp17Cmd);
            SendCommand(charSet8);

            // Print header
            PrintText(printer, "\n--- CODE PAGE 17 (CP866) ---\n", 0, 0);

            // Print raw CP866 text
            PrintText(printer, "CP866 Hello World: ", 0, 0);
            PrintBytes(printer, cp866Hello, cp866Hello.Length, 0, 0);

            PrintText(printer, "\nCP866 Order: ", 0, 0);
            PrintBytes(printer, cp866Order, cp866Order.Length, 0, 0);
            PrintText(printer, "\n\n", 0, 0);

            // Test with Code Page 6 (Windows-1251)
            byte[] cp6Cmd = { 0x1B, 0x74, 6 }; // Select code page 6 (Windows-1251)
            byte[] charSet10 = { 0x1B, 0x52, 10 }; // International char set 10

            // Send commands
            SendCommand(initCmd);
            SendCommand(cp6Cmd);
            SendCommand(charSet10);

            // Print header
            PrintText(printer, "\n--- CODE PAGE 6 (Windows-1251) ---\n", 0, 0);

            // Print raw Windows-1251 text
            PrintText(printer, "Win-1251 Hello World: ", 0, 0);
            PrintBytes(printer, win1251Hello, win1251Hello.Length, 0, 0);

            PrintText(printer, "\nWin-1251 Order: ", 0, 0);
            PrintBytes(printer, win1251Order, win1251Order.Length, 0, 0);
            PrintText(printer, "\n\n", 0, 0);

            // Try alternate method - code page switch in string
            PrintText(printer, "\n--- DIRECT ESCAPE SEQUENCES ---\n", 0, 0);

            // ESC @ ESC t <n> ESC R <n> inline in the string
            byte[] directBytes = new byte[] {
                0x1B, 0x40,                     // Initialize printer
                0x1B, 0x74, 17,                 // Select code page 17 (CP866)
                0x1B, 0x52, 8,                  // International char set
                0x8F, 0xE0, 0xA8, 0xA2, 0xA5, 0xE2, 0x20, 0xAC, 0xA8, 0xE0  // "Привет мир" in CP866
            };

            // Print direct command
            PrintBytes(printer, directBytes, directBytes.Length, 0, 0);
            PrintText(printer, "\n\n", 0, 0);

            // Try specific character sets from printer documentation
            for (byte i = 0; i <= 15; i++)
            {
                try
                {
                    SendCommand(initCmd);

                    // Try code page 17 with different character sets
                    SendCommand(cp17Cmd);

                    byte[] charSetCmd = { 0x1B, 0x52, i }; // Try charsets 0-15
                    SendCommand(charSetCmd);

                    PrintText(printer, $"\nCP17 + CharSet {i}: ", 0, 0);
                    PrintBytes(printer, cp866Hello, cp866Hello.Length, 0, 0);
                    PrintText(printer, "\n", 0, 0);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"error with charset {i}: {ex.Message}");
                }
            }

            // Cut the paper
            CutPaperWithDistance(printer, 1);

            ShowSuccess("raw byte test completed");
        }
        catch (Exception ex)
        {
            ShowError($"sinov jarayonida xato: {ex.Message}");
        }
        finally
        {
            CloseConnection();
        }
    }

    /// <summary>
    /// Basic Cyrillic print test that focuses on the most common working combinations for thermal printers
    /// Uses CP866 encoding (code page 17) with direct binary approach
    /// </summary>
    public async Task BasicCyrillicTestAsync()
    {
        try
        {
            int connectionResult = await OpenPortAsync();
            if (connectionResult != 0)
            {
                ShowError("printer tayyor emas.");
                return;
            }

            // Most printers require RESET at the start
            PrintText(printer, "\n\n", 0, 0);

            // Test header in ascii (guaranteed to work)
            PrintText(printer, "=== BASIC CYRILLIC TEST ===\n\n", 0, 0);

            // === APPROACH 1: CP866 (most common for thermal printers) ===

            // Reset printer
            byte[] resetCmd = { 0x1B, 0x40 }; // ESC @
            PrintBytes(printer, resetCmd, resetCmd.Length, 0, 0);

            // Approach 1: Hard-coded CP866 bytes for "Привет, мир!"
            PrintText(printer, "Method 1 - CP866 direct bytes:\n", 0, 0);

            // Select CP866 encoding (code page 17)
            byte[] cp866Cmd = { 0x1B, 0x74, 17 }; // ESC t 17
            PrintBytes(printer, cp866Cmd, cp866Cmd.Length, 0, 0);

            // Select Russia codepage (value 10)
            byte[] russiaCmd = { 0x1B, 0x52, 10 }; // ESC R 10
            PrintBytes(printer, russiaCmd, russiaCmd.Length, 0, 0);

            // CP866 encoded "Привет, мир!" (Hello, world!)
            byte[] helloWorld866 = new byte[] {
                0x8F, 0xE0, 0xA8, 0xA2, 0xA5, 0xE2, 0x2C, 0x20, 0xAC, 0xA8, 0xE0, 0x21
            };

            // CP866 encoded "Сизнинг буюртмангиз" (Your order)
            byte[] yourOrder866 = new byte[] {
                0x91, 0xA8, 0xA7, 0xAD, 0xA8, 0xAD, 0xA3, 0x20, 0xA1, 0xE3, 0xEE, 0xE0,
                0xE2, 0xAC, 0xA0, 0xAD, 0xA3, 0xA8, 0xA7
            };

            // Print the CP866 encoded text
            PrintBytes(printer, helloWorld866, helloWorld866.Length, 0, 0);
            PrintBytes(printer, new byte[] { 0x0A, 0x0A }, 2, 0, 0); // Two newlines
            PrintBytes(printer, yourOrder866, yourOrder866.Length, 0, 0);
            PrintBytes(printer, new byte[] { 0x0A, 0x0A }, 2, 0, 0); // Two newlines

            // === APPROACH 2: Windows-1251 (code page 6) ===

            // Reset printer
            PrintBytes(printer, resetCmd, resetCmd.Length, 0, 0);

            // Approach 2: Hard-coded Win-1251 bytes for "Привет, мир!"
            PrintText(printer, "Method 2 - Windows-1251 direct bytes:\n", 0, 0);

            // Select Windows-1251 encoding (code page 6)
            byte[] win1251Cmd = { 0x1B, 0x74, 6 }; // ESC t 6
            PrintBytes(printer, win1251Cmd, win1251Cmd.Length, 0, 0);

            // Windows-1251 encoded "Привет, мир!" (Hello, world!)
            byte[] helloWorld1251 = new byte[] {
                0xcf, 0xf0, 0xe8, 0xe2, 0xe5, 0xf2, 0x2c, 0x20, 0xec, 0xe8, 0xf0, 0x21
            };

            // Windows-1251 encoded "Сизнинг буюртмангиз" (Your order)
            byte[] yourOrder1251 = new byte[] {
                0xd1, 0xe8, 0xe7, 0xed, 0xe8, 0xed, 0xe3, 0x20, 0xe1, 0xf3, 0xfe, 0xf0,
                0xf2, 0xec, 0xe0, 0xed, 0xe3, 0xe8, 0xe7
            };

            // Print the Win-1251 encoded text
            PrintBytes(printer, helloWorld1251, helloWorld1251.Length, 0, 0);
            PrintBytes(printer, new byte[] { 0x0A, 0x0A }, 2, 0, 0); // Two newlines
            PrintBytes(printer, yourOrder1251, yourOrder1251.Length, 0, 0);
            PrintBytes(printer, new byte[] { 0x0A, 0x0A }, 2, 0, 0); // Two newlines

            // === APPROACH 3: Direct control with embedded font selection ===
            PrintText(printer, "Method 3 - Full control sequence:\n", 0, 0);

            // Reset printer
            PrintBytes(printer, resetCmd, resetCmd.Length, 0, 0);

            // Select CP866 encoding with specific font selection
            byte[] fullSequence = {
                0x1B, 0x40,       // Initialize printer
                0x1B, 0x4D, 0,    // Select Font A
                0x1B, 0x74, 17,   // Select code page 17 (CP866)
                0x1B, 0x52, 10,   // Select international char set 10 (Russia)
            };

            // Print control sequence
            PrintBytes(printer, fullSequence, fullSequence.Length, 0, 0);

            // Force using Font A (some printers need this)
            byte[] fontA = { 0x1B, 0x21, 0 }; // Normal size, Font A
            PrintBytes(printer, fontA, fontA.Length, 0, 0);

            // Print the CP866 encoded text
            PrintBytes(printer, helloWorld866, helloWorld866.Length, 0, 0);
            PrintBytes(printer, new byte[] { 0x0A, 0x0A }, 2, 0, 0); // Two newlines

            // Try with just Text method directly in CP866
            // This will check if the printer is now correctly initialized
            this.cyrillicCodePage = 17; // Set to CP866
            InitializeEncoding(); // Reinitialize with CP866 as priority

            PrintText(printer, "Method 4 - After CP866 + Font A init:\n", 0, 0);
            PrintText(printer, "Привет, Мир! - Сизнинг буюртмангиз\n\n", 0, 0);

            // Cut paper
            CutPaperWithDistance(printer, 1);

            // Show success message in GUI
            ShowSuccess("basic cyrillic test completed");
        }
        catch (Exception ex)
        {
            ShowError($"sinov jarayonida xato: {ex.Message}");
        }
        finally
        {
            CloseConnection();
        }
    }

    /// <summary>
    /// Sets the name of the local printer to use
    /// </summary>
    /// <param name="printerName">The name of the local printer</param>
    public void SetLocalPrinterName(string printerName)
    {
        if (string.IsNullOrEmpty(printerName))
            throw new ArgumentException("Printer nomi bo'sh bo'lishi mumkin emas", nameof(printerName));

        _localPrinterName = printerName;
        isEthernetMode = false;
        System.Diagnostics.Debug.WriteLine($"lokal printer nomi o'rnatildi: {printerName}");
    }

    /// <summary>
    /// Sets the connection timeout for network printers in milliseconds
    /// </summary>
    /// <param name="timeoutMs">Timeout in milliseconds</param>
    public void SetConnectionTimeout(int timeoutMs)
    {
        if (timeoutMs < 500) timeoutMs = 500; // Minimum 500ms
        if (timeoutMs > 30000) timeoutMs = 30000; // Maximum 30 seconds

        connectionTimeout = timeoutMs;
        System.Diagnostics.Debug.WriteLine($"printer ulanish vaqti o'rnatildi: {connectionTimeout}ms");
    }

    #endregion

    /// <summary>
    /// Prints a kitchen order using the current printer
    /// </summary>
    /// <param name="order">The kitchen order to print</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public async Task PrintKitchenOrderAsync(ContractorOrder order)
    {
        System.Diagnostics.Debug.WriteLine($"printkitchenorderasync boshlanmoqda: {order?.DocNumber ?? "null"}");

        try
        {
            if (order == null)
                throw new ArgumentNullException(nameof(order));

            string textToPrint = BuildKitchenPrintText(order);
            System.Diagnostics.Debug.WriteLine($"printkitchenorderasync: text uzunligi {textToPrint?.Length ?? 0} belgi");
            await ExecutePrintAsync(textToPrint, "Oshxona buyurtmasi muvaffaqiyatli chop etildi");
            System.Diagnostics.Debug.WriteLine("printkitchenorderasync muvaffaqiyatli yakunlandi");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"printkitchenorderasync xatolik: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"stack trace: {ex.StackTrace}");
            throw;
        }
    }
}