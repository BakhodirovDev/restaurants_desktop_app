using Restaurants.Class.Printer;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Restaurants.Printer;

public class XPrinter
{
    private IntPtr printer;
    private int openStatus = -100;

    public XPrinter()
    {
        this.printer = MainWindow.InitPrinter("");

    }

    #region Importing dll methods
    [DllImport("printer.sdk.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.StdCall)]
    public static extern IntPtr InitPrinter(string model);
    
    [DllImport("printer.sdk.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.StdCall)]
    public static extern int OpenPort(IntPtr intPtr, string port);
    
    [DllImport("printer.sdk.dll", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
    public static extern int PrintText(IntPtr intPtr, string data, int alignment, int textSize);
    
    [DllImport("printer.sdk.dll", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
    public static extern int FeedLine(IntPtr intPtr, int lines);
    
    [DllImport("printer.sdk.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.StdCall)]
    public static extern int ClosePort(IntPtr intPtr);

    [DllImport("printer.sdk.dll", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
    public static extern int CutPaperWithDistance(IntPtr intPtr, int distance);
    #endregion

    private int OpenPort()
    {
        try
        {
            if (this.openStatus == 0)
                return this.openStatus;
            this.openStatus = MainWindow.OpenPort(this.printer, "USB," + "USB001");

            return this.openStatus;
        }
        catch
        {
            throw;
        }
    }

    public void PrintText(PrintOrder printOrder)
    {
        string textToPrint = BuildPrintText(printOrder);

        try
        {
            OpenPort();

            if (openStatus != 0)
            {
                MessageBox.Show("Printer tayyor emas.", "Xato", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Matnni printerga yuboramiz
            MainWindow.PrintText(this.printer, textToPrint, 0, 0);
            MainWindow.FeedLine(this.printer, 1);

            // Qog'ozni kesish amali bajariladi
            int cutResult = MainWindow.CutPaperWithDistance(this.printer, 10);
            if (cutResult == 0)
            {
                MessageBox.Show("Bosib chiqarish muvaffaqiyatli yakunlandi.", "Ma'lumot", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Print komandasini yuborishda xato: " + ex.Message, "Print Xato", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string BuildPrintText(PrintOrder order)
    {
        var sb = new StringBuilder();

        // Shrfitni standart holatga qaytarish
        //sb.AppendLine("\x1B\x21\x00");

        // Header Section - Katta shrift
        //sb.AppendLine("\x1B\x61\x01"); // Center align
        //sb.AppendLine("\x1D\x21\x11"); // 2x kenglik va 2x balandlik
        sb.AppendLine($"{order.RestaurantName}");
        //sb.AppendLine("\x1D\x21\x00"); // Normal o'lcham

        // Order Details - Left aligned
        //sb.AppendLine("\x1B\x61\x00"); // Left align
        sb.AppendLine($"Zakaz N#:{order.CheckNumber}");
        sb.AppendLine($"Ofitsiant:{order.WaiterName}");
        sb.AppendLine($"Sana:{DateTime.Parse(order.OrderDate).ToString("dd.MM.yyyy")} Vaqt:{DateTime.Parse(order.OrderTime).ToString("HH:mm")}");
        sb.AppendLine($"Stol:{order.TableNumber}");
        sb.AppendLine();
        sb.AppendLine(new string('=', 48)); // 48 ta "="

        // Order items
        for (int i = 0; i < order.Orders.Count; i++)
        {
            var item = order.Orders[i];
            string sequenceNumber = (i + 1).ToString("D2");
            string productName = FormatProductName(item.ProductShortName, 20);
            string calc = $"{item.Quantity} * {FormatAmount(item.EstimatedPrice)} = {FormatAmount(item.Amount)}";
            int padding = 48 - productName.Length - calc.Length;
            if (padding < 0) padding = 0;
            sb.AppendLine($"{productName}{new string(' ', padding)}{calc}");

            if (i < order.Orders.Count - 1)
                sb.AppendLine(new string('-', 48)); // 48 ta "-"
        }

        sb.AppendLine(new string('=', 48)); // 48 ta "="
        sb.AppendLine();

        // Totals - Katta shrift va bo'shliqsiz
        //sb.AppendLine("\x1D\x21\x10"); // 2x kenglik
        string totalLabel = "Summa:";
        string totalValue = $"{FormatAmount(order.TotalAmount)} UZS";
        int totalDots = 48 - totalLabel.Length - totalValue.Length;
        sb.AppendLine($"{totalLabel}{new string('.', totalDots)}{totalValue}");

        string serviceLabel = "Xizmat haqi:";
        string serviceValue = $"{FormatAmount(order.ServiceFee)} UZS";
        int serviceDots = 48 - serviceLabel.Length - serviceValue.Length;
        sb.AppendLine($"{serviceLabel}{new string('.', serviceDots)}{serviceValue}");

        sb.AppendLine(new string('=', 48)); // 48 ta "="

        string grandLabel = "Jami:";
        string grandValue = $"{FormatAmount(order.GrandTotal)} UZS";
        int grandDots = 48 - grandLabel.Length - grandValue.Length;
        sb.AppendLine($"{grandLabel}{new string('.', grandDots)}{grandValue}");
        //sb.AppendLine("\x1D\x21\x00"); // Normal o'lcham

        // Footer - Centered
        //sb.AppendLine("\x1B\x61\x01"); // Center align
        sb.AppendLine("\nXaridingiz uchun rahmat!\n");

        // Add cut command
        //sb.AppendLine("\x1D\x56\x01"); // Paper cut

        return sb.ToString();
    }

    private static string FormatProductName(string name, int maxLength)
    {
        if (string.IsNullOrEmpty(name))
            return "".PadRight(maxLength);
        if (name.Length <= maxLength)
            return name.PadRight(maxLength);
        return name.Substring(0, maxLength - 3) + "...";
    }

    private static string FormatAmount(decimal amount)
    {
        var s = (int)amount;
        return s.ToString("N0", new NumberFormatInfo()
        {
            NumberGroupSizes = new[] { 3 },
            NumberGroupSeparator = "."
        });
    }

}
