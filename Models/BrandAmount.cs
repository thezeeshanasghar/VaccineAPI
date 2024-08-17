using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace VaccineAPI.Models
{

  public class BrandAmount
  {
    public long Id { get; set; }
    public int Amount { get; set; }
    public int Count { get; set; }
    public long BrandId { get; set; }
    [JsonIgnore]
    public virtual Brand Brand { get; set; }

    public long DoctorId { get; set; }
    [JsonIgnore]
    public virtual Doctor Doctor { get; set; }
    //  public virtual string VaccineName { get; set; }
  }

}