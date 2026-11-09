#nullable disable
namespace TextSenderStudio.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

public sealed class LanControlRequest
{
    public string Kind { get; set; }
    public string Name { get; set; }
    public string Project { get; set; }
}

public sealed class LanControlResponse
{
    public int StatusCode { get; set; }
    public object Body { get; set; }
    public LanControlResponse() { }
    public LanControlResponse(int statusCode, object body) { StatusCode = statusCode; Body = body; }
}

// An explicit LAN endpoint for named, locally configured actions. This transport
// never executes scripts or accepts a command line, file path, or phrase content.
public sealed class LanControlServer : IDisposable
{
    const int MaximumClients = 8, MaximumHeaders = 8192, MaximumBody = 4096;
    const int ReadDeadlineMs = 5000;
    readonly object gate = new object();
    readonly IPAddress address;
    readonly int requestedPort;
    readonly Func<string, LanControlRequest, Task<LanControlResponse>> handler;
    readonly HashSet<TcpClient> clients = new HashSet<TcpClient>();
    TcpListener listener;
    bool running, disposed;
    int generation, boundPort;

    public int Port { get { lock (gate) return boundPort; } }
    public bool IsRunning { get { lock (gate) return running; } }

    public LanControlServer(IPAddress address, int port,
        Func<string, LanControlRequest, Task<LanControlResponse>> handler)
    {
        if (!IsLocalAddress(address)) throw new ArgumentException("请选择明确的局域网 IPv4 地址。", "address");
        if (port < 0 || port > 65535) throw new ArgumentOutOfRangeException("port");
        if (handler == null) throw new ArgumentNullException("handler");
        this.address = address; requestedPort = port; this.handler = handler;
    }

    public static bool IsLocalAddress(IPAddress value)
    {
        if (value == null || value.AddressFamily != AddressFamily.InterNetwork) return false;
        byte[] bytes = value.GetAddressBytes();
        return bytes[0] == 127 || bytes[0] == 10 ||
            (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
            (bytes[0] == 192 && bytes[1] == 168);
    }

    public void Start()
    {
        lock (gate) {
            if (disposed) throw new ObjectDisposedException("LanControlServer");
            if (running) return;
            var next = new TcpListener(address, requestedPort);
            next.ExclusiveAddressUse = true;
            try { next.Start(MaximumClients); }
            catch { next.Stop(); throw; }
            listener = next;
            boundPort = ((IPEndPoint)next.LocalEndpoint).Port;
            running = true;
            int currentGeneration = ++generation, currentPort = boundPort;
            var worker = new Thread(delegate() { Accept(next, currentGeneration, currentPort); });
            worker.Name = "TextSender LAN listener"; worker.IsBackground = true; worker.Start();
        }
    }

    bool Active(int expectedGeneration)
    {
        lock (gate) return running && generation == expectedGeneration;
    }

    void Accept(TcpListener current, int expectedGeneration, int currentPort)
    {
        while (Active(expectedGeneration)) {
            TcpClient client;
            try { client = current.AcceptTcpClient(); }
            catch (SocketException) { break; }
            catch (ObjectDisposedException) { break; }
            bool accepted;
            lock (gate) {
                accepted = running && generation == expectedGeneration && clients.Count < MaximumClients;
                if (accepted) clients.Add(client);
            }
            if (!accepted) { client.Close(); continue; }
            ThreadPool.QueueUserWorkItem(delegate { Serve(client, expectedGeneration, currentPort); });
        }
    }

    void Serve(TcpClient client, int expectedGeneration, int currentPort)
    {
        try {
            if (!Active(expectedGeneration)) return;
            var peer = client.Client.RemoteEndPoint as IPEndPoint;
            if (peer == null || !IsLocalAddress(peer.Address)) return;
            client.NoDelay = true;
            using (NetworkStream stream = client.GetStream()) {
                stream.WriteTimeout = 2000;
                LanControlResponse response;
                try {
                    string route;
                    LanControlRequest request = ReadRequest(stream, currentPort, out route);
                    if (!Active(expectedGeneration)) return;
                    // UI handlers recheck their activation generation before
                    // dispatch, so closing the server also cancels queued work.
                    Task<LanControlResponse> task = handler(route, request);
                    if (task == null) throw new InvalidOperationException();
                    response = task.GetAwaiter().GetResult();
                    if (response == null) throw new InvalidOperationException();
                } catch (HttpFailure error) {
                    response = Error(error.Code, error.Message);
                } catch (IOException) {
                    response = Error(408, "请求读取超时或连接已关闭。");
                } catch (Exception) {
                    response = Error(500, "请求处理失败。");
                }
                if (Active(expectedGeneration)) {
                    WriteResponse(stream, response);
                    // Preserve error responses when rejecting headers before a
                    // caller has finished sending its body. Closing with unread
                    // TCP data can otherwise reset the connection on Windows.
                    client.Client.Shutdown(SocketShutdown.Send);
                    Drain(stream);
                }
            }
        } catch (IOException) { }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
        catch (Exception) { }
        finally {
            client.Close();
            lock (gate) clients.Remove(client);
        }
    }

    LanControlRequest ReadRequest(NetworkStream stream, int currentPort, out string route)
    {
        route = null;
        var elapsed = Stopwatch.StartNew();
        var bytes = new List<byte>();
        while (true) {
            SetReadTimeout(stream, elapsed);
            int value = stream.ReadByte();
            if (value < 0) throw new HttpFailure(400, "HTTP 请求不完整。");
            if (value > 127 || value == 0) throw new HttpFailure(400, "HTTP 请求头无效。");
            bytes.Add((byte)value);
            int count = bytes.Count;
            if (count > MaximumHeaders) throw new HttpFailure(431, "HTTP 请求头过长。");
            if (count >= 4 && bytes[count - 4] == 13 && bytes[count - 3] == 10 &&
                bytes[count - 2] == 13 && bytes[count - 1] == 10) break;
        }
        string[] lines = Encoding.ASCII.GetString(bytes.ToArray()).Split(new string[] { "\r\n" }, StringSplitOptions.None);
        string[] first = lines[0].Split(' ');
        if (first.Length != 3 || (first[2] != "HTTP/1.1" && first[2] != "HTTP/1.0"))
            throw new HttpFailure(400, "HTTP 请求行无效。");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 1; index < lines.Length - 2; index++) {
            string line = lines[index];
            int colon = line.IndexOf(':');
            if (colon <= 0 || Char.IsWhiteSpace(line[0])) throw new HttpFailure(400, "HTTP 请求头无效。");
            string name = line.Substring(0, colon);
            foreach (char character in name)
                if (!((character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z') ||
                    (character >= '0' && character <= '9') || character == '-')) throw new HttpFailure(400, "HTTP 请求头无效。");
            if (headers.ContainsKey(name)) throw new HttpFailure(400, "HTTP 请求头重复。");
            headers.Add(name, line.Substring(colon + 1).Trim());
        }
        string host;
        if (!headers.TryGetValue("Host", out host) || !AllowedAuthority(host, currentPort))
            throw new HttpFailure(403, "请使用程序显示的 IP 地址和端口。");
        string origin;
        if (headers.TryGetValue("Origin", out origin)) {
            Uri uri;
            if (!Uri.TryCreate(origin, UriKind.Absolute, out uri) || uri.Scheme != "http" ||
                uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
                !AllowedAuthority(uri.Authority, currentPort)) throw new HttpFailure(403, "不接受其他网页发起的请求。");
        }
        if (headers.ContainsKey("Transfer-Encoding"))
            throw new HttpFailure(400, "请使用 Content-Length 发送完整请求。");
        string expectation;
        bool continueExpected = headers.TryGetValue("Expect", out expectation);
        if (continueExpected && !String.Equals(expectation, "100-continue", StringComparison.OrdinalIgnoreCase))
            throw new HttpFailure(417, "不支持此 Expect 请求头。");
        string lengthText;
        int length = 0;
        bool hasLength = headers.TryGetValue("Content-Length", out lengthText);
        if (hasLength && (!Int32.TryParse(lengthText, NumberStyles.None, CultureInfo.InvariantCulture, out length) || length > MaximumBody))
            throw new HttpFailure(413, "请求体过长或长度无效。");
        if (first[1] == "/api/items") {
            if (first[0] != "GET") throw new HttpFailure(405, "此接口使用 GET。");
            if (length != 0) throw new HttpFailure(400, "此接口不接受请求体。");
            route = "items"; return null;
        }
        if (first[1] != "/api/run") throw new HttpFailure(404, "接口不存在。");
        if (first[0] != "POST") throw new HttpFailure(405, "此接口使用 POST。");
        string contentType;
        if (!headers.TryGetValue("Content-Type", out contentType) ||
            !String.Equals(contentType.Split(';')[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase))
            throw new HttpFailure(415, "请使用 application/json。");
        if (!hasLength || length == 0) throw new HttpFailure(400, "请提供 JSON 请求体和 Content-Length。");
        if (continueExpected) {
            byte[] interim = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");
            stream.Write(interim, 0, interim.Length);
        }
        var body = new byte[length];
        int received = 0;
        while (received < length) {
            SetReadTimeout(stream, elapsed);
            int count = stream.Read(body, received, length - received);
            if (count == 0) throw new HttpFailure(400, "请求体不完整。");
            received += count;
        }
        Dictionary<string, object> values;
        try {
            string json = new UTF8Encoding(false, true).GetString(body);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, new JsonSerializerOptions { MaxDepth = 8 }); values = parsed?.ToDictionary(pair => pair.Key, pair => pair.Value.ValueKind == JsonValueKind.String ? (object)pair.Value.GetString() : pair.Value);
        } catch (Exception) { throw new HttpFailure(400, "JSON 请求体无效。"); }
        if (values == null) throw new HttpFailure(400, "JSON 请求体必须是对象。");
        foreach (string name in values.Keys)
            if (name != "kind" && name != "name" && name != "project") throw new HttpFailure(400, "不支持的请求字段：" + name);
        string kind = ReadString(values, "kind", true), actionName = ReadString(values, "name", true);
        string project = ReadString(values, "project", false);
        if (kind != "phrase" && kind != "script") throw new HttpFailure(400, "kind 必须为 phrase 或 script。");
        route = "run";
        return new LanControlRequest { Kind = kind, Name = actionName, Project = project };
    }

    bool AllowedAuthority(string authority, int port)
    {
        string suffix = ":" + port.ToString(CultureInfo.InvariantCulture);
        return String.Equals(authority, address.ToString() + suffix, StringComparison.OrdinalIgnoreCase) ||
            (port == 80 && String.Equals(authority, address.ToString(), StringComparison.OrdinalIgnoreCase)) ||
            (IPAddress.IsLoopback(address) && (String.Equals(authority, "localhost" + suffix, StringComparison.OrdinalIgnoreCase) ||
                (port == 80 && String.Equals(authority, "localhost", StringComparison.OrdinalIgnoreCase))));
    }

    static string ReadString(Dictionary<string, object> values, string name, bool required)
    {
        object value;
        if (!values.TryGetValue(name, out value)) {
            if (required) throw new HttpFailure(400, "缺少字段：" + name);
            return null;
        }
        string text = value as string;
        if (text == null || String.IsNullOrWhiteSpace(text) || text.Length > 256)
            throw new HttpFailure(400, "字段无效：" + name);
        foreach (char character in text)
            if (Char.IsControl(character)) throw new HttpFailure(400, "字段包含控制字符：" + name);
        return text;
    }

    static void SetReadTimeout(NetworkStream stream, Stopwatch elapsed)
    {
        int remaining = ReadDeadlineMs - (int)elapsed.ElapsedMilliseconds;
        if (remaining <= 0) throw new HttpFailure(408, "请求读取超时。");
        stream.ReadTimeout = remaining;
    }

    static LanControlResponse Error(int code, string message)
    {
        return new LanControlResponse(code, new { ok = false, error = message });
    }

    static void WriteResponse(NetworkStream stream, LanControlResponse response)
    {
        int code = response.StatusCode;
        if (code < 200 || code > 599) throw new IOException("Invalid response status");
        string reason = code == 200 ? "OK" : code == 202 ? "Accepted" : "Error";
        byte[] body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response.Body));
        byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 " + code + " " + reason + "\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: " +
            body.Length.ToString(CultureInfo.InvariantCulture) + "\r\nConnection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n\r\n");
        stream.Write(header, 0, header.Length); stream.Write(body, 0, body.Length);
    }

    static void Drain(NetworkStream stream)
    {
        var timer = Stopwatch.StartNew();
        var buffer = new byte[1024];
        int total = 0;
        try {
            while (total < MaximumHeaders + MaximumBody && timer.ElapsedMilliseconds < 200) {
                stream.ReadTimeout = Math.Max(1, 200 - (int)timer.ElapsedMilliseconds);
                int count = stream.Read(buffer, 0, buffer.Length);
                if (count == 0) break;
                total += count;
            }
        } catch (IOException) { }
    }

    public void Stop()
    {
        TcpClient[] closing;
        lock (gate) {
            running = false; generation++;
            if (listener != null) { listener.Stop(); listener = null; }
            closing = new TcpClient[clients.Count]; clients.CopyTo(closing); clients.Clear();
        }
        foreach (TcpClient client in closing) client.Close();
    }

    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; }
        Stop();
    }

    sealed class HttpFailure : Exception
    {
        public readonly int Code;
        public HttpFailure(int code, string message) : base(message) { Code = code; }
    }
}
