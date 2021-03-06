using System;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Raven.Client.Client
{
    public class HttpJsonRequest
    {
        public static event EventHandler<WebRequestEventArgs> ConfigureRequest = delegate {  };

        public static HttpJsonRequest CreateHttpJsonRequest(object self, string url, string method, ICredentials credentials)
        {
            var request = new HttpJsonRequest(url, method, credentials);
            ConfigureRequest(self, new WebRequestEventArgs { Request = request.webRequest });
            return request;
        }

        public static HttpJsonRequest CreateHttpJsonRequest(object self, string url, string method, JObject metadata, ICredentials credentials)
        {
            var request = new HttpJsonRequest(url, method, metadata, credentials);
            ConfigureRequest(self, new WebRequestEventArgs { Request = request.webRequest });
            return request;
        }

        private readonly WebRequest webRequest;

        public NameValueCollection ResponseHeaders { get; set; }

        private HttpJsonRequest(string url, string method, ICredentials credentials)
            : this(url, method, new JObject(), credentials)
        {
        }

        private HttpJsonRequest(string url, string method, JObject metadata, ICredentials credentials)
        {
            webRequest = WebRequest.Create(url);
            webRequest.Credentials = credentials;
            WriteMetadata(metadata);
            webRequest.Method = method;
            webRequest.ContentType = "application/json; charset=utf-8";
        }

        public string ReadResponseString()
        {
            WebResponse response;
            try
            {
                response = webRequest.GetResponse();
            }
            catch (WebException e)
            {
                var httpWebResponse = e.Response as HttpWebResponse;
                if (httpWebResponse == null || 
                    httpWebResponse.StatusCode == HttpStatusCode.NotFound ||
                    httpWebResponse.StatusCode == HttpStatusCode.Conflict)
                    throw;
                using (var sr = new StreamReader(e.Response.GetResponseStream()))
                {
                    throw new InvalidOperationException(sr.ReadToEnd(), e);
                }
            }
            ResponseHeaders = response.Headers;
            using (var responseString = response.GetResponseStream())
            {
                var reader = new StreamReader(responseString);
                var text = reader.ReadToEnd();
                reader.Close();
                return text;
            }
        }

        private void WriteMetadata(JObject metadata)
        {
            if (metadata == null || metadata.Count == 0)
            {
                webRequest.ContentLength = 0;
                return;
            }

            foreach (var prop in metadata)
            {
                if (prop.Value == null)
                    continue;

                if (prop.Value.Type == JTokenType.Object ||
                    prop.Value.Type == JTokenType.Array)
                    continue;

                var headerName = prop.Key;
                if (headerName == "ETag")
                    headerName = "If-Match";
                var value = prop.Value.Value<object>().ToString();
                switch (headerName)
                {
                    case "Content-Length":
                        break;
                    case "Content-Type":
                        webRequest.ContentType = value;
                        break;
                    default:
                        webRequest.Headers[headerName] = value;
                        break;
                }
            }
        }

        public void Write(string data)
        {
            var byteArray = Encoding.UTF8.GetBytes(data);

            Write(byteArray);
        }
        public void Write(byte[] byteArray)
        {
            webRequest.ContentLength = byteArray.Length;

            using (var dataStream = webRequest.GetRequestStream())
            {
                dataStream.Write(byteArray, 0, byteArray.Length);
                dataStream.Close();
            }
        }
    }
}