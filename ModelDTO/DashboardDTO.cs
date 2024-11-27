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
    }
}