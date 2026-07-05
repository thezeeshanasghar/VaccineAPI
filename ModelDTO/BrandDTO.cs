using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace VaccineAPI.ModelDTO
{

    public class BrandDTO
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string Manufacturer { get; set; } = "";
        public int? MinAge { get; set; }
        // §10: true when a case-only twin exists (HEXAXIM vs Hexaxim). Give-time UI echoes
        // "⚠ This is <Name> — not its look-alike" so the operator picks the right one.
        public bool HasCaseTwin { get; set; }
        public string Country { get; set; } = "";
        public string Type { get; set; } = "";
        public string Packaging { get; set; } = "";
        public string Description { get; set; } = "";
        public string Caution { get; set; } = "";
        public string Instructions { get; set; } = "";
        public string SideEffects { get; set; } = "";
        public string Dosage { get; set; } = "";
        public string Efficacy { get; set; } = "";
        public string PricePerDose { get; set; } = "";
        public string RecommendationForUse { get; set; } = "";
        public string RecommendationforBoosterShot { get; set; } = "";
        public string RecommendationforMixingVaccines { get; set; } = "";
        public string RecommendationforPregnantandLactating { get; set; } = "";
        public string recommendationforimmunocompromisedpatient { get; set; } = "";
        public string recommendationforallergicindividual { get; set; } = "";
        public string contraindication { get; set; } = "";
        public string BrandeNameInEng { get; set; } = "";
        public string TypeInEng { get; set; } = "";
        public string PackingInEng { get; set; } = "";
        public string DescriptionInEng { get; set; } = "";
        public string CautionInEng { get; set; } = "";
        public string InstructionsInEng { get; set; } = "";
        public string SideEffectsInEng { get; set; } = "";
        public string DosageInEng { get; set; } = "";
        public string EfficacyInEng { get; set; } = "";
        public string PricePerDoseInEng { get; set; } = "";
        public string RecommendationForUseInEng { get; set; } = "";
        public string RecommendationforBoosterShotInEng { get; set; } = "";
        public string RecommendationforMixingVaccinesInEng { get; set; } = "";
        public string RecommendationforPregnantandLactatingInEng { get; set; } = "";
        public string recommendationforimmunocompromisedpatientInEng { get; set; } = "";
        public string recommendationforallergicindividualInEng { get; set; } = "";
        public string contraindicationInEng { get; set; } = "";
        public string approvedBy { get; set; } = "";
        public string routeofadministration { get; set; } = "";
        public string routeofadministrationInEng { get; set; } = "";
        public string approvedByInEng { get; set; } = "";
        public string temporaryUseBy { get; set; } = "";
        public string temporaryUseByInEng { get; set; } = "";
        public string Age { get; set; } = "";
        public string AgeInEng { get; set; } = "";
        public string DoseNo { get; set; } = "";
        public string DoseNoInEng { get; set; } = "";
        public string IntervalInEng { get; set; } = "";
        public string Interval { get; set; } = "";
    }

}