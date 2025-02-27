using Restaurants.Class.Printer;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

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

    private string BuildPrintText(PrintOrder order)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Zakaz N#: {order.CheckNumber}");
        sb.AppendLine($"Restoran: {order.RestaurantName}");
        sb.AppendLine($"Ofitsiant: {order.WaiterName}");
        sb.AppendLine($"Sana: {order.OrderDate}   Vaqt: {order.OrderTime}");
        sb.AppendLine($"Stol: {order.TableNumber}");
        sb.AppendLine(new string('-', 40));

        sb.AppendLine($"Mahsulot    |    Soni    |    Summa");
        sb.AppendLine(new string('-', 40));

        foreach (var item in order.Orders)
        {
            // Format Amount to one decimal place
            string amountFormatted = Math.Round(item.Amount, 1).ToString("0.0").PadLeft(8);
            sb.AppendLine($"{item.ProductShortName.PadRight(12)} | {item.Quantity.ToString().PadLeft(8)} | {amountFormatted} UZS");
        }

        sb.AppendLine(new string('-', 40));
        // Format totals to one decimal place
        string totalFormatted = Math.Round(order.TotalAmount, 1).ToString("0.0").PadLeft(26);
        string serviceFeeFormatted = Math.Round(order.ServiceFee, 1).ToString("0.0").PadLeft(20);
        string grandTotalFormatted = Math.Round(order.GrandTotal, 1).ToString("0.0").PadLeft(27);
        sb.AppendLine($"Summa: {totalFormatted} UZS");
        sb.AppendLine($"Xizmat haqi: {serviceFeeFormatted} UZS");
        sb.AppendLine(new string('-', 40));
        sb.AppendLine($"Jami: {grandTotalFormatted} UZS");

        return sb.ToString();
    }

}
