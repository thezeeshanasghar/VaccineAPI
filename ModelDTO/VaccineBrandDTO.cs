using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;
namespace VaccineAPI.ModelDTO
{
public class VaccineBrandDto
{
    public int Id { get; set; }
    public int BrandId { get; set; }
    public int VaccineId { get; set; }
    public string BrandName { get; set; }
    public string VaccineName { get; set; }
    public BrandDTO Brand { get; set; }
    public VaccineDTO Vaccine { get; set; }
}
}