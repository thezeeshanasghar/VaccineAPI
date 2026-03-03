namespace VaccineAPI.Models
{

    public class Response<T>
    {

        public T ResponseData { get; set; } = default!;
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = "";

        public Response(bool status, string message, T data)
        {
            IsSuccess = status;
            Message = message;
            ResponseData = data;
        }

    }

}