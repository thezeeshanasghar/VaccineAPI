using System.Collections.Generic;

namespace VaccineAPI.ModelDTO
{
    public class StockPositionRowDTO
    {
        public long BrandId { get; set; }
        public string BrandName { get; set; } = "";
        public int Opening { get; set; }
        public int Purchased { get; set; }
        public int DirectSale { get; set; }
        public int Given { get; set; }
        public int Adjusted { get; set; }
        public int Transfer { get; set; }
        public int Closing { get; set; }

        // True when this brand has no stock-adding event (Purchase/AdjustIncrease/TransferIn/
        // OpeningBalance) at this clinic at all, or the report's whole window falls before the
        // brand's first such event — there is no valid Opening/Closing to show, ever or yet.
        public bool HasNoRecord { get; set; }
    }

    public class StockPositionReportDTO
    {
        public string ClinicName { get; set; } = "";
        public string FromDate { get; set; } = "";
        public string ToDate { get; set; } = "";
        public List<StockPositionRowDTO> Rows { get; set; } = new List<StockPositionRowDTO>();
    }
}
