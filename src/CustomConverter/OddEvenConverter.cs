using System.Globalization;
using System.Windows.Data;

namespace Restaurants.CustomConverter
{
    public class OddEvenConverter : IValueConverter
    {
        public static readonly OddEvenConverter Instance = new OddEvenConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int index && parameter is string param)
            {
                bool isOdd = index % 2 != 0;
                if (param == "Odd") return isOdd;
                if (param == "Even") return !isOdd;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
