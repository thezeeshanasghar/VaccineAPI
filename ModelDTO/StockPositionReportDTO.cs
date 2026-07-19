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
    }

    public class StockPositionReportDTO
    {
        public string ClinicName { get; set; } = "";
        public string FromDate { get; set; } = "";
        public string ToDate { get; set; } = "";
        public List<StockPositionRowDTO> Rows { get; set; } = new List<StockPositionRowDTO>();
    }
}
