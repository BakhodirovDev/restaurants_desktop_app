using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using Restaurants.Helper;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using System.Threading.Tasks;

namespace Restaurants.Pages.Windows
{
    public partial class ServiceFeeSettings : Window
    {
        // Regular expression to allow only numbers
        private static readonly Regex _numberRegex = new Regex("[^0-9]+");

        public ServiceFeeSettings()
        {
            InitializeComponent();
            
            // Add mouse drag capability since we have a custom window
            this.MouseLeftButtonDown += (s, e) => this.DragMove();
            
            // Load current service fee percentage from settings
            txtServiceFeePercentage.Text = AppSettings.ServiceFeePercentage.ToString();
        }

        // Allow only numbers in the TextBox
        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            e.Handled = _numberRegex.IsMatch(e.Text);
        }

        // Close the window
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // Cancel button click handler
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // Save button click handler
        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtServiceFeePercentage.Text))
            {
                MessageBox.Show("Iltimos, xizmat haqi foizini kiriting.", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (int.TryParse(txtServiceFeePercentage.Text, out int percentage))
            {
                if (percentage < 0 || percentage > 100)
                {
                    MessageBox.Show("Foiz qiymati 0 dan 100 gacha bo'lishi kerak.", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    // Show loading or disable controls
                    // Send update to the API
                    bool apiSuccess = await UpdateServiceFeePercentageAsync(percentage);
                    
                    if (apiSuccess)
                    {
                        // Save service fee percentage to local settings
                        AppSettings.ServiceFeePercentage = percentage;
                        
                        MessageBox.Show("Xizmat haqi foizi muvaffaqiyatli saqlandi.", "Ma'lumot", MessageBoxButton.OK, MessageBoxImage.Information);
                        DialogResult = true;
                        Close();
                    }
                    else
                    {
                        MessageBox.Show("Xizmat haqi foizini saqlashda xatolik yuz berdi. Iltimos, qayta urinib ko'ring.", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Xizmat haqi foizini saqlashda xatolik: {ex.Message}", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show("Noto'g'ri foiz qiymati kiritildi.", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        
        private async Task<bool> UpdateServiceFeePercentageAsync(int percentage)
        {
            try
            {
                // Get the current token
                string token = Restaurants.Settings.Default.AccessToken;
                if (string.IsNullOrEmpty(token))
                {
                    MessageBox.Show("Authentication token not found. Please login again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
                
                // Create API request data
                var requestData = new
                {
                    percentage = percentage,
                    amount = 0,
                    isAutoAdd = true,
                    code = "service_fee",
                    shortName = "Xizmat haqi",
                    fullName = "Xizmat haqi"
                };
                
                // Create HTTP client and set headers
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                    client.DefaultRequestHeaders.Add("accept", "text/plain");
                    
                    // Convert request data to JSON
                    string jsonRequest = JsonConvert.SerializeObject(requestData);
                    var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json-patch+json");
                    
                    // Send the request
                    HttpResponseMessage response = await client.PostAsync("https://crm-api.webase.uz/crm/AdditionalPayment/Create", content);
                    
                    // Return true if successful
                    return response.IsSuccessStatusCode;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating service fee: {ex.Message}");
                return false;
            }
        }
    }
} 