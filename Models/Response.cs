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