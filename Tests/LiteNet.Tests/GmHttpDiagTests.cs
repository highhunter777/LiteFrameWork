using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using RoomServer;
using RoomServer.Application;
using RoomServer.Diagnostics;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// T0-2e：GM 诊断下载端点——/gm/diag-list 清单与 /gm/diag-fetch 原样回传；
    /// 密钥门、路径安全（分隔符/穿越拒绝）、未命中 404。
    /// </summary>
    public sealed class GmHttpDiagTests
    {
        private static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private static string WriteDiagFile(string dir, string name, byte[] content)
        {
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, name);
            File.WriteAllBytes(path, content);
            return path;
        }

        private static HttpRequestMessage Post(string url, string key, string json)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add(GmHttpListener.KeyHeaderName, key);
            if (json != null)
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            return request;
        }

        [Fact]
        public async Task diag列表_密钥门与文件清单()
        {
            string dir = Path.Combine(Path.GetTempPath(), "gm-diag-" + Guid.NewGuid().ToString("N"));
            try
            {
                WriteDiagFile(dir, "room-a-b-20261021.json.gz", new byte[] { 1, 2, 3 });
                var options = new RoomDiagnosticsOptions(true, dir, 10, 16, 1000, 1000, 10, 4);
                using var service = new RoomDiagnosticsService(options, "build-gm");
                int port = FreePort();
                using var listener = new GmHttpListener(Encoding.UTF8.GetBytes("test-key"),
                    _ => null, () => new ServerHost.RoomSummary[0],
                    diagList: () => service.ListFileSummaries().ToArray(),
                    diagResolve: name => service.ResolveFile(name));
                listener.Start(port, "127.0.0.1");

                using var http = new HttpClient();

                using (var wrong = await http.SendAsync(Post(
                    $"http://127.0.0.1:{port}/gm/diag-list", "wrong-key", null)))
                {
                    Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
                }

                using (var ok = await http.SendAsync(Post(
                    $"http://127.0.0.1:{port}/gm/diag-list", "test-key", null)))
                {
                    Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
                    string body = await ok.Content.ReadAsStringAsync();
                    Assert.Contains("room-a-b-20261021.json.gz", body);
                    Assert.Contains("\"bytes\":3", body);
                }
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public async Task diag下载_命中回传原文_非法名与未命中拒绝()
        {
            string dir = Path.Combine(Path.GetTempPath(), "gm-diag-" + Guid.NewGuid().ToString("N"));
            try
            {
                byte[] content = { 0x52, 0x52, 0x55, 0x4e, 9, 8, 7 };
                WriteDiagFile(dir, "room-x-y-20261021.json.gz", content);
                var options = new RoomDiagnosticsOptions(true, dir, 10, 16, 1000, 1000, 10, 4);
                using var service = new RoomDiagnosticsService(options, "build-gm");
                int port = FreePort();
                using var listener = new GmHttpListener(Encoding.UTF8.GetBytes("test-key"),
                    _ => null, () => new ServerHost.RoomSummary[0],
                    diagList: () => service.ListFileSummaries().ToArray(),
                    diagResolve: name => service.ResolveFile(name));
                listener.Start(port, "127.0.0.1");
                string baseUrl = $"http://127.0.0.1:{port}";

                using var http = new HttpClient();

                using (var ok = await http.SendAsync(Post($"{baseUrl}/gm/diag-fetch", "test-key",
                           "{\"file\":\"room-x-y-20261021.json.gz\"}")))
                {
                    Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
                    Assert.Equal("application/octet-stream", ok.Content.Headers.ContentType.MediaType);
                    Assert.Equal(content, await ok.Content.ReadAsByteArrayAsync());
                }

                using (var traversal = await http.SendAsync(Post($"{baseUrl}/gm/diag-fetch", "test-key",
                           "{\"file\":\"..\\\\secrets.txt\"}")))
                {
                    Assert.Equal(HttpStatusCode.BadRequest, traversal.StatusCode);
                }

                using (var slashed = await http.SendAsync(Post($"{baseUrl}/gm/diag-fetch", "test-key",
                           "{\"file\":\"sub/dir/file.json.gz\"}")))
                {
                    Assert.Equal(HttpStatusCode.BadRequest, slashed.StatusCode);
                }

                using (var missing = await http.SendAsync(Post($"{baseUrl}/gm/diag-fetch", "test-key",
                           "{\"file\":\"no-such.json.gz\"}")))
                {
                    Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
                }
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
