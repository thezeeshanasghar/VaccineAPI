using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;

namespace VaccineAPI.ModelDTO
{

    public class ChildCsvDTO
    {

        public string Name { get; set; }
        public string FatherName { get; set; }
        public string DOB { get; set; }
        public string City { get; set; }
        // public string Location { get; set; }
        public string Due_Date { get; set; }
        public string Next_Due_Date { get; set; }
        public string Due_Vaccines { get; set; }
        public string Next_Due_Vaccines { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }

    }

}