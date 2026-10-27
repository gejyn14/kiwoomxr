using System;
using System.Collections.Generic;

namespace SpatialTrading.Domain
{
    public sealed class Holding
    {
        public string Code { get; }
        public string Name { get; }
        public long Quantity { get; }
        public decimal? AveragePrice { get; }
        public decimal? CurrentPrice { get; }
        public decimal? Profit { get; }
        public decimal? ProfitRate { get; }
        public Holding(string code, string name, long quantity, decimal? averagePrice, decimal? currentPrice,
            decimal? profit, decimal? profitRate)
        {
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name) || quantity < 0)
                throw new ArgumentException("Invalid holding identity or quantity.");
            Code = code; Name = name; Quantity = quantity; AveragePrice = averagePrice;
            CurrentPrice = currentPrice; Profit = profit; ProfitRate = profitRate;
        }
    }

    public sealed class BalanceSnapshot
    {
        public string Account { get; }
        public decimal? TotalEvaluation { get; }
        public decimal? TotalProfit { get; }
        public decimal? TotalProfitRate { get; }
        public decimal? OrderableCash { get; }
        public IReadOnlyList<Holding> Holdings { get; }
        public BalanceSnapshot(string account, decimal? totalEvaluation, decimal? totalProfit, decimal? totalProfitRate,
            decimal? orderableCash, IReadOnlyList<Holding> holdings)
        {
            if (string.IsNullOrWhiteSpace(account) || holdings == null) throw new ArgumentException("Balance requires an account and holdings.");
            var copy = new List<Holding>(holdings);
            if (copy.Contains(null)) throw new ArgumentException("Holdings must not contain null.");
            Account = account; TotalEvaluation = totalEvaluation; TotalProfit = totalProfit;
            TotalProfitRate = totalProfitRate; OrderableCash = orderableCash; Holdings = copy.AsReadOnly();
        }
    }

    public sealed class NewsItem
    {
        public bool IsFiling { get; }
        public string Source { get; }
        public string Title { get; }
        public DateTimeOffset PublishedAt { get; }
        public IReadOnlyList<string> Mentions { get; }
        public string FilingType { get; }
        public NewsItem(bool isFiling, string source, string title, DateTimeOffset publishedAt,
            IReadOnlyList<string> mentions, string filingType)
        {
            if (source == null || string.IsNullOrWhiteSpace(title) || mentions == null)
                throw new ArgumentException("News requires source, title and mentions.");
            IsFiling = isFiling; Source = source; Title = title; PublishedAt = publishedAt;
            Mentions = new List<string>(mentions).AsReadOnly(); FilingType = filingType;
        }
    }

    public sealed class NewsFeed
    {
        public IReadOnlyList<NewsItem> Items { get; }
        public NewsFeed(IReadOnlyList<NewsItem> items)
        {
            if (items == null) throw new ArgumentException("News feed requires an explicit item list.");
            var copy = new List<NewsItem>(items);
            if (copy.Contains(null)) throw new ArgumentException("News items must not be null.");
            Items = copy.AsReadOnly();
        }
    }
}
