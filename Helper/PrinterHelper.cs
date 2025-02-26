using System.Runtime.InteropServices;
using System.Text;

namespace Restaurants.Helper
{
    public class PrinterHelper
    {
        [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool OpenPrinter(string src, out IntPtr hPrinter, IntPtr pd);

        [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool WritePrinter(IntPtr hPrinter, byte[] buffer, int bufSize, out int bytesWritten);

        public static bool SendStringToPrinter(string printerName, string data)
        {
            IntPtr hPrinter;
            if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero)) return false;

            byte[] bytes = Encoding.UTF8.GetBytes(data);
            bool success = WritePrinter(hPrinter, bytes, bytes.Length, out _);
            ClosePrinter(hPrinter);
            return success;
        }
    }
}
