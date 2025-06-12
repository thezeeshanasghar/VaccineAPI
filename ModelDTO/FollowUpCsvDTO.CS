using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;

namespace VaccineAPI.ModelDTO
{
public class FollowUpCsvDTO
{
    public string ChildName { get; set; }
    public string FatherName { get; set; }
    public string DOB { get; set; }
    public string ClinicName { get; set; }
    public string DoctorName { get; set; }
    public string CurrentVisitDate { get; set; }
    public string NextVisitDate { get; set; }
    public string Disease { get; set; }
    public string Weight { get; set; }
    public string Height { get; set; }
    public string OFC { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
}
}