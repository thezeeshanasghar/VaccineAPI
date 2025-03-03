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
}