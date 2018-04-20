using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StocksDAL
{
    public class Stock
    {
        public int StockId { get; set; }
        public Dictionary<DateTime,double> DateWithPrice { get; set; }
    }
}
