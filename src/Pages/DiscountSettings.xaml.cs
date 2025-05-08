using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Restaurants.Classes;
using Restaurants.Helper;

namespace Restaurants.Pages
{
    public partial class DiscountSettings : Window
    {
        public bool IsPercentage { get; private set; } = true;
        public decimal DiscountPercentage { get; private set; }
        public decimal DiscountAmount { get; private set; }
        public decimal TotalAmount { get; set; }

        public bool DiscountApplied { get; private set; }

        public DiscountSettings(decimal totalAmount, decimal currentDiscountAmount = 0, bool isPercentage = true)
        {
            InitializeComponent();
            
            TotalAmount = totalAmount;
            DiscountApplied = currentDiscountAmount > 0;
            
            // Setup Loaded event handler to initialize UI after all controls are ready
            this.Loaded += (s, e) => 
            {
                if (DiscountApplied)
                {
                    if (isPercentage)
                    {
                        DiscountPercentage = Math.Round(currentDiscountAmount / totalAmount * 100, 2);
                        txtDiscountPercentage.Text = DiscountPercentage.ToString();
                        rbPercentage.IsChecked = true;
                    }
                    else
                    {
                        DiscountAmount = currentDiscountAmount;
                        txtDiscountAmount.Text = DiscountAmount.ToString();
                        rbAmount.IsChecked = true;
                    }
                }
                
                UpdateDiscountPreview();
            };
        }

        private void DiscountType_Checked(object sender, RoutedEventArgs e)
        {
            IsPercentage = rbPercentage.IsChecked ?? true;
            UpdateDiscountPreview();
        }

        private void NumericTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Only allow digits and one decimal point
            Regex regex = new Regex(@"^[0-9]*(?:\.[0-9]*)?$");
            string text = ((TextBox)sender).Text + e.Text;
            e.Handled = !regex.IsMatch(text);
        }

        private void PercentageTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            // Auto-select the percentage radio button when the percentage textbox gets focus
            rbPercentage.IsChecked = true;
        }

        private void AmountTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            // Auto-select the amount radio button when the amount textbox gets focus
            rbAmount.IsChecked = true;
        }

        private void UpdateDiscountPreview()
        {
            if (txtDiscountPercentage == null || txtDiscountAmount == null || lblDiscountPreview == null)
                return;
                
            decimal discountValue = 0;
            
            if (IsPercentage)
            {
                if (!string.IsNullOrEmpty(txtDiscountPercentage.Text) && 
                    decimal.TryParse(txtDiscountPercentage.Text.Replace(',', '.'), out decimal percentage))
                {
                    discountValue = TotalAmount * percentage / 100;
                    DiscountPercentage = percentage;
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(txtDiscountAmount.Text) && 
                    decimal.TryParse(txtDiscountAmount.Text.Replace(',', '.'), out decimal amount))
                {
                    discountValue = amount;
                    DiscountAmount = amount;
                }
            }
            
            lblDiscountPreview.Text = AppSettings.FormatCurrency(discountValue) + " so'm";
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            // Validate input
            if (IsPercentage)
            {
                if (string.IsNullOrWhiteSpace(txtDiscountPercentage.Text) ||
                    !decimal.TryParse(txtDiscountPercentage.Text.Replace(',', '.'), out decimal percentage))
                {
                    MessageBox.Show("Iltimos, to'g'ri foiz miqdorini kiriting!", "Xato", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                DiscountPercentage = percentage;
                DiscountAmount = TotalAmount * percentage / 100;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(txtDiscountAmount.Text) ||
                    !decimal.TryParse(txtDiscountAmount.Text.Replace(',', '.'), out decimal amount))
                {
                    MessageBox.Show("Iltimos, to'g'ri chegirma summasini kiriting!", "Xato", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                DiscountAmount = amount;
                
                // Validate that discount is not greater than total
                if (DiscountAmount > TotalAmount)
                {
                    MessageBox.Show("Chegirma jami summadan ko'p bo'lishi mumkin emas!", "Xato", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            
            DiscountApplied = true;
            DialogResult = true;
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void btnResetDiscount_Click(object sender, RoutedEventArgs e)
        {
            DiscountApplied = false;
            DiscountAmount = 0;
            DiscountPercentage = 0;
            DialogResult = true;
        }
    }
} 