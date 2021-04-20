using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;

namespace VaccineAPI.Models
{

    public class Schedule
    {
        // override object.Equals
        // public override bool Equals(object obj)
        // {
        //     if (obj == null || GetType() != obj.GetType())
        //     {
        //         return false;
        //     }
        //     Schedule tmp = (Schedule)obj;
        //     if(this.Id==tmp.Id)
        //         return false;
            
            
        //     return base.Equals(obj);
        // }
        
        // // override object.GetHashCode
        // public override int GetHashCode()
        // {
        //     return base.GetHashCode();
        // }
        // public object Clone() {
        //     return this.MemberwiseClone();
        // }
        public long Id { get; set; }
       
        public DateTime Date { get; set; }
        public float? Weight { get; set; }
        public float? Height { get; set; }
        public float? Circle { get; set; }
        public bool IsDone { get; set; }
        public bool? IsSkip { get; set; }
        public bool? IsDisease { get; set; }
        public bool Due2EPI { get; set; }
        public string DiseaseYear {get; set;}
        public DateTime? GivenDate { get; set; }
         public long? BrandId { get; set; }
        public virtual Brand Brand { get; set; }
        public int? Amount {get; set;}
        
         public long ChildId { get; set; }
        public virtual Child Child { get; set; }
       
        public long DoseId { get; set; }
        public virtual Dose Dose { get; set; }
        // public virtual DateTime FromDate { get; set; }
        // public virtual DateTime ToDate { get; set; }
    }

}