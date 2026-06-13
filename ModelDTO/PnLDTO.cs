using System;
using System.Collections.Generic;

namespace VaccineAPI.ModelDTO
{
    public class PnLResponseDTO
    {
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public string ExpenseMode { get; set; } = "operational";
        public List<PnLClinicBreakdownDTO> Clinics { get; set; } = new List<PnLClinicBreakdownDTO>();
        public PnLTotalsDTO Consolidated { get; set; } = new PnLTotalsDTO();
    }

    public class PnLClinicBreakdownDTO
    {
        public long ClinicId { get; set; }
        public string ClinicName { get; set; } = "";

        // Income streams (individual + accumulated)
        public decimal VaccineRevenue { get; set; }
        public decimal VaccineCost { get; set; }
        public decimal VaccineProfit { get; set; }
        public decimal ConsultationIncome { get; set; }
        public decimal DirectSaleRevenue { get; set; }
        public decimal DirectSaleCost { get; set; }
        public decimal DirectSaleProfit { get; set; }
        public decimal TotalIncome { get; set; }

        // Expenses
        public List<PnLCategoryAmountDTO> ExpensesByCategory { get; set; } = new List<PnLCategoryAmountDTO>();
        public decimal DirectExpenses { get; set; }
        public decimal SharedExpenseShare { get; set; }
        public decimal SharedExpenseSharePercent { get; set; }
        public decimal TotalExpenses { get; set; }

        public decimal NetPnL { get; set; }
    }

    public class PnLTotalsDTO
    {
        public decimal TotalVaccineRevenue { get; set; }
        public decimal TotalVaccineCost { get; set; }
        public decimal TotalVaccineProfit { get; set; }
        public decimal TotalConsultationIncome { get; set; }
        public decimal TotalDirectSaleRevenue { get; set; }
        public decimal TotalDirectSaleCost { get; set; }
        public decimal TotalDirectSaleProfit { get; set; }
        public decimal TotalIncome { get; set; }
        public decimal TotalDirectExpenses { get; set; }
        public decimal SharedExpensesTotal { get; set; }
        public List<PnLCategoryAmountDTO> SharedExpensesByCategory { get; set; } = new List<PnLCategoryAmountDTO>();
        public decimal TotalExpenses { get; set; }
        public decimal NetPnL { get; set; }
    }

    public class PnLCategoryAmountDTO
    {
        public string Category { get; set; } = "";
        public decimal Amount { get; set; }
    }
}
