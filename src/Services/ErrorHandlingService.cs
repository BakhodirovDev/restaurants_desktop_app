using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Restaurants.Services
{
    public class ErrorHandlingService
    {
        private static readonly object _lockObject = new object();
        private static readonly Dictionary<string, DateTime> _shownErrors = new Dictionary<string, DateTime>();
        private static readonly TimeSpan _errorCooldown = TimeSpan.FromMinutes(5); // 5 minutes cooldown

        /// <summary>
        /// Show error message only once within cooldown period
        /// </summary>
        public static void ShowErrorOnce(string errorKey, string message, string title = "Xatolik")
        {
            lock (_lockObject)
            {
                var now = DateTime.Now;
                
                // Clean up old errors
                var expiredKeys = _shownErrors
                    .Where(kvp => now - kvp.Value > _errorCooldown)
                    .Select(kvp => kvp.Key)
                    .ToList();
                
                foreach (var key in expiredKeys)
                {
                    _shownErrors.Remove(key);
                }
                
                // Check if this error was already shown recently
                if (_shownErrors.ContainsKey(errorKey))
                {
                    System.Diagnostics.Debug.WriteLine($"Error suppressed (already shown): {errorKey}");
                    return;
                }
                
                // Show the error and record it
                _shownErrors[errorKey] = now;
                
                try
                {
                    // Use dispatcher to ensure UI thread access
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error showing message box: {ex.Message}");
                }
                
                System.Diagnostics.Debug.WriteLine($"Error shown: {errorKey} - {message}");
            }
        }

        /// <summary>
        /// Show warning message only once within cooldown period
        /// </summary>
        public static void ShowWarningOnce(string warningKey, string message, string title = "Ogohlantirish")
        {
            lock (_lockObject)
            {
                var now = DateTime.Now;
                
                // Check if this warning was already shown recently
                if (_shownErrors.ContainsKey(warningKey))
                {
                    System.Diagnostics.Debug.WriteLine($"Warning suppressed (already shown): {warningKey}");
                    return;
                }
                
                // Show the warning and record it
                _shownErrors[warningKey] = now;
                
                try
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error showing warning box: {ex.Message}");
                }
                
                System.Diagnostics.Debug.WriteLine($"Warning shown: {warningKey} - {message}");
            }
        }

        /// <summary>
        /// Show info message only once within cooldown period
        /// </summary>
        public static void ShowInfoOnce(string infoKey, string message, string title = "Ma'lumot")
        {
            lock (_lockObject)
            {
                var now = DateTime.Now;
                
                // Check if this info was already shown recently
                if (_shownErrors.ContainsKey(infoKey))
                {
                    System.Diagnostics.Debug.WriteLine($"Info suppressed (already shown): {infoKey}");
                    return;
                }
                
                // Show the info and record it
                _shownErrors[infoKey] = now;
                
                try
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error showing info box: {ex.Message}");
                }
                
                System.Diagnostics.Debug.WriteLine($"Info shown: {infoKey} - {message}");
            }
        }

        /// <summary>
        /// Log error without showing message box
        /// </summary>
        public static void LogError(string context, Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in {context}: {ex.Message}");
            if (ex.InnerException != null)
            {
                System.Diagnostics.Debug.WriteLine($"Inner exception: {ex.InnerException.Message}");
            }
        }

        /// <summary>
        /// Clear all error history (for testing or reset)
        /// </summary>
        public static void ClearErrorHistory()
        {
            lock (_lockObject)
            {
                _shownErrors.Clear();
                System.Diagnostics.Debug.WriteLine("Error history cleared");
            }
        }
    }
} 