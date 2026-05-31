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

    // ── Advanced Analytics ─────────────────────────────────────────────────
    public class AdvancedAnalyticsDTO
    {
        public PeriodSummaryDTO Current  { get; set; }
        public PeriodSummaryDTO Previous { get; set; }
        public List<ClinicSummaryDTO>   ByClinic  { get; set; }
        public List<VaccinePerfDTO>     ByVaccine { get; set; }
        public List<MonthlyTrendDTO>    Monthly     { get; set; }
        public List<MonthlyTrendDTO>    MonthlyPrev { get; set; }
    }

    public class PeriodSummaryDTO
    {
        public int     Patients  { get; set; }
        public int     Doses     { get; set; }
        public decimal Revenue   { get; set; }  // vaccination + direct sales + consult fees (billed)
        public decimal Expense   { get; set; }  // purchase bills + operating expenses
        public decimal NetProfit { get; set; }
    }

    public class ClinicSummaryDTO
    {
        public long    ClinicId   { get; set; }
        public string  ClinicName { get; set; }
        public decimal Revenue    { get; set; }
        public decimal Expense    { get; set; }
        public decimal Net        { get; set; }
        public int     Doses      { get; set; }
        public int     Patients   { get; set; }
    }

    public class VaccinePerfDTO
    {
        public long    BrandId  { get; set; }
        public string  Name     { get; set; }
        public int     Doses    { get; set; }
        public decimal Revenue  { get; set; }
        public decimal AvgPrice { get; set; }
    }

    public class MonthlyTrendDTO
    {
        public string  Month    { get; set; }
        public decimal Revenue  { get; set; }
        public int     Doses    { get; set; }
        public int     Patients { get; set; }
        public decimal Expense  { get; set; }
    }
}