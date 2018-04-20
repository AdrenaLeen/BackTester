using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data.SqlClient;
using System.Data;

namespace StocksDAL
{
    public class StockDAL
    {
        private SqlConnection sqlConnection = null;
        public void OpenConnection(string connectionString)
        {
            sqlConnection = new SqlConnection { ConnectionString = connectionString };
            sqlConnection.Open();
        }

        public void CloseConnection()
        {
            sqlConnection.Close();
        }

        public List<Stock> GetStocksAsList(string ids, string startDate, string endDate)
        {
            List<Stock> stocks = new List<Stock>();

            string sql = $"Select * From StockPrices Where StockId In ({ids}) And Date >= '{startDate}' And Date < '{endDate}'";
            using (SqlCommand command = new SqlCommand(sql, sqlConnection))
            {
                SqlDataReader dataReader = command.ExecuteReader();
                while (dataReader.Read())
                {
                    Stock currentStock = (from s in stocks where s.StockId == (int)dataReader["StockId"] select s).SingleOrDefault();
                    if (currentStock == null)
                    {
                        Dictionary<DateTime, double> currentDateAndPrice = new Dictionary<DateTime, double>();
                        currentDateAndPrice.Add((DateTime)dataReader["Date"], (double)dataReader["Price"]);
                        stocks.Add(new Stock
                        {
                            StockId = (int)dataReader["StockId"],
                            DateWithPrice = currentDateAndPrice
                        });
                    }
                    else
                    {
                        int index = stocks.IndexOf(currentStock);
                        currentStock.DateWithPrice.Add((DateTime)dataReader["Date"], (double)dataReader["Price"]);
                        stocks[index] = currentStock;
                    }
                }
                dataReader.Close();
            }
            return stocks;
        }

        public DataTable GetStocksAsDataTable(string ids, string startDate, string endDate)
        {
            DataTable dataTable = new DataTable();

            string sql = $"Select * From StockPrices Where StockId In ({ids}) And Date >= '{startDate}' And Date < '{endDate}'";
            using (SqlCommand cmd = new SqlCommand(sql, sqlConnection))
            {
                SqlDataReader dataReader = cmd.ExecuteReader();
                dataTable.Load(dataReader);
                dataReader.Close();
            }
            return dataTable;
        }
    }
}
