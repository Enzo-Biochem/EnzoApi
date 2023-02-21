namespace EnzoApi.Models
{
    public class Response<T>
    {
        public Response(T rsp)
        {
            Msg= rsp;
        }
        public T Msg { get; set; }
        public bool Success { get; set; } = false;
    }

    public class Response
    {
        public string Msg { get; set; }
        public bool Success { get; set; } = false;
    }


}
