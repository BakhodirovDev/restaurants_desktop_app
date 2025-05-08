using System;

namespace Restaurants.Helper
{
    public static class AppSettings
    {
        // Default service fee percentage
        private static int _serviceFeePercentage = 10;

        // Property for service fee percentage
        public static int ServiceFeePercentage
        {
            get => _serviceFeePercentage;
            set
            {
                _serviceFeePercentage = value;
                // In a production app, we'd persist this to a file or registry
            }
        }

        // Format numbers with space as a thousand separator
        public static string FormatCurrency(decimal amount)
        {
            // Replace comma with space in the formatted number
            return string.Format("{0:N2}", amount).Replace(",", " ");
        }
    }
} 