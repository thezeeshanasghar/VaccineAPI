using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
namespace VaccineAPI.ModelDTO
{
    public class FeeDTO
    {
        public string InvoiceId { get; set; }
        public decimal Amount { get; set; }
    }
}