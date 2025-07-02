using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Restaurants.Pages.Windows
{
    public partial class PaymentTypes : Window
    {
        private bool _isDragging;
        private Point _startMousePosition;
        private Point _startWindowPosition;

        // Tanlangan to'lov turini saqlash uchun xususiyat
        public PaymentMethod SelectedPaymentMethod { get; private set; }

        public PaymentTypes()
        {
            InitializeComponent();
            LoadPaymentMethodsAsync();
        }

        private async Task LoadPaymentMethodsAsync()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string apiUrl = "https://crm-api.webase.uz/mmv/Manual/PaymentTypeSelectList";
                    client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                    client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/plain"));
                    client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("*/*"));
                    client.DefaultRequestHeaders.Add("accept-language", "ru-RU,ru;q=0.9,uz-UZ;q=0.8,uz;q=0.7,en-US;q=0.6,en;q=0.5");
                    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Settings.Default.AccessToken);
                    client.DefaultRequestHeaders.Add("origin", "https://crm.webase.uz");
                    client.DefaultRequestHeaders.Add("priority", "u=1, i");
                    client.DefaultRequestHeaders.Add("referer", "https://crm.webase.uz/");
                    client.DefaultRequestHeaders.Add("sec-ch-ua", "\"Not(A:Brand\";v=\"99\", \"Google Chrome\";v=\"133\", \"Chromium\";v=\"133\"");
                    client.DefaultRequestHeaders.Add("sec-ch-ua-mobile", "?0");
                    client.DefaultRequestHeaders.Add("sec-ch-ua-platform", "\"Windows\"");
                    client.DefaultRequestHeaders.Add("sec-fetch-dest", "empty");
                    client.DefaultRequestHeaders.Add("sec-fetch-mode", "cors");
                    client.DefaultRequestHeaders.Add("sec-fetch-site", "same-site");
                    client.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/133.0.0.0 Safari/537.36");

                    HttpResponseMessage response = await client.GetAsync(apiUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        var paymentMethods = await response.Content.ReadFromJsonAsync<List<PaymentMethod>>();
                        for (int i = 0; i < paymentMethods.Count; i++)
                        {
                            paymentMethods[i].Index = i;
                        }
                        PaymentMethodsPanel.ItemsSource = paymentMethods;
                    }
                    else
                    {
                        MessageBox.Show("API dan ma'lumot olishda xatolik yuz berdi.", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Xatolik: {ex.Message}", "Xatolik", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void PaymentButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is PaymentMethod paymentMethod)
            {
                SelectedPaymentMethod = paymentMethod; // Tanlangan to'lov turini saqlash
                this.DialogResult = true; // Oynani yopish va natijani qaytarish
                this.Close();
            }
        }

        private void Header_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _startMousePosition = PointToScreen(e.GetPosition(this));
            _startWindowPosition = new Point(this.Left, this.Top);
            (sender as UIElement)?.CaptureMouse();
            e.Handled = true;
        }

        private void Header_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && Mouse.RightButton == MouseButtonState.Pressed)
            {
                Point currentMousePosition = PointToScreen(e.GetPosition(this));
                double offsetX = currentMousePosition.X - _startMousePosition.X;
                double offsetY = currentMousePosition.Y - _startMousePosition.Y;

                this.Left = _startWindowPosition.X + offsetX;
                this.Top = _startWindowPosition.Y + offsetY;
            }
        }

        private void Header_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                (sender as UIElement)?.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        public class PaymentMethod
        {
            public int Value { get; set; }
            public string Text { get; set; }
            public int Index { get; set; }
        }
    }
}