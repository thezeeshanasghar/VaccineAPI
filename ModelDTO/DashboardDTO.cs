using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;

namespace VaccineAPI.ModelDTO
{
    public class DashboardDTO
    {
        public int CurrentMonthChildCount { get; set; }
        public int TotalChildCount { get; set; }
        public int TotalAlertsCount { get; set; }
        public int FutureAlertsCount { get; set; }
        public int GivenDosesCount { get; set; }
        public decimal TotalRevenue { get; set; }
    }

    public class AnalyticsDTO
    {
        public List<MonthlyStatDTO> MonthlyRevenue { get; set; }
        public List<MonthlyStatDTO> MonthlyDoses { get; set; }
        public List<MonthlyStatDTO> MonthlyNewPatients { get; set; }
        public List<BrandStatDTO> TopVaccines { get; set; }
    }

    public class MonthlyStatDTO
    {
        public string Month { get; set; }
        public decimal Value { get; set; }
    }

    public class BrandStatDTO
    {
        public string BrandName { get; set; }
        public int Count { get; set; }
    }
}