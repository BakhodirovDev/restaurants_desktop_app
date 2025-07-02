using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Restaurants.Converters
{
    public class IndexToGradientConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // Agar values uzunligi 6 bo'lmasa yoki birinchi qiymat int bo'lmasa, fallback qaytarish
            if (values.Length != 6 || !(values[0] is int index))
            {
                return Brushes.Transparent;
            }

            // Gradient brushlarni values dan olish
            var brushes = new[]
            {
                values[1] as Brush, // Button1Brush
                values[2] as Brush, // Button2Brush
                values[3] as Brush, // Button3Brush
                values[4] as Brush, // Button4Brush
                values[5] as Brush  // Button5Brush
            };

            // Brushlarning hammasi mavjudligini tekshirish
            if (brushes.Any(b => b == null))
            {
                return Brushes.Transparent;
            }

            // Indeks bo'yicha brushni qaytarish (5 ta brush orasida aylanish)
            return brushes[index % 5];
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}