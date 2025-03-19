using Microsoft.AspNetCore.SignalR.Client;
using Restaurants.Class.Printer;
using Restaurants.Printer;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
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
            string accessToken = Settings.Default.AccessToken;
            string userId = Settings.Default.UserId.ToString();
            string organizationId = Settings.Default.OrganizationId.ToString();

            _connection = new HubConnectionBuilder()
                .WithUrl($"ws://crm-api.webase.uz/ws/restarunt", options =>
                {
                    options.AccessTokenProvider = async () => await Task.FromResult(accessToken);
                    options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets;

                    options.Headers["UserId"] = userId;
                    options.Headers["OrganizationId"] = organizationId;
                })
                .WithAutomaticReconnect()
                .Build();

            _connection.On<string>("ReceiveNotification", message =>
            {
                MessageBox.Show($"Yangi xabar: {message}");
            });

            _connection.On<object>("OrderData", message =>
            {
/*
                var data = JsonSerializer.Deserialize<>(message);


                PrintOrder order = message.ToObject<PrintOrder>();
                _printer.PrintOrder(order);*/
            });

            _connection.On("Ping", async () =>
            {
                await SendPong();
            });

            await StartConnection();
        }

        private async Task SendPong()
        {
            if (_connection.State == HubConnectionState.Connected)
            {
                await _connection.SendAsync("Pong");
            }
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
