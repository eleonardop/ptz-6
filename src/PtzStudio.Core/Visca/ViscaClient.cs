using System.Net;
using System.Net.Sockets;
using PtzStudio.Core.Models;

namespace PtzStudio.Core.Visca;

public enum ConnectionState { Desconectado, Conectando, Conectado, ErroDeComunicacao }

public interface IViscaSender
{
    void Send(byte[] payload);
}

/// <summary>
/// Cliente VISCA sobre UDP: envia comandos, recebe respostas e monitora a conexão.
/// Não guarda histórico de comandos (TX/RX); apenas avisa (ErrorReported) quando há erro de comunicação.
/// </summary>
public sealed class ViscaClient : IViscaSender, IDisposable
{
    private readonly object _gate = new();
    private readonly object _errLock = new();
    private string _lastErr = "";
    private DateTime _lastErrAt = DateTime.MinValue;
    private UdpClient? _udp;
    private CameraConfig _cfg = new();
    private CancellationTokenSource? _cts;
    private uint _seq;
    private DateTime _probeSentUtc;
    private TaskCompletionSource<bool>? _probe;
    private int _failures;
    private TaskCompletionSource<ViscaResponse>? _inq;
    private readonly SemaphoreSlim _probeGate = new(1, 1);

    public ConnectionState State { get; private set; } = ConnectionState.Desconectado;
    public string StateDetail { get; private set; } = "Sem conexão";
    public double LastRoundTripMs { get; private set; } = -1;
    public event Action<ConnectionState, string>? StateChanged;
    public event Action<string>? ErrorReported;
    public event Action<ViscaResponse>? ResponseReceived;

    public void Connect(CameraConfig cfg)
    {
        lock (_gate)
        {
            Close();
            _cfg = cfg.Clone();
            _failures = 0;
            SetState(ConnectionState.Conectando, $"Conectando a {cfg.IpAddress}:{cfg.Port}...");
            try
            {
                OpenSocket();
            }
            catch (Exception e)
            {
                ReportError("Falha ao abrir socket: " + e.Message);
                SetState(ConnectionState.ErroDeComunicacao, e.Message);
                return;
            }
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _ = Task.Run(() => HealthLoop(ct));
        }
    }

    private void OpenSocket()
    {
        // Só endereço IP (nada de nome de máquina): resolver nomes (DNS) travaria a tela por vários segundos.
        if (!IPAddress.TryParse((_cfg.IpAddress ?? "").Trim(), out var ip))
            throw new ArgumentException($"Endereço IP inválido: \"{_cfg.IpAddress}\". Use o formato 192.168.1.100.");
        _udp = new UdpClient(ip.AddressFamily);
        // Windows: sem isto, um ICMP "porta inalcançável" faz o PRÓXIMO envio/recepção falhar (SIO_UDP_CONNRESET).
        try { _udp.Client.IOControl(-1744830452, new byte[4], null); } catch { }
        _udp.Connect(ip, _cfg.Port);
        var udp = _udp;
        _ = Task.Run(() => ReceiveLoop(udp));
    }

    private void Close()
    {
        try { _cts?.Cancel(); } catch { }
        try { _udp?.Close(); } catch { }
        _udp = null;
        _cts = null;
    }

    public void Send(byte[] payload) => SendRaw(payload, ViscaPayloadType.Command);

    private void SendRaw(byte[] payload, ViscaPayloadType type)
    {
        UdpClient? udp;
        byte[] data;
        lock (_gate)
        {
            udp = _udp;
            if (udp == null) return;
            data = _cfg.Transport == ViscaTransportMode.ViscaOverIp ? ViscaFraming.Wrap(payload, _seq++, type) : payload;
        }
        try
        {
            udp.Send(data, data.Length);
        }
        catch (Exception e)
        {
            ReportError("Envio falhou: " + e.Message);
            SetState(ConnectionState.ErroDeComunicacao, e.Message);
        }
    }

    private async Task ReceiveLoop(UdpClient udp)
    {
        while (true)
        {
            try
            {
                var r = await udp.ReceiveAsync().ConfigureAwait(false);
                var resp = ViscaResponse.Parse(r.Buffer, _cfg.Transport);
                var inq = _inq;
                if (inq != null && (resp.Kind == ViscaResponseKind.Inquerito || resp.Kind == ViscaResponseKind.Erro)) inq.TrySetResult(resp);
                var probe = _probe;
                if (probe != null)
                {
                    LastRoundTripMs = (DateTime.UtcNow - _probeSentUtc).TotalMilliseconds;
                    probe.TrySetResult(true);
                }
                _failures = 0;
                if (State != ConnectionState.Conectado) SetState(ConnectionState.Conectado, "Câmera respondendo");
                ResponseReceived?.Invoke(resp);
            }
            catch (ObjectDisposedException) { return; }
            catch (SocketException e)
            {
                // Em UDP, "porta inalcançável" aparece aqui quando o destino recusa o pacote.
                ReportError("Recepção: " + e.Message);
                await Task.Delay(500).ConfigureAwait(false);
                if (udp != _udp) return;
            }
            catch (Exception e)
            {
                ReportError("Recepção: " + e.Message);
                return;
            }
        }
    }

    /// <summary>Envia uma consulta de versão e espera qualquer resposta dentro do timeout configurado.</summary>
    public async Task<bool> TestConnectionAsync()
    {
        if (_udp == null) return false;
        await _probeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _probe = tcs;
            _probeSentUtc = DateTime.UtcNow;
            SendRaw(ViscaCommands.VersionInquiry(_cfg.ViscaAddress), ViscaPayloadType.Inquiry);
            var done = await Task.WhenAny(tcs.Task, Task.Delay(_cfg.TimeoutMs)).ConfigureAwait(false);
            _probe = null;
            bool ok = done == tcs.Task && tcs.Task.Result;
            if (!ok) LastRoundTripMs = -1;
            return ok;
        }
        finally { _probeGate.Release(); }
    }

    /// <summary>
    /// Envia uma consulta VISCA e devolve a resposta, ou null se a câmera não responder no prazo.
    /// Uma consulta por vez (compartilha a trava com o teste de conexão, para as respostas não se misturarem).
    /// </summary>
    public async Task<ViscaResponse?> InquireAsync(byte[] inquiry, int timeoutMs = 0)
    {
        if (_udp == null) return null;
        int wait = timeoutMs > 0 ? timeoutMs : _cfg.TimeoutMs;
        await _probeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var tcs = new TaskCompletionSource<ViscaResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            _inq = tcs;
            SendRaw(inquiry, ViscaPayloadType.Inquiry);
            var done = await Task.WhenAny(tcs.Task, Task.Delay(wait)).ConfigureAwait(false);
            return done == tcs.Task ? tcs.Task.Result : null;
        }
        finally { _inq = null; _probeGate.Release(); }
    }

    private async Task HealthLoop(CancellationToken ct)
    {
        try { await Task.Delay(1500, ct).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_cfg.HealthCheck)
                {
                    bool ok;
                    lock (_gate) { if (_udp == null) { try { OpenSocket(); } catch { } } }
                    ok = await TestConnectionAsync().ConfigureAwait(false);
                    if (!ok)
                    {
                        _failures++;
                        if (_failures >= Math.Max(1, _cfg.ReconnectAttempts))
                            SetState(ConnectionState.Desconectado, "Sem resposta da câmera (tentando reconectar...)");
                        else
                            SetState(ConnectionState.ErroDeComunicacao, $"Sem resposta ({_failures}/{_cfg.ReconnectAttempts})");
                    }
                }
                await Task.Delay(3000, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e) { ReportError("Monitor: " + e.Message); }
        }
    }

    private void SetState(ConnectionState s, string detail)
    {
        bool changed = s != State;
        State = s;
        StateDetail = detail;
        if (changed) StateChanged?.Invoke(s, detail);
    }

    /// <summary>Registra um erro de comunicação. Erros idênticos e repetidos em menos de 5 s não se acumulam.</summary>
    private void ReportError(string text)
    {
        bool raise;
        lock (_errLock)
        {
            raise = text != _lastErr || (DateTime.UtcNow - _lastErrAt).TotalSeconds > 5;
            if (raise)
            {
                _lastErr = text; _lastErrAt = DateTime.UtcNow;
            }
        }
        if (raise) ErrorReported?.Invoke(text);
    }

    public void Dispose() { lock (_gate) Close(); }
}
