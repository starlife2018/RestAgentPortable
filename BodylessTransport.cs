using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RestAgent
{
    // Framework HttpClientHandler opens a write stream whenever HttpContent exists,
    // even when it only carries Content-Type and has zero bytes. GET/HEAD need
    // headers without a write stream, so use HttpWebRequest's public header API.
    public static class BodylessTransport
    {
        public static async Task<HttpResponseMessage> Send(Request request, Uri uri, CancellationToken token)
        {
            if (request.Method != "GET" && request.Method != "HEAD") throw new ArgumentException("Ожидался метод GET или HEAD.");
            if (!string.IsNullOrEmpty(request.Body)) throw new ArgumentException("Для GET и HEAD оставьте тело запроса пустым.");
            var outgoing = (HttpWebRequest)WebRequest.Create(uri);
            outgoing.Method = request.Method;
            outgoing.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            outgoing.AllowAutoRedirect = false;
            outgoing.CookieContainer = null;
            outgoing.ConnectionGroupName = "RestAgent-" + Guid.NewGuid().ToString("N");
            foreach (var pair in request.Headers.Where(p => p.Enabled && !string.IsNullOrWhiteSpace(p.Name))) AddHeader(outgoing, pair);
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(request.Timeout));
                using (deadline.Token.Register(outgoing.Abort))
                {
                    try
                    {
                        HttpWebResponse incoming;
                        try { incoming = (HttpWebResponse)await outgoing.GetResponseAsync(); }
                        catch (WebException ex)
                        {
                            // HTTP errors are valid responses in an API tester.
                            incoming = ex.Response as HttpWebResponse;
                            if (incoming == null) throw;
                        }
                        using (incoming)
                        using (var body = incoming.GetResponseStream())
                        using (var buffer = new MemoryStream())
                        {
                            var chunk = new byte[8192]; int count;
                            while (body != null && (count = await body.ReadAsync(chunk, 0, chunk.Length, deadline.Token)) != 0)
                            {
                                if (buffer.Length + count > 16 * 1024 * 1024) throw new HttpRequestException("Ответ превышает допустимый размер 16 МиБ.");
                                buffer.Write(chunk, 0, count);
                            }
                            deadline.Token.ThrowIfCancellationRequested();
                            var result = new HttpResponseMessage(incoming.StatusCode) { ReasonPhrase = incoming.StatusDescription, Version = incoming.ProtocolVersion, Content = new ByteArrayContent(buffer.ToArray()) };
                            foreach (string name in incoming.Headers.AllKeys)
                            {
                                var values = incoming.Headers.GetValues(name);
                                if (!result.Headers.TryAddWithoutValidation(name, values)) result.Content.Headers.TryAddWithoutValidation(name, values);
                            }
                            return result;
                        }
                    }
                    catch (Exception)
                    {
                        if (deadline.IsCancellationRequested) throw new OperationCanceledException(deadline.Token);
                        throw;
                    }
                    finally { outgoing.ServicePoint.CloseConnectionGroup(outgoing.ConnectionGroupName); }
                }
            }
        }

        static void AddHeader(HttpWebRequest outgoing, Pair pair)
        {
            string name = pair.Name.Trim(), value = pair.Value ?? "";
            if (!HeaderCatalog.IsValidName(name) || pair.Name.IndexOfAny(new[] { '\r', '\n' }) >= 0 || value.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                throw new ArgumentException("Некорректный заголовок: " + name + ". Имя должно быть без пробелов и переносов строк.");
            switch (name.ToLowerInvariant())
            {
                case "accept": outgoing.Accept = value; break;
                case "content-type": outgoing.ContentType = value; break;
                case "user-agent": outgoing.UserAgent = value; break;
                case "referer": outgoing.Referer = value; break;
                case "host": outgoing.Host = value; break;
                case "date": outgoing.Date = DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AdjustToUniversal); break;
                case "if-modified-since": outgoing.IfModifiedSince = DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AdjustToUniversal); break;
                case "connection":
                    var connections = value.Split(',').Select(v => v.Trim()).ToArray();
                    if (connections.Any(v => v.Equals("close", StringComparison.OrdinalIgnoreCase))) outgoing.KeepAlive = false;
                    else if (connections.Any(v => v.Equals("keep-alive", StringComparison.OrdinalIgnoreCase))) outgoing.KeepAlive = true;
                    var others = connections.Where(v => !v.Equals("close", StringComparison.OrdinalIgnoreCase) && !v.Equals("keep-alive", StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (others.Length > 0) outgoing.Connection = string.Join(", ", others);
                    break;
                case "content-length":
                    long length;
                    if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out length) || length != 0) throw new ArgumentException("Для GET и HEAD без тела Content-Length может быть только 0. Удалите заголовок или укажите 0.");
                    outgoing.ContentLength = 0; break;
                case "transfer-encoding": throw new ArgumentException("Для GET и HEAD без тела удалите Transfer-Encoding.");
                case "expect":
                    if (value.IndexOf("100-continue", StringComparison.OrdinalIgnoreCase) >= 0) throw new ArgumentException("Expect: 100-continue используется при отправке тела. Для GET и HEAD удалите этот заголовок.");
                    outgoing.Expect = value; break;
                case "range": AddRange(outgoing, value); break;
                default: outgoing.Headers.Add(name, value); break;
            }
        }

        static void AddRange(HttpWebRequest outgoing, string value)
        {
            int equals = value.IndexOf('=');
            if (equals <= 0) throw new ArgumentException("Некорректный Range. Пример: bytes=0-1023.");
            string unit = value.Substring(0, equals).Trim();
            foreach (var part in value.Substring(equals + 1).Split(','))
            {
                var bounds = part.Trim().Split('-'); long start, end;
                if (bounds.Length != 2) throw new ArgumentException("Некорректный Range. Пример: bytes=0-1023.");
                if (bounds[0].Length == 0)
                {
                    if (!long.TryParse(bounds[1], NumberStyles.None, CultureInfo.InvariantCulture, out end) || end <= 0) throw new ArgumentException("Некорректный Range.");
                    outgoing.AddRange(unit, -end);
                }
                else
                {
                    if (!long.TryParse(bounds[0], NumberStyles.None, CultureInfo.InvariantCulture, out start)) throw new ArgumentException("Некорректный Range.");
                    if (bounds[1].Length == 0) outgoing.AddRange(unit, start);
                    else
                    {
                        if (!long.TryParse(bounds[1], NumberStyles.None, CultureInfo.InvariantCulture, out end) || end < start) throw new ArgumentException("Некорректный Range.");
                        outgoing.AddRange(unit, start, end);
                    }
                }
            }
        }
    }
}
