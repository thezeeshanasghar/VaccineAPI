using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace VaccineAPI.ModelDTO
{
    public class InvoiceDTO
    {

        public string InvoiceId { get; set; }

        public decimal Amount { get; set; }

        public long ChildId { get; set; }
    }
}