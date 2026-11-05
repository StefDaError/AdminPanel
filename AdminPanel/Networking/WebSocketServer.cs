using AdminPanel.Database;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;

namespace AdminPanel.Networking;

public sealed class WebSocketServer
{
    private readonly DatabaseService _db;
    private readonly int _port;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    public event Action? AgentConnected;
    public event Action<string>? Error;

    public WebSocketServer(DatabaseService db, int port = 9000)
    {
        _db = db;
        _port = port;
    }

    public void Start()
    {
        if (_listener != null) return;

        _listener = new HttpListener();

        // Используем "+", чтобы принимать подключения по любому IP-адресу
        _listener.Prefixes.Add($"http://+:{_port}/");

        try
        {
            _listener.Start();
        }
        catch (HttpListenerException ex) when (ex.ErrorCode == 5) // 5 = Access Denied
        {
            // Если нет прав администратора, откатываемся на localhost
            _listener.Prefixes.Clear();
            _listener.Prefixes.Add($"http://localhost:{_port}/");
            _listener.Start();

            Error?.Invoke("Нет прав для сети. Сервер работает только для localhost. Запустите программу от имени Администратора.");
        }
        catch (Exception ex)
        {
            Error?.Invoke($"Не удалось запустить сервер: {ex.Message}");
            return;
        }

        _cts = new CancellationTokenSource();
        _ = Task.Run(() => AcceptLoop(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        _listener = null;
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch { break; }

            if (!context.Request.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
                continue;
            }

            try
            {
                var wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
                _ = Task.Run(() => HandleClient(wsContext.WebSocket, context, ct));
            }
            catch (Exception ex)
            {
                Error?.Invoke($"Ошибка Accept: {ex.Message}");
            }
        }
    }

    private async Task HandleClient(WebSocket ws, HttpListenerContext context, CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var ms = new MemoryStream();

        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", ct);
                    break;
                }

                ms.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;

                var json = Encoding.UTF8.GetString(ms.ToArray());
                ms.SetLength(0);

                var remoteIp = context.Request.RemoteEndPoint?.Address.ToString() ?? "unknown";
                _db.SaveFromJson(json, remoteIp, "windows");

                AgentConnected?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Error?.Invoke($"Ошибка клиента: {ex.Message}");
        }
        finally
        {
            ws.Dispose();
        }
    }
}