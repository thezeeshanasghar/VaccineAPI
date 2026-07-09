namespace VaccineAPI.Models
{

    public class Response<T>
    {

        public T? ResponseData { get; set; }
        public bool IsSuccess { get; set; }
        public bool IsWarning { get; set; }
        public string? Message { get; set; }
        // Stable machine-readable rule identifier so the client can decide which
        // override to offer without parsing the (human-worded) Message. Null when
        // the response is not a rule violation.
        public string? RuleCode { get; set; }
        // The give was accepted as valid under the CDC 4-day grace period (dose given
        // up to 4 days before its minimum interval). The give still succeeds; the client
        // shows an informational note + CDC link. Never set for ExactIntervalRequired
        // vaccines (cholera/rabies), which keep the strict floor.
        public bool GraceApplied { get; set; }
        public string? GraceMessage { get; set; }

        public Response(bool status, string? message, T? data)
        {
            IsSuccess = status;
            Message = message;
            ResponseData = data;
        }

        public static Response<T> Warning(string message)
        {
            return new Response<T>(false, message, default) { IsWarning = true };
        }

    }

}