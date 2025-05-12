using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using Restaurants.Helper;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using System.Threading.Tasks;
using Restaurants.Classes;

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
        
        async Task<bool> UpdateServiceFeePercentageAsync(int percentage)
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

                using (HttpClient client = new HttpClient())
                {
                    // Set headers
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                    client.DefaultRequestHeaders.Add("accept", "text/plain");

                    // First get current service fee
                    var requestData = new
                    {
                        search = (string)null,
                        sortBy = "id",
                        orderType = "DESC",
                        page = 0,
                        pageSize = 0
                    };

                    var content = new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json");
                    HttpResponseMessage response = await client.PostAsync("https://crm-api.webase.uz/crm/AdditionalPayment/GetList", content);

                    if (response.IsSuccessStatusCode)
                    {
                        string jsonResponse = await response.Content.ReadAsStringAsync();
                        var result = JsonConvert.DeserializeObject<AdditionalPaymentResponse>(jsonResponse);

                        if (result != null && result.Rows != null && result.Rows.Count > 0)
                        {
                            // Get first row as requested
                            var firstPayment = result.Rows[0];

                            // Delete existing service fee before updating
                            await DeleteExistingServiceFee(firstPayment.Id, token);

                            // Now create a new service fee with the new percentage
                            var newServiceFee = new
                            {
                                code = firstPayment.Code,
                                shortName = firstPayment.ShortName,
                                fullName = firstPayment.FullName,
                                percentage = percentage
                            };

                            var createContent = new StringContent(JsonConvert.SerializeObject(newServiceFee), Encoding.UTF8, "application/json");

                            // Create new service fee
                            HttpResponseMessage createResponse = await client.PostAsync("https://crm-api.webase.uz/crm/AdditionalPayment/Create", createContent);

                            if (createResponse.IsSuccessStatusCode)
                            {
                                // Update local settings
                                AppSettings.ServiceFeePercentage = percentage;
                                return true;
                            }
                            else
                            {
                                string errorContent = await createResponse.Content.ReadAsStringAsync();
                                return false;
                            }
                        }
                    }
                    else
                    {
                        string errorResponse = await response.Content.ReadAsStringAsync();
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                return false;
            }

            return false;
        }

        private async Task DeleteExistingServiceFee(int id, string token)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    // Set headers
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                    client.DefaultRequestHeaders.Add("accept", "text/plain");

                    // Send delete request
                    HttpResponseMessage response = await client.DeleteAsync($"https://crm-api.webase.uz/crm/AdditionalPayment/Delete/{id}");

                    if (!response.IsSuccessStatusCode)
                    {
                        string errorResponse = await response.Content.ReadAsStringAsync();
                    }
                }
            }
            catch (Exception ex)
            {
            }
        }
    }
} 