using Restaurants.Class;
using Restaurants.Printer;
using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Restaurants
{
    public partial class MainWindow : Window
    {
        private readonly HttpClient _httpClient;
        private readonly XPrinter _xPrinter;
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


        private IntPtr printer;
        private int openStatus = -100;
        public MainWindow()
        {
            _xPrinter = new XPrinter();
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://crm-api.webase.uz/"),
                Timeout = TimeSpan.FromSeconds(30) // Set a reasonable timeout
            };
            _httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            //this.printer = MainWindow.InitPrinter("");
            InitializeComponent();
            //InitializePrinter();
            AutoLogin();
        }


        public int openPort()
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

        private void InitializePrinter()
        {

            try
            {
                openPort();

                if(openStatus == 0)
                {
                    MainWindow.PrintText(this.printer, "------------------------------------------------\r\n", 0, 0);
                    MainWindow.FeedLine(this.printer, 1);
                    MainWindow.PrintText(this.printer, "------------------------------------------------\r\n", 0, 0);
                    MainWindow.FeedLine(this.printer, 1);
                    MainWindow.PrintText(this.printer, "------------------------------------------------\r\n", 0, 0);
                    MainWindow.FeedLine(this.printer, 1);
                    MainWindow.PrintText(this.printer, "------------------------------------------------\r\n", 0, 0);
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
                Console.WriteLine("Error sending ZPL: " + ex.Message);
            }
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            string username = UsernameTextBox.Text.Trim();
            string password = PasswordBox.Password.Trim();

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Please enter both username and password.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var loginRequest = new
            {
                username,
                password
            };

            string jsonRequest = JsonSerializer.Serialize(loginRequest);
            var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

            try
            {
                HttpResponseMessage response = await _httpClient.PostAsync("account/GenerateToken", content);

                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    var loginResponse = JsonSerializer.Deserialize<LoginResponse>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (loginResponse?.AccessToken == null)
                    {
                        MessageBox.Show("Login response invalid.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // Save tokens
                    Settings.Default.AccessToken = loginResponse.AccessToken;
                    Settings.Default.RefreshToken = loginResponse.RefreshToken;
                    Settings.Default.accessTokenExpireAt = loginResponse.AccessTokenExpireAt.ToString();
                    Settings.Default.refreshTokenExpireAt = loginResponse.RefreshTokenExpireAt.ToString();
                    Settings.Default.Save();

                    // Update HttpClient with the new token
                    _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginResponse.AccessToken);

                    Print print = new Print(_httpClient, _xPrinter);
                    print.Show();
                    Close();
                }
                else
                {
                    MessageBox.Show($"Login failed: {response.ReasonPhrase}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (HttpRequestException ex)
            {
                MessageBox.Show($"Network error: {ex.Message}", "Network Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unexpected error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task<string> GetTokenAsync()
        {
            string accessToken = Settings.Default.AccessToken;
            string refreshToken = Settings.Default.RefreshToken;

            if (!string.IsNullOrEmpty(accessToken))
                return accessToken;

            if (string.IsNullOrEmpty(refreshToken))
                return null;

            var refreshRequest = new { refreshToken };
            HttpResponseMessage response = await SendRequestAsync("account/RefreshToken", HttpMethod.Post, refreshRequest);

            if (response?.IsSuccessStatusCode == true)
            {
                string jsonResponse = await response.Content.ReadAsStringAsync();
                var newTokenResponse = JsonSerializer.Deserialize<LoginResponse>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (newTokenResponse?.AccessToken != null)
                {
                    Settings.Default.AccessToken = newTokenResponse.AccessToken;
                    Settings.Default.RefreshToken = newTokenResponse.RefreshToken;
                    Settings.Default.Save();
                    return newTokenResponse.AccessToken;
                }
            }

            return null;
        }

        private async Task<HttpResponseMessage> SendRequestAsync(string url, HttpMethod method, object requestBody = null)
        {
            //string token = await GetTokenAsync();
            string token = null;
            if (token == null)
            {
                MessageBox.Show("Session expired. Please log in again.", "Session Timeout", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }

            var request = new HttpRequestMessage(method, url)
            {
                Headers =
                {
                    Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token)
                }
            };

            if (requestBody != null)
            {
                string jsonRequest = JsonSerializer.Serialize(requestBody);
                request.Content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
            }

            HttpResponseMessage response = await _httpClient.SendAsync(request);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                token = await GetTokenAsync();
                if (token == null)
                    return response;

                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                response = await _httpClient.SendAsync(request);
            }

            return response;
        }

        private void AutoLogin()
        {
            string savedToken = Settings.Default.AccessToken;
            string expireAt = Settings.Default.accessTokenExpireAt;

            if (!string.IsNullOrEmpty(savedToken) && !string.IsNullOrEmpty(expireAt))
            {
                // Parse the expiration time (assuming it’s a Unix timestamp or ISO date string)
                if (IsTokenValid(expireAt))
                {
                    _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", savedToken);
                    Print print = new Print(_httpClient, _xPrinter);
                    print.Show();
                    Close();
                }
                else
                {
                    // Token expired, clear it and let user log in manually
                    Settings.Default.AccessToken = null;
                    Settings.Default.accessTokenExpireAt = null;
                    Settings.Default.Save();
                    MessageBox.Show("Your session has expired. Please log in again.", "Session Expired", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private bool IsTokenValid(string expireAt)
        {
            try
            {
                // Assuming expireAt is a Unix timestamp (seconds since epoch)
                if (long.TryParse(expireAt, out long unixTimestamp))
                {
                    DateTime expirationDate = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp).UtcDateTime;
                    return DateTime.UtcNow < expirationDate;
                }
                // Alternatively, if expireAt is an ISO date string (e.g., "2025-02-25T16:41:00Z")
                else if (DateTime.TryParse(expireAt, out DateTime expirationDateTime))
                {
                    return DateTime.UtcNow < expirationDateTime.ToUniversalTime();
                }
                // If parsing fails, assume invalid
                return false;
            }
            catch (Exception)
            {
                return false; // If any error occurs, treat as invalid
            }
        }
    }
}