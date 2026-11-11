using System.IO;
using System.Windows;
using System.Windows.Threading;
using PtzStudio.App.Input;
using PtzStudio.Core.Control;
using PtzStudio.Core.Input;
using PtzStudio.Core.Models;
using PtzStudio.Core.Storage;
using PtzStudio.Core.Visca;

namespace PtzStudio.App.Engine;

/// <summary>Liga entrada (controle/virtual), núcleo de controle, câmera e persistência. Não contém desenho de tela.</summary>
public sealed class AppEngine : IDisposable
{
    public SettingsStore Store { get; }
    public AppSettings Settings { get; set; }
    public ViscaClient Client { get; } = new();
    public PtzController Controller { get; }
    public ButtonRouter Router { get; } = new();
    /// <summary>Brilho e exposição da câmera (interface e botões do controle usam o mesmo objeto, então ficam sincronizados).</summary>
    public CameraImage Image { get; }
    /// <summary>Tempo entre abrir o programa e a primeira tela aparecer (ms). Só para o Diagnóstico.</summary>
    public double StartupMs { get; set; }
    public DirectInputPad Pad { get; } = new();

    public InputSnapshot? LastSnapshot { get; private set; }
    public InterpretedInput Last { get; private set; } = new();
    public double[] Rest { get; private set; } = new double[8];
    public double VirtualX, VirtualY, VirtualZ;

    public event Action? ProfileChanged;
    public event Action? SettingsReplaced;
    public event Action<string>? Notice;

    // Prioridade Normal: o padrão do DispatcherTimer (Background) perde para mouse e desenho da tela, o que atrasaria comandos e PARADAS.
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(16) };
    private DateTime _lastTick = DateTime.UtcNow;
    private bool _needRest = true;
    private Action<int>? _detectButton;
    private Action<int>? _detectAxis;
    private HashSet<int> _detectBaseline = new();
    private double[]? _detectAxisBase;
    private (int slot, DateTime when)? _pendingSave;
    private int _pendingSock = -1;
    private readonly object _issueLock = new();
    // Gravação com atraso: arrastar um slider gera dezenas de alterações por segundo; gravar uma por uma travaria a mesma thread do controle.
    private readonly DispatcherTimer _saveTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(600) };
    private bool _dirty;
    private bool _waitRelease;   // detecção acabou de capturar um botão: ignora funções até soltar tudo
    private readonly List<string> _issues = new();

    public Profile Profile => Settings.Profiles.FirstOrDefault(p => p.Name == Settings.ActiveProfile) ?? Settings.Profiles[0];
    public CameraConfig Camera => Settings.Cameras.FirstOrDefault(c => c.Id == Settings.ActiveCameraId) ?? Settings.Cameras[0];
    public bool Detecting => _detectButton != null || _detectAxis != null;

    /// <summary>Últimos problemas (erros de comunicação e falhas internas), só na memória: no máximo 30. Não há histórico de comandos.</summary>
    public IReadOnlyList<string> RecentIssues { get { lock (_issueLock) return _issues.ToArray(); } }

    public void AddIssue(string text)
    {
        lock (_issueLock)
        {
            _issues.Add($"{DateTime.Now:HH:mm:ss} {text}");
            if (_issues.Count > 30) _issues.RemoveAt(0);
        }
    }

    public AppEngine()
    {
        Store = new SettingsStore(SettingsStore.DefaultDirectory());
        // O backup automático (compacta um arquivo) não precisa atrasar a abertura da janela: roda em segundo plano.
        var store = Store;
        _ = Task.Run(() => { try { store.AutoBackup(); } catch { } });
        Settings = Store.Load();
        Controller = new PtzController(Client, () => Camera, () => Profile);
        Image = new CameraImage(Client, () => Camera);
        Image.Notice += m => Application.Current?.Dispatcher.BeginInvoke(() => Notice?.Invoke(m));
        Controller.ActiveSource = Settings.InputSource;
        Controller.Message += m => Notice?.Invoke(m);
        Router.Triggered += Execute;
        Client.ErrorReported += t => AddIssue("Câmera: " + t);
        Client.StateChanged += (st, msg) =>
        {
            if (st == ConnectionState.Desconectado) Controller.SafetyStop("comunicação com a câmera perdida");
            // Câmera (re)conectada: pergunta a ela quais ajustes de imagem suporta (em segundo plano; nada é presumido).
            if (st == ConnectionState.Conectado) _ = Image.DetectAsync();
            Application.Current?.Dispatcher.BeginInvoke(() => Notice?.Invoke("Câmera: " + msg));
        };
        Client.ResponseReceived += r =>
        {
            if (_pendingSave is not { } ps || (DateTime.UtcNow - ps.when).TotalSeconds >= 3) return;
            // Só confirma se a CONCLUSÃO vier do mesmo "socket" VISCA que o ACK do comando de gravar (evita confundir
            // com a conclusão de um zoom/foco qualquer).
            int sock = r.Payload.Length >= 2 ? r.Payload[1] & 0x0F : -1;
            if (r.Kind == ViscaResponseKind.Ack) { if (_pendingSock < 0) _pendingSock = sock; return; }
            if (r.Kind == ViscaResponseKind.Conclusao && _pendingSock >= 0 && sock == _pendingSock)
            {
                _pendingSave = null;
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    var e = Settings.Presets.FirstOrDefault(x => x.CameraId == Camera.Id && x.Slot == ps.slot);
                    if (e != null) { e.ConfirmedOnCamera = true; Save(); Notice?.Invoke($"Preset {ps.slot} confirmado pela câmera."); }
                });
            }
        };
        Pad.ConnectionChanged += c =>
        {
            _needRest = true;
            if (!c) Controller.SafetyStop("controle desconectado");
            Notice?.Invoke(c ? $"Controle conectado: {Pad.Name}" : "Controle desconectado");
        };
        _saveTimer.Tick += (_, _) => Flush();
        _timer.Tick += (_, _) =>
        {
            try { Tick(); }
            catch (Exception ex)
            {
                // Nunca deixar uma falha repetir 60x/s (caixas de erro em loop) com a câmera em movimento.
                AddIssue("Falha no ciclo de controle: " + ex.Message);
                try { Controller.SafetyStop("erro interno no ciclo de controle"); } catch { }
            }
        };
    }

    /// <summary>Chamado depois que a janela já apareceu. O controle é procurado em segundo plano; o ciclo de comandos só começa com tudo pronto.</summary>
    public void Start(IntPtr hwnd)
    {
        Pad.Initialize(hwnd);
        ConnectCamera();
        _timer.Start();
    }

    public void ConnectCamera()
    {
        Image.Reset();
        Client.Connect(Camera);
    }

    private void Tick()
    {
        var now = DateTime.UtcNow;
        double dt = Math.Clamp((now - _lastTick).TotalSeconds, 0.001, 0.1);
        _lastTick = now;

        var snap = Pad.Poll();
        if (snap != null)
        {
            if (_needRest) { Rest = (double[])snap.Axes.Clone(); _needRest = false; }
            LastSnapshot = snap;
            var watched = Settings.Layout.Values.Where(v => v >= 2000).ToList();
            if (_detectButton != null)
                for (int a = 0; a < 8; a++) { watched.Add(VirtualButtons.AxisPlus(a)); watched.Add(VirtualButtons.AxisMinus(a)); }
            Last = InputInterpreter.Interpret(snap, Profile.AxisMap, Rest, watched);
            ProcessDetection(snap);
        }
        else
        {
            LastSnapshot = null;
            Last = new InterpretedInput();
        }

        if (_waitRelease && Last.Pressed.Count == 0) _waitRelease = false;
        if (Detecting || _waitRelease)
        {
            Router.Update(Array.Empty<int>(), Profile);   // não executa funções enquanto detecta (nem com o botão detectado ainda apertado)
            Image.SetHold(ImageParam.Brilho, 0, now); Image.SetHold(ImageParam.Exposicao, 0, now);
            Controller.SetDigital(0, 0, 0, 0);
            Controller.SetAnalog(InputSource.Fisico, 0, 0, 0, now);
        }
        else
        {
            Router.Update(Last.Pressed, Profile);
            Image.SetHold(ImageParam.Brilho, Router.HeldBright, now);
            Image.SetHold(ImageParam.Exposicao, Router.HeldExposure, now);
            Controller.SetDigital(Router.HeldPan, Router.HeldTilt, Router.HeldZoom, Router.HeldFocus);
            Controller.SetAnalog(InputSource.Fisico, Last.X, Last.Y, Last.Z, now);
        }
        Image.Tick(now);
        Controller.SetAnalog(InputSource.Virtual, VirtualX, VirtualY, VirtualZ, now);
        Controller.Tick(dt, now);
    }

    // ---------- detecção automática ----------
    public void BeginDetectButton(Action<int> done)
    {
        _detectBaseline = new HashSet<int>(Last.Pressed);
        _detectButton = done;
        _detectAxis = null;
    }

    public void BeginDetectAxis(Action<int> done)
    {
        _detectAxisBase = LastSnapshot != null ? (double[])LastSnapshot.Axes.Clone() : null;
        _detectAxis = done;
        _detectButton = null;
    }

    public void RecalibrateRest() { _needRest = true; }

    public void CancelDetect() { _detectButton = null; _detectAxis = null; }

    private void ProcessDetection(InputSnapshot snap)
    {
        if (_detectButton != null)
        {
            // sticks atribuídos não contam como "botão"
            var map = Profile.AxisMap;
            var ignore = new HashSet<int> { map.PanAxis, map.TiltAxis, map.ZoomAxis };
            foreach (int id in Last.Pressed)
            {
                if (_detectBaseline.Contains(id)) continue;
                if (id >= 2000 && ignore.Contains(id % 1000)) continue;
                var cb = _detectButton; _detectButton = null;
                _waitRelease = true;
                cb?.Invoke(id);
                return;
            }
            _detectBaseline.IntersectWith(Last.Pressed);
        }
        else if (_detectAxis != null && _detectAxisBase != null)
        {
            for (int a = 0; a < snap.Axes.Length; a++)
            {
                if (Math.Abs(snap.Axes[a] - _detectAxisBase[a]) > 0.4)
                {
                    var cb = _detectAxis; _detectAxis = null;
                    cb?.Invoke(a);
                    return;
                }
            }
        }
        else if (_detectAxis != null) _detectAxisBase = (double[])snap.Axes.Clone();
    }

    // ---------- ações de botões ----------
    public void Execute(ButtonAction a, int p)
    {
        if (a == ButtonAction.Nenhuma) return;
        switch (a)
        {
            case ButtonAction.Parar: Controller.StopAll("botão Parar"); break;
            case ButtonAction.SalvarPreset: SavePresetSlot(p, null); break;
            case ButtonAction.AcionarPreset: Controller.RecallPreset(p); Notice?.Invoke($"Preset {p} acionado"); break;
            case ButtonAction.AlternarPerfil: NextProfile(); break;
            case ButtonAction.Home: Controller.Home(); break;
            case ButtonAction.AutoFoco: Controller.ToggleAutoFocus(); Notice?.Invoke("Foco automático: " + (Controller.AutoFocusOn ? "ligado" : "desligado")); break;
            case ButtonAction.AlternarEntrada: ToggleSource(); break;
        }
    }

    public void ToggleSource() => SetSource(Controller.ActiveSource == InputSource.Fisico ? InputSource.Virtual : InputSource.Fisico);

    public void SetSource(InputSource s)
    {
        Controller.ActiveSource = s;
        Settings.InputSource = s;
        VirtualX = VirtualY = VirtualZ = 0;
        Save();
        Notice?.Invoke("Entrada ativa: " + (s == InputSource.Fisico ? "joystick físico" : "joystick virtual"));
    }

    public void SavePresetSlot(int slot, string? name)
    {
        Controller.SavePreset(slot);
        var e = Settings.Presets.FirstOrDefault(x => x.CameraId == Camera.Id && x.Slot == slot);
        if (e == null) { e = new PresetEntry { CameraId = Camera.Id, Slot = slot, Name = name ?? $"Preset {slot}" }; Settings.Presets.Add(e); }
        else if (name != null) e.Name = name;
        e.ConfirmedOnCamera = false;
        e.SentAt = DateTime.Now;
        _pendingSock = -1;
        _pendingSave = (slot, DateTime.UtcNow);
        Save();
        Notice?.Invoke($"Comando de gravar preset {slot} enviado; aguardando confirmação da câmera.");
    }

    public void NextProfile()
    {
        int i = Settings.Profiles.FindIndex(p => p.Name == Settings.ActiveProfile);
        SetProfile(Settings.Profiles[(i + 1) % Settings.Profiles.Count].Name);
    }

    public void SetProfile(string name)
    {
        Controller.StopAll("troca de perfil");
        Settings.ActiveProfile = name;
        Save();
        ProfileChanged?.Invoke();
        Notice?.Invoke("Perfil ativo: " + name);
    }

    public void ReplaceSettings(AppSettings s)
    {
        Controller.StopAll("restauração de configurações");
        _saveTimer.Stop(); _dirty = false;   // gravação pendente era das configurações antigas
        Settings = s;
        Controller.ActiveSource = s.InputSource;
        ConnectCamera();
        SettingsReplaced?.Invoke();
        ProfileChanged?.Invoke();
    }

    /// <summary>Pede para gravar as configurações. A gravação acontece logo depois (agrupa várias alterações seguidas).</summary>
    public void Save()
    {
        if (!_saveTimer.Dispatcher.CheckAccess()) { _saveTimer.Dispatcher.BeginInvoke(new Action(Save)); return; }
        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Grava agora o que estiver pendente (antes de backup, exportação ou ao sair).</summary>
    public void Flush()
    {
        _saveTimer.Stop();
        if (!_dirty) return;
        _dirty = false;
        try { Store.Save(Settings); }
        catch (Exception e) { Notice?.Invoke("Não foi possível salvar: " + e.Message); }
    }

    /// <summary>Aplica um novo mapeamento (peça do desenho → número do botão) levando junto as funções já atribuídas.</summary>
    public void ApplyLayout(IReadOnlyDictionary<string, int> newLayout)
    {
        LayoutMigration.Apply(Settings, newLayout);
        Controller.StopAll("novo mapeamento de botões");
        Save();
    }

    /// <summary>Volta os ajustes de movimento do perfil ativo aos valores de fábrica daquele perfil. Botões e eixos não mudam.</summary>
    public void RestoreMotionDefaults()
    {
        Profile.Motion = AppSettings.DefaultMotionFor(Profile.Name);
        Controller.StopAll("padrões de movimento restaurados");
        Save();
    }

    public void Dispose()
    {
        _timer.Stop();
        try { Controller.StopAll("encerrando o programa"); } catch { }
        _dirty = true;   // sempre grava ao sair
        Flush();
        Client.Dispose();
        Pad.Dispose();
    }
}
