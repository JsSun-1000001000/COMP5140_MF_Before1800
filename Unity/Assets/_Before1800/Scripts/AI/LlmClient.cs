using System;
using System.Collections;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace Before1800
{
    public sealed class LlmClient : MonoBehaviour
    {
        public string serviceUrl = "http://127.0.0.1:8000";
        public float timeoutSeconds = 45;
        private CancellationTokenSource inFlight;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private void Awake() { Configure(); }
        public void Configure()
        {
            foreach (var argument in Environment.GetCommandLineArgs())
                if (argument.StartsWith("--service-url=",StringComparison.Ordinal)) serviceUrl = argument.Substring(14).TrimEnd('/');
        }
        private sealed class HttpResult { public string Body, Error; }
        // Local gateway connections bypass proxies without changing OS settings.
        private async Task<HttpResult> Send(string path, object body, float seconds, CancellationToken token)
        {
            try
            {
                var uri = new Uri(serviceUrl + path);
                using (var handler = new HttpClientHandler { UseProxy = !uri.IsLoopback, AllowAutoRedirect = false })
                using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(seconds), MaxResponseContentBufferSize = 2 * 1024 * 1024 })
                using (var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, uri))
                {
                    if (body != null) request.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
                    using (var response = await client.SendAsync(request, token).ConfigureAwait(false))
                    {
                        string raw = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        return new HttpResult { Body = raw, Error = response.IsSuccessStatusCode ? null : "Service request failed (HTTP " + (int)response.StatusCode + ")." };
                    }
                }
            }
            catch (OperationCanceledException) { return new HttpResult { Body = "", Error = "Service request timed out or was cancelled." }; }
            catch (Exception) { return new HttpResult { Body = "", Error = "Cannot connect to the local service. Start it and retry." }; }
        }
        public IEnumerator Health(Action<string, bool> result)
        {
            var task = Send("/health", null, 5, lifetime.Token);
            while (!task.IsCompleted) yield return null;
            if (!lifetime.IsCancellationRequested) result(task.Result.Body, task.Result.Error == null);
        }
        public IEnumerator Decide(DecisionRequest envelope, Action<string,string> result)
        {
            if (inFlight != null) { result(null,"A request is already in progress."); yield break; }
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            inFlight = cancellation;
            try
            {
                var task = Send("/npc/decide", envelope, timeoutSeconds, cancellation.Token);
                while (!task.IsCompleted) yield return null;
                if (inFlight == cancellation) inFlight = null;
                if (!cancellation.IsCancellationRequested) result(task.Result.Body, task.Result.Error);
            }
            finally
            {
                if (inFlight == cancellation) inFlight = null;
                cancellation.Cancel(); cancellation.Dispose();
            }
        }
        public void Abort() { if (inFlight != null) { inFlight.Cancel(); inFlight = null; } }
        public IEnumerator CancelRemote(string sessionId,string requestId)
        {
            var task = Send("/npc/cancel", new { session_id = sessionId, request_id = requestId }, 3, lifetime.Token);
            while (!task.IsCompleted) yield return null;
        }
        private void OnDestroy() { Abort(); lifetime.Cancel(); lifetime.Dispose(); }
    }
}
