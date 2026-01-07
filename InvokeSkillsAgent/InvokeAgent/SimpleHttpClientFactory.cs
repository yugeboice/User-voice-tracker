namespace InvokeAgent
{
    internal class SimpleHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            // Each call returns a *new* HttpClient.
            // No pooling, no handlers reuse.
            return new HttpClient();
        }
    }
}
