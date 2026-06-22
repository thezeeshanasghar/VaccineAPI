using System;
using System.IO;

namespace VaccineAPI.helper
{
    public static class ReportFileName
    {
        public static string Build(string reportType, string clinicName)
        {
            var safeClinicName = string.Join("_", (clinicName ?? "Unknown_Clinic").Split(Path.GetInvalidFileNameChars()))
                .Replace(' ', '_');
            return $"{reportType}_{safeClinicName}_{DateTime.Today:dd-MM-yyyy}.pdf";
        }
    }
}
