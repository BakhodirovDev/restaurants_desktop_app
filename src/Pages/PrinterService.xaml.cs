using Microsoft.AspNetCore.SignalR.Client;
using Restaurants.Printer;
using System.Net.Http;
using System.Windows;

namespace Restaurants.Pages
{
    public partial class PrinterService : Window
    {
        private readonly HttpClient _httpClient;
        private readonly XPrinter _printer;
        private HubConnection _connection;

        public PrinterService(HttpClient httpClient, XPrinter printer)
        {
            _httpClient = httpClient;
            _printer = printer;

            InitializeComponent();
            ConnectToSignalR();
        }


        private async void ConnectToSignalR()
        {
            _connection = new HubConnectionBuilder()
                .WithUrl("wss://localhost:5000/notificationHub", options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult(Settings.Default.AccessToken);
                    options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets;
                })
                .WithAutomaticReconnect()
                .Build();

            _connection.On<string>("ReceiveNotification", message =>
            {
                MessageBox.Show($"Yangi xabar: {message}");
            });

            await StartConnection();
        }

        private async Task StartConnection()
        {
            try
            {
                await _connection.StartAsync();
                MessageBox.Show("WebSocket orqali SignalR ulanishi muvaffaqiyatli!");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"SignalR ulanish xatosi: {ex.Message}");
            }
        }


    }
}
