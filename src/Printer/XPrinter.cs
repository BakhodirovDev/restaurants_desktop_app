using Restaurants.Class.ContractorOrder_Get;
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
                MessageBox.Show("Chek muvaffaqiyatli chop etildi", "Xabar", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Kassa komandasini yuborishda xato: " + ex.Message, "Kassa Xato", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    public void PrintText(ContractorOrder printOrder)
    {
        string textToPrint = BuildKitchenPrintText(printOrder);

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
            MessageBox.Show("Kassa komandasini yuborishda xato: " + ex.Message, "Kassa Xato", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    private static string BuildPrintText(PrintOrder order)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"710 BUXORO KAFE\n");
        //sb.AppendLine("\x1D\x21\x00"); // Normal o'lcham

        // Order Details - Left aligned
        //sb.AppendLine("\x1B\x61\x00"); // Left align
        sb.AppendLine($"Zakaz N#: {order.CheckNumber}");
        sb.AppendLine($"Ofitsiant: {order.WaiterName}");
        sb.AppendLine($"Sana: {DateTime.ParseExact(order.OrderDate, "dd.MM.yyyy", null).ToString("dd.MM.yyyy")} " +
                      $" Vaqt: {DateTime.ParseExact(order.OrderTime, "HH:mm", null).ToString("HH:mm")}");

        sb.AppendLine($"Stol: {order.TableNumber}");
        sb.AppendLine();
        sb.AppendLine(new string('=', 48)); // 48 ta "="

        // Order items
        for (int i = 0; i < order.Orders.Count; i++)
        {
            var item = order.Orders[i];
            string sequenceNumber = (i + 1).ToString("D2");
            string productName = FormatProductName(item.ProductShortName, 25);
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
        string totalLabel = "Summa: ";
        string totalValue = $"{FormatAmount(order.TotalAmount)} UZS";
        int totalDots = 48 - totalLabel.Length - totalValue.Length;
        sb.AppendLine($"{totalLabel}{new string('.', totalDots)}{totalValue}");

        string serviceLabel = $"Xizmat haqi ({order.AdditionalPercentage}%): ";
        string serviceValue = $"{FormatAmount(order.ServiceFee)} UZS";
        int serviceDots = 48 - serviceLabel.Length - serviceValue.Length;
        sb.AppendLine($"{serviceLabel}{new string('.', serviceDots)}{serviceValue}");

        string paymentLabel = "To'lov turi: ";
        string paymentValue = $"{order.PaymentTypeText}";
        int paymentDots = 48 - paymentLabel.Length - paymentValue.Length;
        sb.AppendLine($"{paymentLabel}{new string('.', paymentDots)}{paymentValue}");

        sb.AppendLine(new string('=', 48)); // 48 ta "="

        string grandLabel = "Jami: ";
        string grandValue = $"{FormatAmount(order.GrandTotal)} UZS";
        int grandDots = 48 - grandLabel.Length - grandValue.Length;
        sb.AppendLine($"{grandLabel}{new string('.', grandDots)}{grandValue}");
        //sb.AppendLine("\x1D\x21\x00"); // Normal o'lcham

        sb.AppendLine("\nXaridingiz uchun rahmat!\n");


        return sb.ToString();
    }
    private static string BuildKitchenPrintText(ContractorOrder order)
    {
        var sb = new StringBuilder();

        // Header
        sb.AppendLine($"");
        sb.AppendLine($"YANGI BUYURTMA!");
        sb.AppendLine(new string('=', 48));

        // Order Information
        sb.AppendLine($"Zakaz N#: {order.DocNumber}");
        sb.AppendLine($"Ofitsiant: {order.Responsible}");
        sb.AppendLine($"Sana: {DateTime.Parse(order.DocDate).ToString("dd.MM.yyyy")} Vaqt: {DateTime.Parse(order.DocTime).ToString("HH:mm")}");

        // If you have a Tables property in your order, add this info
        if (order.Tables != null && order.Tables.Count > 0)
        {
            sb.AppendLine($"Stol: {string.Join(", ", order.Tables.Select(t => t.OrderNumber))}");
        }

        sb.AppendLine(new string('=', 48));

        // Order Items - formatted differently for kitchen staff
        sb.AppendLine("  TAOM NOMI                      MIQDORI");
        sb.AppendLine(new string('-', 48));

        // Since we're using ContractorOrderTable, let's iterate through Tables
        if (order.Tables != null)
        {
            for (int i = 0; i < order.Tables.Count; i++)
            {
                var item = order.Tables[i];
                string sequenceNumber = (i + 1).ToString("D2");
                string productName = FormatProductName(item.ProductShortName, 32);
                string quantity = item.Quantity.ToString("0.###");

                // Calculate padding to align quantities to the right
                int padding = 48 - productName.Length - quantity.Length;
                if (padding < 0) padding = 0;

                sb.AppendLine($"{sequenceNumber}. {productName}{new string(' ', padding)}{quantity}");

                // Add preparation instructions if available
                if (!string.IsNullOrEmpty(item.ContractorRequirement))
                {
                    sb.AppendLine($"   > {item.ContractorRequirement}");
                }

                if (!string.IsNullOrEmpty(item.Details))
                {
                    sb.AppendLine($"   > {item.Details}");
                }

                if (i < order.Tables.Count - 1)
                    sb.AppendLine(new string('-', 48));
            }
        }

        sb.AppendLine(new string('=', 48));

        // Add preparation notes for the kitchen
        sb.AppendLine($"Jami taomlar soni: {order.Tables?.Count ?? 0}");

        // Add current time of printing
        sb.AppendLine($"Chek chop etildi: {DateTime.Now.ToString("HH:mm:ss")}");

        // Add a cut command for the printer
        //sb.AppendLine("\x1D\x56\x01"); // Paper cut - Uncomment if your printer supports it

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
