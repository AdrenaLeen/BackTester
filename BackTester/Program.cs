using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Configuration;
using System.Data.Common;
using System.Data.SqlClient;
using StocksDAL;
using System.Data;
using System.IO;
using System.Globalization;

namespace BackTester
{
    class Program
    {
        static void Main()
        {
            //int[] idsArray = GetIdsArray();
            int[] idsArray = GetTestIds();
            List<Stock> stocks = GetStocks(idsArray, 0, "2017-01-01 00:00:00.000", "2018-01-01 00:00:00.000");
            Console.WriteLine();

            int year = 2017;
            //List<Pair> pairs = GetPairs(year);
            List<Pair> pairs = GetTestPair();
            StringBuilder csv = new StringBuilder();
            MLApp.MLApp matlab = new MLApp.MLApp();
            matlab.Execute(@"cd D:\OneDrive\Matlab");
            foreach (Pair p in pairs)
            {
                Console.WriteLine($"Тестируем пару ({p.IdY},{p.IdX})");
                Stock Y = (from s in stocks where s.StockId == p.IdY select s).Single();
                Stock X = (from s in stocks where s.StockId == p.IdX select s).Single();
                int half = GetHalf(Y);
                Console.WriteLine($"half: {half}");

                double m = GetMaxAbsSpread(Y, X, half);
                Console.WriteLine($"m: {m}");
                Console.WriteLine();

                double[] spread = GetSpread(Y, X, p.Beta);
                Dictionary<double, double> profit = GetProfit(half, m, spread);
                double bestG = GetBestG(profit);
                Console.WriteLine($"Наилучшая g: {bestG}");
                Console.WriteLine();

                Console.WriteLine("***** Тестирование стратегии *****");
                Console.WriteLine();
                double percentProfit = TestStrategyFromGToZero(Y, X, half, spread, bestG, p.Beta, 365.0, matlab);
                Console.WriteLine();

                Console.WriteLine("***** Тестирование альтернативной стратегии *****");
                Console.WriteLine();
                double percentAltProfit = TestStrategyFromGToG(Y, X, half, spread, bestG, p.Beta, 365.0, matlab);
;
                csv.AppendLine($"{Y.StockId};{X.StockId};{percentProfit};{percentAltProfit}");
            }
            //File.WriteAllText($"Poloniex {year}\\pairsWithProfit.csv", csv.ToString());

            Console.ReadLine();
        }

        private static int GetTestIdY() => 10779;
        private static int GetTestIdX() => 10775;
        private static double GetTestBeta() => 36.68714635810386;
        private static int[] GetTestIds() => new int[] { GetTestIdY(), GetTestIdX() };
        private static List<Pair> GetTestPair()
        {
            List<Pair> pairs = new List<Pair>();
            pairs.Add(new Pair { IdY = GetTestIdY(), IdX = GetTestIdX(), Beta = GetTestBeta() });
            return pairs;
        }

        private static List<Pair> GetPairs(int year)
        {
            string[] data = File.ReadAllLines($"Poloniex {year}\\pairsPoloniex.txt");
            List<Pair> pairs = new List<Pair>();
            foreach (string pair in data)
            {
                string[] values = pair.Split(',');
                pairs.Add(new Pair { IdY = int.Parse(values[0]), IdX = int.Parse(values[1]), Beta = double.Parse(values[2], CultureInfo.InvariantCulture) });
            }
            return pairs;
        }

        private static int[] GetIdsArray()
        {
            string[] idsArray = File.ReadAllLines(@"D:\OneDrive\Документы\Рабочие материалы\Ксения Кузнецова\Аспирантура\НИД\2018 - Статья ютимаг\NonStationaryIdsPoloniex2017.txt");
            return Array.ConvertAll(idsArray, new Converter<string, int>(int.Parse));
        }

        private static double TestStrategyFromGToG(Stock Y, Stock X, int half, double[] spread, double bestG, double beta, double totalTradeDays, MLApp.MLApp matlab)
        {
            int tradeIndex = GetFirstTradeIndex(Y, X, spread, half + 1, Y.DateWithPrice.Count, bestG, matlab);
            List<int> tradeIndexes = new List<int>();
            double[] position = null;
            List<double[]> positions = new List<double[]>();
            bool terminate = false;
            while (tradeIndex > -1)
            {
                tradeIndexes.Add(tradeIndex);
                if (terminate) positions.Add(new double[] { 0, 0 });
                else
                {
                    position = GetAltPositon(spread, tradeIndex, beta, position);
                    positions.Add(position);
                }
                if (terminate || tradeIndex == spread.Length - 1) tradeIndex = -1;
                else tradeIndex = GetNextIndexAltStrategy(Y, X, spread, tradeIndex, Y.DateWithPrice.Count - 1, bestG, matlab, out terminate);
            }
            if ((!terminate) && (positions.Count > 0) && (positions.Last()[0] != 0) && (tradeIndexes.Last() != spread.Length - 1))
            {
                tradeIndexes.Add(spread.Length - 1);
                positions.Add(new double[] { 0, 0 });
            }
            List<double> profits = GetAltProfit(tradeIndexes, spread, positions);

            for (int i = 0; i < tradeIndexes.Count; i++)
            {
                Console.WriteLine($"Момент времени: {tradeIndexes[i]}");
                Console.WriteLine($"Спред: {spread[tradeIndexes[i]]}");
                Console.WriteLine($"Позиция: {positions[i][0]}; {positions[i][1]}");
                Console.WriteLine($"Цена Y: {Y.DateWithPrice.ElementAt(tradeIndexes[i]).Value}");
                Console.WriteLine($"Цена X: {X.DateWithPrice.ElementAt(tradeIndexes[i]).Value}");
                Console.WriteLine($"Прибыль: {profits[i]}");
                Console.WriteLine();
            }
            double totalProfit = profits.Sum();
            Console.WriteLine($"Итого: {totalProfit}");
            if (positions.Count == 0) return 0;
            double initialInvestment = Math.Abs(positions[0][0]) * Y.DateWithPrice.ElementAt(tradeIndexes[0]).Value + Math.Abs(positions[0][1]) * X.DateWithPrice.ElementAt(tradeIndexes[0]).Value;
            Console.WriteLine($"Начальные вложения: {initialInvestment}");
            double percentProfit = totalProfit / initialInvestment;
            int tradeDays = 0;
            if (terminate) tradeDays = tradeIndexes.Last() - half;
            else tradeDays = Y.DateWithPrice.Count - (half + 1);
            Console.WriteLine($"Доходность за {tradeDays} торговых дней: {percentProfit}");
            if (percentProfit < 0 || terminate) return percentProfit;
            double percentDays = tradeDays / totalTradeDays;
            Console.WriteLine($"Процент торговых дней: {percentDays}");
            double predictProfit = percentProfit / percentDays;
            Console.WriteLine($"Прогнозируемая доходность за год: {predictProfit}");
            return predictProfit;
        }

        private static double TestStrategyFromGToZero(Stock Y, Stock X, int half, double[] spread, double bestG, double beta, double totalTradeDays, MLApp.MLApp matlab)
        {
            int tradeIndex = GetFirstTradeIndex(Y, X, spread, half + 1, Y.DateWithPrice.Count, bestG, matlab);
            List<int> tradeIndexes = new List<int>();
            double[] position = null;
            List<double[]> positions = new List<double[]>();
            bool terminate = false;
            while (tradeIndex > -1)
            {
                tradeIndexes.Add(tradeIndex);
                position = GetPositon(spread, tradeIndex, beta, position);
                positions.Add(position);
                if (terminate || tradeIndex == spread.Length - 1) tradeIndex = -1;
                else tradeIndex = GetNextIndexZeroStrategy(Y, X, spread, tradeIndex, Y.DateWithPrice.Count - 1, bestG, position, matlab, out terminate);
            }
            if ((!terminate) && (positions.Count > 0) && (positions.Last()[0] != 0) && (tradeIndexes.Last() != spread.Length - 1))
            {
                tradeIndexes.Add(spread.Length - 1);
                positions.Add(new double[] { 0, 0 });
            }
            List<double> profits = GetProfit(tradeIndexes, spread, positions);

            for (int i = 0; i < tradeIndexes.Count; i++)
            {
                Console.WriteLine($"Момент времени: {tradeIndexes[i]}");
                Console.WriteLine($"Спред: {spread[tradeIndexes[i]]}");
                Console.WriteLine($"Позиция: {positions[i][0]}; {positions[i][1]}");
                Console.WriteLine($"Цена Y: {Y.DateWithPrice.ElementAt(tradeIndexes[i]).Value}");
                Console.WriteLine($"Цена X: {X.DateWithPrice.ElementAt(tradeIndexes[i]).Value}");
                Console.WriteLine($"Прибыль: {profits[i]}");
                Console.WriteLine();
            }
            double totalProfit = profits.Sum();
            Console.WriteLine($"Итого: {totalProfit}");
            if (positions.Count == 0) return 0;
            double initialInvestment = Math.Abs(positions[0][0]) * Y.DateWithPrice.ElementAt(tradeIndexes[0]).Value + Math.Abs(positions[0][1]) * X.DateWithPrice.ElementAt(tradeIndexes[0]).Value;
            Console.WriteLine($"Начальные вложения: {initialInvestment}");
            double percentProfit = totalProfit / initialInvestment;
            int tradeDays = 0;
            if (terminate) tradeDays = tradeIndexes.Last() - half;
            else tradeDays = Y.DateWithPrice.Count - (half + 1);
            Console.WriteLine($"Доходность за {tradeDays} торговых дней: {percentProfit}");
            if (percentProfit < 0 || terminate) return percentProfit;
            double percentDays = tradeDays / totalTradeDays;
            Console.WriteLine($"Процент торговых дней: {percentDays}");
            double predictProfit = percentProfit / percentDays;
            Console.WriteLine($"Прогнозируемая доходность за год: {predictProfit}");
            return predictProfit;
        }

        private static List<double> GetAltProfit(List<int> tradeIndexes, double[] spread, List<double[]> positions)
        {
            List<double> profitList = new List<double>();
            profitList.Add(0);
            for (int i = 1; i < tradeIndexes.Count; i++)
            {
                if (positions[i - 1][0] == 1) profitList.Add(spread[tradeIndexes[i]] - spread[tradeIndexes[i - 1]]);
                else profitList.Add(spread[tradeIndexes[i - 1]] - spread[tradeIndexes[i]]);
            }
            return profitList;
        }

        private static List<double> GetProfit(List<int> tradeIndexes, double[] spread, List<double[]> positions)
        {
            List<double> profitList = new List<double>();
            for (int i = 0; i < tradeIndexes.Count; i++)
            {
                if (positions[i][0] != 0) profitList.Add(0);
                else
                {
                    if (positions[i - 1][0] == 1) profitList.Add(spread[tradeIndexes[i]] - spread[tradeIndexes[i - 1]]);
                    else profitList.Add(spread[tradeIndexes[i - 1]] - spread[tradeIndexes[i]]);
                }
            }
            return profitList;
        }

        private static double[] GetAltPositon(double[] spread, int tradeIndex, double beta, double[] position)
        {
            if (spread[tradeIndex] > 0) return new double[] { -1, beta };
            else return new double[] { 1, -beta };
        }

        private static double[] GetPositon(double[] spread, int tradeIndex, double beta, double[] prevPosition)
        {
            if (prevPosition != null && prevPosition[0] != 0) return new double[] { 0, 0 };
            if (spread[tradeIndex] > 0) return new double[] { -1, beta };
            else return new double[] { 1, -beta };
        }

        private static Dictionary<double, double> GetProfit(int half, double m, double[] spread)
        {
            Dictionary<double, double> profit = new Dictionary<double, double>();
            for (int i = 0; i < 10; i++)
            {
                Console.WriteLine($"Процент: {5 * (i + 1)}");
                double g = 0.05 * (i + 1) * m;
                Console.WriteLine($"g: {g}");
                List<int> tradeIndexes = FindTradeIndexes(spread, half, g);
                Console.WriteLine("Номера сделок:");
                foreach (int index in tradeIndexes) Console.WriteLine(index);
                Console.WriteLine($"Количество сделок: {tradeIndexes.Count}");
                double minProfit = CalculateMinProfit(tradeIndexes, g);
                Console.WriteLine($"Минимальная прибыль: {minProfit}");
                profit.Add(g, minProfit);
                Console.WriteLine();
                if (tradeIndexes.Count == 0) break;
            }
            return profit;
        }

        private static double GetBestG(Dictionary<double, double> profit) => (from p in profit where p.Value == profit.Values.Max() select p.Key).First();

        private static double CalculateMinProfit(List<int> tradeIndexes, double g)
        {
            if (tradeIndexes.Count == 0) return 0;
            else return (tradeIndexes.Count - 1) * 2 * g;
        }

        private static List<int> FindTradeIndexes(double[] spread, int half, double g)
        {
            int tradeIndex = GetFirstTradeIndex(spread, 0, half, g);
            List<int> tradeIndexes = new List<int>();
            while (tradeIndex > -1)
            {
                tradeIndexes.Add(tradeIndex);
                tradeIndex = GetNextIndex(spread, tradeIndex, half, g);
            }
            return tradeIndexes;
        }

        private static bool CheckCointegration(MLApp.MLApp matlab, int n, Stock Y, Stock X)
        {
            object result = null;
            double[,] testPrices = new double[n, 2];
            for (int i = 0; i < n; i++)
            {
                testPrices[i, 0] = Y.DateWithPrice.ElementAt(i).Value;
                testPrices[i, 1] = X.DateWithPrice.ElementAt(i).Value;
            }
            matlab.Feval("TestCoint", 1, out result, testPrices);
            object[] res = result as object[];
            return (bool)res[0];
        }

        private static int GetNextIndexAltStrategy(Stock Y, Stock X, double[] spread, int startIndex, int endIndex, double g, MLApp.MLApp matlab, out bool terminate)
        {
            terminate = false;
            if (spread[startIndex] >= g)
            {
                for (int i = startIndex + 1; i < endIndex + 1; i++)
                {
                    if (!CheckCointegration(matlab, i + 1, Y, X))
                    {
                        terminate = true;
                        return i;
                    }
                    if (spread[i] <= -g) return i;
                }
            }
            if (spread[startIndex] <= -g)
            {
                for (int i = startIndex + 1; i < endIndex + 1; i++)
                {
                    if (!CheckCointegration(matlab, i + 1, Y, X))
                    {
                        terminate = true;
                        return i;
                    }
                    if (spread[i] >= g) return i;
                }
            }
            return -1;
        }

        private static int GetNextIndexZeroStrategy(Stock Y, Stock X, double[] spread, int startIndex, int endIndex, double g, double[] position, MLApp.MLApp matlab, out bool terminate)
        {
            terminate = false;
            if (position[0] != 0)
            {
                if (spread[startIndex] >= 0)
                {
                    for (int i = startIndex + 1; i < endIndex + 1; i++)
                    {
                        if (!CheckCointegration(matlab, i + 1, Y, X))
                        {
                            terminate = true;
                            return i;
                        }
                        if (spread[i] <= 0) return i;
                    }
                }
                if (spread[startIndex] <= 0)
                {
                    for (int i = startIndex + 1; i < endIndex + 1; i++)
                    {
                        if (!CheckCointegration(matlab, i + 1, Y, X))
                        {
                            terminate = true;
                            return i;
                        }
                        if (spread[i] >= 0) return i;
                    }
                }
            }
            else
            {
                for (int i = startIndex + 1; i < endIndex; i++)
                {
                    if (spread[i] >= g || spread[i] <= -g) return i;
                }
            }
            return -1;
        }

        private static int GetNextIndex(double[] spread, int startIndex, int endIndex, double g)
        {
            if (spread[startIndex] >= g)
            {
                for (int i = startIndex; i < endIndex + 1; i++)
                {
                    if (spread[i] <= -g) return i;
                }
            }
            if (spread[startIndex] <= -g)
            {
                for (int i = startIndex; i < endIndex + 1; i++)
                {
                    if (spread[i] >= g) return i;
                }
            }
            return -1;
        }

        private static int GetFirstTradeIndex(Stock Y, Stock X, double[] spread, int startIndex, int endIndex, double g, MLApp.MLApp matlab)
        {
            for (int i = startIndex; i < endIndex; i++)
            {
                if (!CheckCointegration(matlab, i + 1, Y, X)) return -1;
                if (Math.Abs(spread[i]) >= g) return i;
            }
            return -1;
        }

        private static int GetFirstTradeIndex(double[] spread, int startIndex, int endIndex, double g)
        {
            for (int i = startIndex; i < endIndex; i++)
            {
                if (Math.Abs(spread[i]) >= g) return i;
            }
            return -1;
        }

        private static int GetHalf(Stock stock) => (int)Math.Floor((stock.DateWithPrice.Count - 1) / 2.0);

        private static List<Stock> GetStocks(int[] ids, int skip, string startDate, string endDate)
        {
            StockDAL stockDAL = new StockDAL();
            stockDAL.OpenConnection(GetConnString());
            List<Stock> stocks = new List<Stock>();

            try
            {
                stocks = ListStocksViaList(stockDAL, string.Join(",", ids), startDate, endDate, skip);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            finally
            {
                stockDAL.CloseConnection();
            }

            return stocks;
        }

        private static double[] GetSpread(Stock Y, Stock X, double beta)
        {
            double[] spread = new double[Y.DateWithPrice.Count];
            for (int i = 0; i < Y.DateWithPrice.Count; i++)
            {
                spread[i] = Y.DateWithPrice.ElementAt(i).Value - beta * X.DateWithPrice.ElementAt(i).Value;
            }
            return spread;
        }

        private static double StringToDouble(string line)
        {
            return double.Parse(line, CultureInfo.InvariantCulture);
        }

        private static double GetMaxAbsSpread(Stock Y, Stock X, int half)
        {
            double r = GetAverageHalf(Y, X, half);
            Console.WriteLine($"r: {r}");
            List<double> absspread = new List<double>();
            for (int i = 0; i < half + 1; i++) absspread.Add(Math.Abs(Y.DateWithPrice.ElementAt(i).Value - r * X.DateWithPrice.ElementAt(i).Value));
            return absspread.Max();
        }

        private static double GetAverageHalf(Stock Y, Stock X, int half)
        {
            double sumRatio = 0;
            for (int i = 0; i < half + 1; i++)
            {
                sumRatio += Y.DateWithPrice.ElementAt(i).Value / X.DateWithPrice.ElementAt(i).Value;
            }
            Console.WriteLine($"sumRatio: {sumRatio}");
            return sumRatio / (half + 1);
        }

        private static void ListStocks(StockDAL stockDAL, string ids, string startDate, string endDate)
        {
            DataTable dt = stockDAL.GetStocksAsDataTable(ids, startDate, endDate);
            DisplayTable(dt);
        }

        private static void DisplayTable(DataTable dt)
        {
            for (int curCol = 0; curCol < dt.Columns.Count; curCol++) Console.Write($"{dt.Columns[curCol].ColumnName}\t");
            Console.WriteLine("\n----------------------------------");

            for (int curRow = 0; curRow < dt.Rows.Count; curRow++)
            {
                for (int curCol = 0; curCol < dt.Columns.Count; curCol++) Console.Write($"{dt.Rows[curRow][curCol]}\t");
                Console.WriteLine();
            }
        }

        private static List<Stock> ListStocksViaList(StockDAL stockDAL, string ids, string startDate, string endDate, int skip)
        {
            List<Stock> record = stockDAL.GetStocksAsList(ids, startDate, endDate);
            if (skip == 0)
            {
                foreach (Stock stock in record) stock.DateWithPrice = stock.DateWithPrice.ToDictionary(x => x.Key, x => x.Value);
            }
            else
            {
                foreach (Stock stock in record) stock.DateWithPrice = stock.DateWithPrice.Skip(skip).ToDictionary(x => x.Key, x => x.Value);
            }
            Console.WriteLine("ID:\tДата:\tЦена:");
            foreach (Stock c in record)
            {
                foreach (var dp in c.DateWithPrice) Console.WriteLine($"{c.StockId}\t{dp.Key}\t{dp.Value}");
            }
            return record;
        }

        public static string GetConnString()
        {
            return ConfigurationManager.ConnectionStrings["StockSqlProvider"].ConnectionString;
        }
    }
}
