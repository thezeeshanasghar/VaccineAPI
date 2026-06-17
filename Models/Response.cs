namespace VaccineAPI.Models
{

    public class Response<T>
    {

        public T? ResponseData { get; set; }
        public bool IsSuccess { get; set; }
        public bool IsWarning { get; set; }
        public string? Message { get; set; }

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