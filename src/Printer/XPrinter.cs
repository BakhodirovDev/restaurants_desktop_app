using System.Net.Http;
using System.Runtime.InteropServices;
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

    public void PrintText(string text)
    {

        try
        {
            OpenPort();

            if (openStatus == 0)
            {
                MainWindow.PrintText(this.printer, text, 0, 0);
                MainWindow.FeedLine(this.printer, 1);
                var result = MainWindow.CutPaperWithDistance(this.printer, 10);

                if (result == 0)
                {
                    MessageBox.Show("Urra");
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Error sending ZPL: " + ex.Message);
        }
    }

}
