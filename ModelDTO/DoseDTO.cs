using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace VaccineAPI.ModelDTO
{

    public class DoseDTO
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public int MinAge { get; set; }
        public int? MaxAge { get; set; }
        public int? MinGap { get; set; }
        public int? DoseOrder { get; set; }
        public long VaccineId { get; set; }
        public string CountryCode { get; set; } = "";
        public string PhoneNumber { get; set; } = "";
        public ClinicDTO Clinic { get; set; } = null!;
        public VaccineDTO Vaccine { get; set; } = null!;

        // Signed magic-link token for the parent app, so VacDoc can build a
        // login-bypassing deep link into the child's vaccine page. Same value on
        // every dose in the response (it is keyed by child, not dose).
        public string LinkToken { get; set; } = "";
        public long ChildId { get; set; }

        // DTaP combo-coverage grey-out (Add Dose screen only). Populated by
        // DoseController.GetSDosesForChild when this dose belongs to a ContainsDTaP
        // vaccine whose DoseOrder is already covered by DTaP-equivalent doses the child
        // has been given from any ContainsDTaP vaccine. Left false/"" everywhere else —
        // additive fields, harmless for other consumers of this DTO.
        public bool IsCoveredByDTaP { get; set; }
        public string CoveredByVaccineNames { get; set; } = "";
    }

}