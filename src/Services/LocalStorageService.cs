using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Restaurants.Class.ContractorOrder_Get;

namespace Restaurants.Services
{
    public class LocalStorageService
    {
        private readonly string _localDataPath;
        private Dictionary<int, ContractorOrder> _localOrders;
        private readonly object _lockObject = new object();

        public LocalStorageService()
        {
            _localDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "local_orders.json");
            _localOrders = new Dictionary<int, ContractorOrder>();
            LoadLocalOrders();
        }

        /// <summary>
        /// Load orders from local storage
        /// </summary>
        private void LoadLocalOrders()
        {
            try
            {
                if (File.Exists(_localDataPath))
                {
                    string json = File.ReadAllText(_localDataPath);
                    var orders = JsonSerializer.Deserialize<List<ContractorOrder>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    
                    if (orders != null)
                    {
                        _localOrders = orders.ToDictionary(o => o.Id, o => o);
                        System.Diagnostics.Debug.WriteLine($"Local orders loaded: {_localOrders.Count} orders");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading local orders: {ex.Message}");
                _localOrders = new Dictionary<int, ContractorOrder>();
            }
        }

        /// <summary>
        /// Save orders to local storage
        /// </summary>
        private void SaveLocalOrders()
        {
            try
            {
                lock (_lockObject)
                {
                    string json = JsonSerializer.Serialize(_localOrders.Values.ToList(), new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_localDataPath, json);
                    System.Diagnostics.Debug.WriteLine($"Local orders saved: {_localOrders.Count} orders");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving local orders: {ex.Message}");
            }
        }

        /// <summary>
        /// Compare server data with local data and return new/updated/deleted orders
        /// </summary>
        public LocalDataComparison CompareWithServerData(List<ContractorOrder> serverOrders)
        {
            var comparison = new LocalDataComparison();
            
            lock (_lockObject)
            {
                var serverOrderDict = serverOrders?.ToDictionary(o => o.Id, o => o) ?? new Dictionary<int, ContractorOrder>();
                
                // Find new orders (exist in server but not in local)
                foreach (var serverOrder in serverOrderDict.Values)
                {
                    if (!_localOrders.ContainsKey(serverOrder.Id))
                    {
                        comparison.NewOrders.Add(serverOrder);
                        System.Diagnostics.Debug.WriteLine($"New order detected: {serverOrder.Id}");
                    }
                    else
                    {
                        // Check for new items in existing orders
                        var localOrder = _localOrders[serverOrder.Id];
                        var newItems = FindNewItemsInOrder(localOrder, serverOrder);
                        if (newItems.Any())
                        {
                            comparison.NewItems.AddRange(newItems.Select(item => new OrderItemChange
                            {
                                OrderId = serverOrder.Id,
                                Item = item,
                                TableNumber = item.OrderNumber
                            }));
                            System.Diagnostics.Debug.WriteLine($"New items detected in order {serverOrder.Id}: {newItems.Count} items");
                        }
                    }
                }
                
                // Find deleted orders (exist in local but not in server)
                foreach (var localOrderId in _localOrders.Keys.ToList())
                {
                    if (!serverOrderDict.ContainsKey(localOrderId))
                    {
                        comparison.DeletedOrderIds.Add(localOrderId);
                        System.Diagnostics.Debug.WriteLine($"Deleted order detected: {localOrderId}");
                    }
                }
                
                // Update local storage with server data
                _localOrders = serverOrderDict;
                SaveLocalOrders();
            }
            
            return comparison;
        }

        /// <summary>
        /// Find new items in an order by comparing with local version
        /// </summary>
        private List<ContractorOrderTable> FindNewItemsInOrder(ContractorOrder localOrder, ContractorOrder serverOrder)
        {
            var newItems = new List<ContractorOrderTable>();
            
            if (serverOrder.Tables == null) return newItems;
            
            var localItems = new Dictionary<int, ContractorOrderTable>();
            if (localOrder.Tables != null)
            {
                foreach (var item in localOrder.Tables)
                {
                    localItems[item.Id] = item;
                }
            }
            
            foreach (var item in serverOrder.Tables)
            {
                if (!localItems.ContainsKey(item.Id))
                {
                    newItems.Add(item);
                }
                else
                {
                    // Check if quantity increased
                    var localItem = localItems[item.Id];
                    if (item.Quantity > localItem.Quantity)
                    {
                        // Create a new item representing the additional quantity
                        var additionalItem = new ContractorOrderTable
                        {
                            Id = item.Id,
                            ProductShortName = item.ProductShortName,
                            ProductCode = item.ProductCode,
                            Quantity = item.Quantity - localItem.Quantity,
                            EstimatedPrice = item.EstimatedPrice,
                            Amount = item.Amount - localItem.Amount,
                            Responsible = item.Responsible,
                            Position = item.Position,
                            ContractorRequirement = item.ContractorRequirement,
                            OrderNumber = item.OrderNumber
                        };
                        newItems.Add(additionalItem);
                    }
                }
            }
            
            return newItems;
        }

        /// <summary>
        /// Get all local orders
        /// </summary>
        public List<ContractorOrder> GetLocalOrders()
        {
            lock (_lockObject)
            {
                return _localOrders.Values.ToList();
            }
        }

        /// <summary>
        /// Get local order by ID
        /// </summary>
        public ContractorOrder? GetLocalOrder(int orderId)
        {
            lock (_lockObject)
            {
                return _localOrders.TryGetValue(orderId, out var order) ? order : null;
            }
        }
    }

    public class LocalDataComparison
    {
        public List<ContractorOrder> NewOrders { get; set; } = new List<ContractorOrder>();
        public List<OrderItemChange> NewItems { get; set; } = new List<OrderItemChange>();
        public List<int> DeletedOrderIds { get; set; } = new List<int>();
    }

    public class OrderItemChange
    {
        public int OrderId { get; set; }
        public ContractorOrderTable Item { get; set; } = new ContractorOrderTable();
        public int TableNumber { get; set; }
    }
} 