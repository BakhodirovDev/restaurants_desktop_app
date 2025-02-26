using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Restaurants.Class
{
    public class TableData
    {
        public int TableNumber { get; set; }
        public List<OrderItem> Orders { get; set; } = new List<OrderItem>();
    }
}
