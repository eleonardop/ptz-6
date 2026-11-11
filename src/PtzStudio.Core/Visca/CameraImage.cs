using PtzStudio.Core.Models;

namespace PtzStudio.Core.Visca;

public enum ImageParam { Brilho, Exposicao }
public enum ImageSupport { Desconhecido, Suportado, NaoSuportado }

/// <summary>Estado de um parâmetro de imagem, como a câmera informou.</summary>
public sealed class ImageParamState
{
    public ImageSupport Support { get; internal set; } = ImageSupport.Desconhecido;
    /// <summary>Valor atual (lido da câmera; entre uma leitura e outra, o último valor enviado).</summary>
    public int Value { get; internal set; }
    public bool HasValue { get; internal set; }
    public int Min { get; internal set; }
    public int Max { get; internal set; }
    /// <summary>Valor quando a câmera foi detectada (usado por "valor inicial").</summary>
    public int? Initial { get; internal set; }
    /// <summary>Explicação curta quando não está disponível.</summary>
    public string Note { get; internal set; } = "";
}

/// <summary>
/// Brilho e exposição da câmera, pela interface e pelos botões do controle: os dois usam estes mesmos métodos, então os valores
/// ficam sincronizados. Nada é presumido: o suporte é descoberto perguntando à câmera (consultas VISCA) e, se ela não responder
/// ou recusar, o parâmetro fica indisponível (nenhum comando é enviado).
///   Brilho   = "ajuste de vídeo" da câmera (VISCA 04 A1), valor absoluto; a faixa real varia por câmera e é aprendida ao usar.
///   Exposição = compensação de exposição (VISCA 04 3E/0E/4E), 0..14 com 7 = 0 EV.
/// </summary>
public sealed class CameraImage
{
    private readonly ViscaClient _client;
    private readonly Func<CameraConfig> _cam;
    private readonly object _l = new();
    private readonly ImageParamState _bright = new() { Min = 0, Max = 14 };
    private readonly ImageParamState _expo = new() { Min = 0, Max = 14 };
    private readonly DateTime[] _readDue = new DateTime[2];
    private readonly bool[] _reading = new bool[2];
    private readonly int[] _lastSent = { -1, -1 };
    private readonly int[] _holdDir = new int[2];
    private readonly DateTime[] _holdSince = new DateTime[2];
    private readonly DateTime[] _lastStep = new DateTime[2];
    private DateTime _lastWarn = DateTime.MinValue;
    private bool _expOn;
    private int _version;

    public CameraImage(ViscaClient client, Func<CameraConfig> cam) { _client = client; _cam = cam; }

    public ImageParamState Brilho => _bright;
    public ImageParamState Exposicao => _expo;
    public ImageParamState Get(ImageParam p) => p == ImageParam.Brilho ? _bright : _expo;
    public bool Detecting { get; private set; }
    public bool Detected { get; private set; }
    /// <summary>Muda a cada atualização: a tela usa para saber quando redesenhar.</summary>
    public int Version => Volatile.Read(ref _version);
    /// <summary>Avisos curtos (ex.: parâmetro indisponível ao usar o botão do controle).</summary>
    public event Action<string>? Notice;

    private void Bump() => Interlocked.Increment(ref _version);

    // ---------------- detecção ----------------
    public async Task DetectAsync()
    {
        if (Detecting) return;
        if (_client.State != ConnectionState.Conectado)
        {
            lock (_l) { _bright.Note = _expo.Note = "Conecte a câmera para detectar os ajustes de imagem."; }
            Bump();
            return;
        }
        Detecting = true; Bump();
        try
        {
            var cam = _cam();
            int wait = Math.Max(800, cam.TimeoutMs);

            // Brilho
            var rb = await _client.InquireAsync(ViscaCommands.BrightnessInquiry(cam.ViscaAddress), wait).ConfigureAwait(false);
            lock (_l)
            {
                if (rb != null && rb.Kind == ViscaResponseKind.Inquerito && ViscaCommands.TryReadValue(rb.Payload, out int bv))
                {
                    _bright.Support = ImageSupport.Suportado; _bright.Value = bv; _bright.HasValue = true; _bright.Initial ??= bv;
                    _bright.Max = Math.Max(_bright.Max, bv); _bright.Note = "";
                }
                else
                {
                    _bright.Support = ImageSupport.NaoSuportado; _bright.HasValue = false;
                    _bright.Note = rb == null ? "A câmera não respondeu à consulta de brilho." : "A câmera recusou a consulta de brilho (recurso não disponível).";
                }
            }

            // Exposição (compensação)
            var rp = await _client.InquireAsync(ViscaCommands.ExpCompPowerInquiry(cam.ViscaAddress), wait).ConfigureAwait(false);
            bool powerOk = rp != null && rp.Kind == ViscaResponseKind.Inquerito && ViscaCommands.TryReadOnOff(rp.Payload, out _expOn);
            if (powerOk)
            {
                var rv = await _client.InquireAsync(ViscaCommands.ExpCompPositionInquiry(cam.ViscaAddress), wait).ConfigureAwait(false);
                lock (_l)
                {
                    _expo.Support = ImageSupport.Suportado; _expo.Note = "";
                    if (rv != null && rv.Kind == ViscaResponseKind.Inquerito && ViscaCommands.TryReadValue(rv.Payload, out int ev))
                    { _expo.Value = Math.Clamp(ev, 0, 14); _expo.HasValue = true; _expo.Initial ??= _expo.Value; }
                    else { _expo.Value = 7; _expo.HasValue = true; _expo.Initial ??= 7; }   // compensação desligada: 0 EV
                }
            }
            else
            {
                lock (_l)
                {
                    _expo.Support = ImageSupport.NaoSuportado; _expo.HasValue = false;
                    _expo.Note = rp == null ? "A câmera não respondeu à consulta de exposição." : "A câmera recusou a consulta de exposição (recurso não disponível).";
                }
            }
            Detected = true;
        }
        finally { Detecting = false; Bump(); }
    }

    /// <summary>Esquece o que foi detectado (ao trocar de câmera).</summary>
    public void Reset()
    {
        lock (_l)
        {
            foreach (var s in new[] { _bright, _expo }) { s.Support = ImageSupport.Desconhecido; s.HasValue = false; s.Initial = null; s.Note = ""; }
            _bright.Max = 14; _expo.Max = 14; _expOn = false; Detected = false;
            Array.Clear(_holdDir); Array.Clear(_lastSent); _lastSent[0] = _lastSent[1] = -1;
        }
        Bump();
    }

    // ---------------- comandos ----------------
    private bool Usable(ImageParam p)
    {
        var st = Get(p);
        if (st.Support == ImageSupport.Suportado && st.HasValue) return true;
        WarnOnce(p, st);
        return false;
    }

    private void WarnOnce(ImageParam p, ImageParamState st)
    {
        if ((DateTime.UtcNow - _lastWarn).TotalSeconds < 6) return;
        _lastWarn = DateTime.UtcNow;
        string name = p == ImageParam.Brilho ? "Brilho" : "Exposição";
        Notice?.Invoke(st.Support == ImageSupport.NaoSuportado
            ? $"{name}: a câmera não confirmou este recurso. Nada foi enviado."
            : $"{name}: ainda não detectado. Abra Controle PTZ > Imagem e clique em Detectar.");
    }

    /// <summary>Um passo (dir > 0 aumenta, dir &lt; 0 diminui).</summary>
    public void Step(ImageParam p, int dir)
    {
        if (dir == 0 || !Usable(p)) return;
        var cam = _cam();
        lock (_l)
        {
            var st = Get(p);
            int next = Math.Clamp(st.Value + Math.Sign(dir), st.Min, p == ImageParam.Brilho ? 255 : st.Max);
            if (p == ImageParam.Brilho)
            {
                _client.Send(ViscaCommands.BrightnessDirect(cam.ViscaAddress, next));
            }
            else
            {
                EnsureExpOn(cam);
                _client.Send(ViscaCommands.ExpCompStep(cam.ViscaAddress, Math.Sign(dir)));
            }
            st.Value = next; _lastSent[(int)p] = next;
            _readDue[(int)p] = DateTime.UtcNow.AddMilliseconds(450);
        }
        Bump();
    }

    /// <summary>Valor absoluto (controle deslizante da tela).</summary>
    public void SetValue(ImageParam p, int value)
    {
        if (!Usable(p)) return;
        var cam = _cam();
        lock (_l)
        {
            var st = Get(p);
            int v = Math.Clamp(value, st.Min, p == ImageParam.Brilho ? 255 : st.Max);
            if (p == ImageParam.Brilho) _client.Send(ViscaCommands.BrightnessDirect(cam.ViscaAddress, v));
            else { EnsureExpOn(cam); _client.Send(ViscaCommands.ExpCompDirect(cam.ViscaAddress, v)); }
            st.Value = v; _lastSent[(int)p] = v;
            _readDue[(int)p] = DateTime.UtcNow.AddMilliseconds(450);
        }
        Bump();
    }

    /// <summary>Volta ao valor que a câmera tinha quando foi detectada (exposição: 0 EV).</summary>
    public void RestoreInitial(ImageParam p)
    {
        if (!Usable(p)) return;
        var cam = _cam();
        lock (_l)
        {
            var st = Get(p);
            if (p == ImageParam.Exposicao)
            {
                EnsureExpOn(cam);
                _client.Send(ViscaCommands.ExpCompStep(cam.ViscaAddress, 0));
                st.Value = 7;
            }
            else if (st.Initial is int init)
            {
                _client.Send(ViscaCommands.BrightnessDirect(cam.ViscaAddress, init));
                st.Value = init;
            }
            _lastSent[(int)p] = st.Value;
            _readDue[(int)p] = DateTime.UtcNow.AddMilliseconds(450);
        }
        Bump();
    }

    private void EnsureExpOn(CameraConfig cam)
    {
        if (_expOn) return;
        _client.Send(ViscaCommands.ExpCompPower(cam.ViscaAddress, true));   // com a compensação desligada o ajuste não tem efeito
        _expOn = true;
    }

    // ---------------- segurar o botão do controle ----------------
    /// <summary>dir: -1, 0 ou +1 (botão do controle). Chamado a cada ciclo; o primeiro passo é imediato, depois repete.</summary>
    public void SetHold(ImageParam p, int dir, DateTime now)
    {
        int i = (int)p; dir = Math.Sign(dir);
        if (dir == _holdDir[i]) return;
        _holdDir[i] = dir;
        if (dir != 0) { _holdSince[i] = now; _lastStep[i] = now; Step(p, dir); }
        else _readDue[i] = now.AddMilliseconds(250);          // soltou: confere o valor final na câmera
    }

    /// <summary>Repete o ajuste enquanto o botão está apertado e confere os valores na câmera depois de mexer.</summary>
    public void Tick(DateTime now)
    {
        for (int i = 0; i < 2; i++)
        {
            var p = (ImageParam)i;
            if (_holdDir[i] != 0 && (now - _holdSince[i]).TotalMilliseconds > 400 && (now - _lastStep[i]).TotalMilliseconds >= 140)
            {
                _lastStep[i] = now;
                Step(p, _holdDir[i]);
            }
            bool due;
            lock (_l) due = _readDue[i] != default && now >= _readDue[i] && !_reading[i] && _holdDir[i] == 0;
            if (due) _ = ReadBackAsync(p);
        }
    }

    private async Task ReadBackAsync(ImageParam p)
    {
        int i = (int)p;
        lock (_l) { _reading[i] = true; _readDue[i] = default; }
        try
        {
            var cam = _cam();
            var inq = p == ImageParam.Brilho ? ViscaCommands.BrightnessInquiry(cam.ViscaAddress) : ViscaCommands.ExpCompPositionInquiry(cam.ViscaAddress);
            var r = await _client.InquireAsync(inq, 800).ConfigureAwait(false);
            if (r == null || r.Kind != ViscaResponseKind.Inquerito || !ViscaCommands.TryReadValue(r.Payload, out int v)) return;
            lock (_l)
            {
                var st = Get(p);
                // pedimos mais do que a câmera aceitou: esse é o limite dela (faixa aprendida)
                if (p == ImageParam.Brilho && _lastSent[i] > v && _lastSent[i] >= st.Max) st.Max = Math.Max(st.Min + 1, v);
                st.Value = p == ImageParam.Exposicao ? Math.Clamp(v, 0, 14) : v;
                st.HasValue = true;
                if (p == ImageParam.Brilho && v > st.Max) st.Max = v;
            }
            Bump();
        }
        finally { lock (_l) _reading[i] = false; }
    }

    /// <summary>Lê os valores atuais (ao abrir a tela ou depois de reconectar).</summary>
    public void Refresh()
    {
        var now = DateTime.UtcNow;
        lock (_l)
        {
            foreach (var p in new[] { ImageParam.Brilho, ImageParam.Exposicao })
                if (Get(p).Support == ImageSupport.Suportado) _readDue[(int)p] = now;
        }
    }
}
