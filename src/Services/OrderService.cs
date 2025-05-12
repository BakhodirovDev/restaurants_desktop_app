using Newtonsoft.Json;
using Restaurants.Class.ContractorOrder_Get;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Restaurants.Services
{
    public class OrderService
    {
        private readonly HttpClient _httpClient;
        
        public OrderService(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }
        
        /// <summary>
        /// Marks a product as defective and removes it from the order
        /// </summary>
        public async Task<ContractorOrder> MarkProductAsDefectiveAsync(
            ContractorOrder order, 
            int itemId, 
            decimal defectiveQuantity, 
            string token)
        {
            if (order == null)
                throw new ArgumentNullException(nameof(order));
                
            if (string.IsNullOrEmpty(token))
                throw new ArgumentException("Valid token is required", nameof(token));
                
            // Find the item in the order
            var item = order.Tables?.FirstOrDefault(t => t.Id == itemId);
            if (item == null)
                throw new ArgumentException($"Item with ID {itemId} not found in order", nameof(itemId));
                
            // Create a copy of the order without the defective product for the update
            var orderToUpdate = new
            {
                id = order.Id,
                statusId = order.StatusId,
                docNumber = order.DocNumber,
                docDate = order.DocDate,
                docTime = order.DocTime,
                firstContactId = order.FirstContactId,
                contact = order.Contact,
                clientName = order.ClientName,
                startDate = order.StartDate,
                estimatedEndDate = order.EstimatedEndDate,
                endDate = order.EndDate,
                estimatedPaymentTypeId = order.EstimatedPaymentTypeId,
                responsibleId = order.ResponsibleId,
                currencyId = order.CurrencyId,
                isForManReport = order.IsForManReport,
                organizationAreasOfActivityId = order.OrganizationAreasOfActivityId,
                ctWarehouseId = order.CtWarehouseId,
                contractorId = order.ContractorId,
                isCreateManufacturingReport = order.IsCreateManufacturingReport,
                details = order.Details,
                tables = (from t in order.Tables
                         where t.Id != itemId || defectiveQuantity < t.Quantity
                         select new
                         {
                             id = t.Id,
                             orderNumber = t.OrderNumber,
                             productId = t.ProductId,
                             contractorRequirement = t.ContractorRequirement,
                             estimatedPrice = t.EstimatedPrice,
                             quantity = t.Id == itemId ? (t.Quantity - defectiveQuantity) : t.Quantity,
                             defectedQuantity = t.Id == itemId ? defectiveQuantity : t.DefectedQuantity,
                             amount = t.Id == itemId ? (t.EstimatedPrice * (t.Quantity - defectiveQuantity)) : t.Amount,
                             defectedAmount = t.Id == itemId ? (t.EstimatedPrice * defectiveQuantity) : t.DefectedAmount,
                             details = t.Details,
                             responsibleId = t.ResponsibleId,
                             ctWarehouseId = t.CtWarehouseId
                         }).ToList(),
                additionalPayments = order.AdditionalPayments?.Select(p => new
                {
                    id = p.Id,
                    orderNumber = p.OrderNumber,
                    additionalPaymentId = p.AdditionalPaymentId,
                    amount = p.Amount,
                    details = p.Details
                }).ToList()
            };

            // Send API request to update order
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                client.DefaultRequestHeaders.Add("accept", "*/*");

                var content = new StringContent(
                    JsonConvert.SerializeObject(orderToUpdate),
                    Encoding.UTF8,
                    "application/json-patch+json");

                HttpResponseMessage response = await client.PostAsync(
                    "https://crm-api.webase.uz/crm/ContractorOrder/Update",
                    content);

                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    var updatedOrder = JsonConvert.DeserializeObject<ContractorOrder>(jsonResponse);
                    
                    // Immediately get fresh data from the server to ensure everything is updated
                    var refreshedOrder = await GetOrderByIdAsync(updatedOrder.Id, token);
                    return refreshedOrder ?? updatedOrder;
                }
                else
                {
                    // Handle error response
                    string errorResponse = await response.Content.ReadAsStringAsync();
                    throw new HttpRequestException($"API error: {response.StatusCode}\n{errorResponse}");
                }
            }
        }
        
        /// <summary>
        /// Gets the latest order data from the server by ID
        /// </summary>
        public async Task<ContractorOrder> GetOrderByIdAsync(int orderId, string token)
        {
            if (orderId <= 0)
                throw new ArgumentException("Valid order ID is required", nameof(orderId));
                
            if (string.IsNullOrEmpty(token))
                throw new ArgumentException("Valid token is required", nameof(token));
                
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                    client.DefaultRequestHeaders.Add("accept", "text/plain");
                    
                    HttpResponseMessage response = await client.GetAsync(
                        $"https://crm-api.webase.uz/crm/ContractorOrder/Get/{orderId}");
                        
                    if (response.IsSuccessStatusCode)
                    {
                        string jsonResponse = await response.Content.ReadAsStringAsync();
                        return JsonConvert.DeserializeObject<ContractorOrder>(jsonResponse);
                    }
                    
                    // Return null if we couldn't get the refreshed data
                    return null;
                }
            }
            catch
            {
                // If refreshing fails, we'll just return null and use the original updated order
                return null;
            }
        }
    }
} 