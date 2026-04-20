using System;
using System.Net;

namespace BcToolsC.Models
{
    public class TimeoutedWebClient : WebClient
    {
        public int Timeout { get; set; } = 5000;
        protected override WebRequest GetWebRequest(Uri address)
        {
            WebRequest request = base.GetWebRequest(address);
            request.Timeout = Timeout;
            return request;
        }
    }
}