using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;
namespace VaccineAPI.ModelDTO
{
public class BirthdayCsvDTO
{
    public string ChildName { get; set; } = "";
    public string FatherName { get; set; } = "";
    public string DOB { get; set; } = "";
    public string ClinicName { get; set; } = "";
    public string DoctorName { get; set; } = "";
    public int Age { get; set; }
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
}
}