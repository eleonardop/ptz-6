using PtzStudio.Core.Models;
using PtzStudio.Core.Motion;
using PtzStudio.Core.Visca;

namespace PtzStudio.Core.Control;

/// <summary>
/// Converte entradas (analógico físico, joystick virtual, botões) em comandos VISCA, com as proteções:
/// parada ao neutro, reforço da parada, watchdog de entrada, anti-repetição e troca segura de fonte.
/// </summary>
public sealed class PtzController
{
    public const int WatchdogMs = 600;
    public const int KeepAliveMs = 400;

    private readonly IViscaSender _tx;
    private readonly Func<CameraConfig> _cam;
    private readonly Func<Profile> _profile;
    private readonly MotionProcessor _motion = new();
    private readonly object _lock = new();

    private InputSource _source = InputSource.Fisico;
    private double _ax, _ay, _az;
    private int _digPan, _digTilt, _digZoom, _digFocus;
    private DateTime _lastInput = DateTime.MinValue;
    private int _sentPan, _sentTilt, _sentZoom, _sentFocus;
    private readonly StopRepeat _ptRep = new(), _zoomRep = new(), _focusRep = new();
    private DateTime _lastPt = DateTime.MinValue, _lastZoom = DateTime.MinValue;
    private bool _requireNeutral;
    private bool _autoFocus;
    private (double x, double y, double z, DateTime until)? _pulse;
    private (int dir, DateTime until)? _focusPulse;

    /// <summary>
    /// Reforço da PARADA. UDP não garante entrega: se os poucos pacotes de parada se perderem, a câmera seguiria andando.
    /// Depois de parar um eixo, a parada é repetida em intervalos crescentes (cerca de 2 s no total) — só enquanto
    /// aquele eixo estiver parado e nenhum comando novo tiver saído. Acaba sozinho: não "briga" com o controle remoto da câmera.
    /// </summary>
    private sealed class StopRepeat
    {
        private static readonly int[] Schedule = { 30, 90, 250, 600, 1200, 2000 };   // ms depois da primeira parada
        private DateTime _at;
        private bool _anchored;
        private int _step = Schedule.Length;                                           // nada pendente

        /// <summary>Começa a contar a partir do próximo ciclo (usa o relógio do Tick, não o do sistema).</summary>
        public void Arm() { _step = 0; _anchored = false; }
        public void Cancel() => _step = Schedule.Length;

        public bool Due(DateTime now)
        {
            if (_step >= Schedule.Length) return false;
            if (!_anchored) { _at = now; _anchored = true; return false; }
            if ((now - _at).TotalMilliseconds < Schedule[_step]) return false;
            _step++;
            return true;
        }
    }

    /// <summary>Trava leve (aguarda o neutro): usada em paradas de segurança automáticas (watchdog, controle desconectado...).</summary>
    public bool SafetyLatched => _requireNeutral;
    public int SentPan => _sentPan;
    public int SentTilt => _sentTilt;
    public int SentZoom => _sentZoom;
    public event Action<string>? Message;

    public PtzController(IViscaSender tx, Func<CameraConfig> cam, Func<Profile> profile)
    {
        _tx = tx; _cam = cam; _profile = profile;
    }

    public InputSource ActiveSource
    {
        get => _source;
        set
        {
            lock (_lock)
            {
                if (_source == value) return;
                _source = value;
                StopAllLocked("troca da fonte de entrada");
            }
        }
    }

    /// <summary>Recebe a posição do analógico (físico ou virtual). Entradas da fonte inativa são ignoradas.</summary>
    public void SetAnalog(InputSource src, double x, double y, double z, DateTime now)
    {
        lock (_lock)
        {
            if (src != _source) return;
            _ax = x; _ay = y; _az = z; _lastInput = now;
        }
    }

    public void SetDigital(int pan, int tilt, int zoom, int focus)
    {
        lock (_lock) { _digPan = pan; _digTilt = tilt; _digZoom = zoom; _digFocus = focus; }
    }

    /// <summary>Teste de direção: injeta uma entrada por um curto tempo (passa pelas inversões do perfil).</summary>
    public void Pulse(double x, double y, double z, int ms, DateTime now)
    {
        lock (_lock) { _pulse = (x, y, z, now.AddMilliseconds(ms)); _lastInput = now; }
    }

    /// <summary>Teste de foco: aciona o foco por um curto tempo e para (a parada é repetida por segurança).</summary>
    public void PulseFocus(int dir, int ms, DateTime now)
    {
        lock (_lock) { _focusPulse = (Math.Sign(dir), now.AddMilliseconds(ms)); _lastInput = now; }
    }

    public void Tick(double dt, DateTime now)
    {
        lock (_lock)
        {
            var cam = _cam();
            var prof = _profile();
            double ax = _ax, ay = _ay, az = _az;

            if (_pulse is { } pl)
            {
                if (now < pl.until) { ax = pl.x; ay = pl.y; az = pl.z; _lastInput = now; }
                else { _pulse = null; ax = ay = az = 0; }
            }
            else
            {
                ax = Math.Clamp(ax + _digPan, -1, 1);
                ay = Math.Clamp(ay + _digTilt, -1, 1);
                az = Math.Clamp(az + _digZoom, -1, 1);
            }

            int focus = _digFocus;
            if (_focusPulse is { } fp)
            {
                if (now < fp.until) { focus = fp.dir; _lastInput = now; }
                else _focusPulse = null;
            }

            bool neutral = Math.Abs(ax) < 0.001 && Math.Abs(ay) < 0.001 && Math.Abs(az) < 0.001 && focus == 0;
            if (_requireNeutral)
            {
                if (neutral) _requireNeutral = false;
                else { Settle(cam, now); return; }
            }

            bool moving = _sentPan != 0 || _sentTilt != 0 || _sentZoom != 0;
            if (moving && _pulse == null && (now - _lastInput).TotalMilliseconds > WatchdogMs)
            {
                SafetyStopLocked("sem atualização de entrada (watchdog)");
                return;
            }

            var o = _motion.Process(ax, ay, az, dt, prof.Motion);
            // Pan/Tilt: porcentagem do perfil (1% a 100%). Zoom: independente — só o joystick virtual tem a sua intensidade própria.
            double ptScale = Math.Clamp(prof.Motion.PanTiltSpeedPercent, 1, 100) / 100.0;
            double zoomScale = _source == InputSource.Virtual ? Math.Clamp(prof.Motion.VirtualZoomPercent, 1, 100) / 100.0 : 1.0;
            int pan = ToInt(o.Pan, ptScale, cam.MaxPanSpeed);
            int tilt = ToInt(o.Tilt, ptScale, cam.MaxTiltSpeed);
            int zoom = ToInt(o.Zoom, zoomScale, cam.MaxZoomSpeed);

            ApplyPanTilt(cam, pan, tilt, now);
            ApplyZoom(cam, zoom, now);
            ApplyFocus(cam, focus, now);
        }
    }

    /// <summary>Único ponto de saída de comandos do controlador.</summary>
    private void Tx(byte[] packet) => _tx.Send(packet);

    private static int ToInt(double speed, double level, int cameraMax)
    {
        if (Math.Abs(speed) < 0.5) return 0;
        int i = (int)Math.Max(1, Math.Round(Math.Abs(speed) * level));
        return Math.Sign(speed) * Math.Min(i, Math.Max(1, cameraMax));
    }

    private void ApplyPanTilt(CameraConfig cam, int pan, int tilt, DateTime now)
    {
        if (pan == 0 && tilt == 0)
        {
            if (_sentPan != 0 || _sentTilt != 0)
            {
                Tx(ViscaCommands.PanTiltStop(cam.ViscaAddress));
                _sentPan = _sentTilt = 0; _lastPt = now; _ptRep.Arm();
            }
            else if (_ptRep.Due(now)) Tx(ViscaCommands.PanTiltStop(cam.ViscaAddress));
            return;
        }
        bool changed = pan != _sentPan || tilt != _sentTilt;
        double since = (now - _lastPt).TotalMilliseconds;
        if ((changed && since >= cam.CommandIntervalMs) || (!changed && since >= KeepAliveMs))
        {
            Tx(ViscaCommands.PanTiltDrive(cam.ViscaAddress, pan, tilt, cam.MaxPanSpeed, cam.MaxTiltSpeed));
            _sentPan = pan; _sentTilt = tilt; _lastPt = now; _ptRep.Cancel();
        }
    }

    private void ApplyZoom(CameraConfig cam, int zoom, DateTime now)
    {
        if (zoom == 0)
        {
            if (_sentZoom != 0)
            {
                Tx(ViscaCommands.Zoom(cam.ViscaAddress, 0));
                _sentZoom = 0; _lastZoom = now; _zoomRep.Arm();
            }
            else if (_zoomRep.Due(now)) Tx(ViscaCommands.Zoom(cam.ViscaAddress, 0));
            return;
        }
        bool changed = zoom != _sentZoom;
        double since = (now - _lastZoom).TotalMilliseconds;
        if ((changed && since >= cam.CommandIntervalMs) || (!changed && since >= KeepAliveMs))
        {
            Tx(ViscaCommands.Zoom(cam.ViscaAddress, zoom, cam.MaxZoomSpeed));
            _sentZoom = zoom; _lastZoom = now; _zoomRep.Cancel();
        }
    }

    private void ApplyFocus(CameraConfig cam, int dir, DateTime now)
    {
        if (dir != _sentFocus)
        {
            Tx(ViscaCommands.Focus(cam.ViscaAddress, dir));
            _sentFocus = dir;
            if (dir == 0) _focusRep.Arm(); else _focusRep.Cancel();
        }
        else if (dir == 0 && _focusRep.Due(now))
            Tx(ViscaCommands.Focus(cam.ViscaAddress, 0)); // UDP não garante entrega: um foco "preso" é pior que um pacote a mais
    }

    // Repete as paradas pendentes enquanto a parada de segurança aguarda o neutro.
    private void Settle(CameraConfig cam, DateTime now)
    {
        if (_ptRep.Due(now)) Tx(ViscaCommands.PanTiltStop(cam.ViscaAddress));
        if (_zoomRep.Due(now)) Tx(ViscaCommands.Zoom(cam.ViscaAddress, 0));
        if (_focusRep.Due(now)) Tx(ViscaCommands.Focus(cam.ViscaAddress, 0));
    }

    /// <summary>Para tudo imediatamente, sem trava.</summary>
    public void StopAll(string reason)
    {
        lock (_lock) StopAllLocked(reason);
    }

    /// <summary>Parada de segurança automática (watchdog, controle desconectado, câmera sem resposta): só volta a mover depois que todas as entradas voltarem ao neutro.</summary>
    public void SafetyStop(string reason)
    {
        lock (_lock) SafetyStopLocked(reason);
    }

    private void CancelStopRepeats() { _ptRep.Cancel(); _zoomRep.Cancel(); _focusRep.Cancel(); }

    private void StopAllLocked(string reason)
    {
        var cam = _cam();
        _ax = _ay = _az = 0;
        _pulse = null;
        _focusPulse = null;
        _motion.Reset();
        Tx(ViscaCommands.PanTiltStop(cam.ViscaAddress));
        Tx(ViscaCommands.Zoom(cam.ViscaAddress, 0));
        Tx(ViscaCommands.Focus(cam.ViscaAddress, 0));
        _sentPan = _sentTilt = _sentZoom = _sentFocus = 0;
        _ptRep.Arm(); _zoomRep.Arm(); _focusRep.Arm();
        Message?.Invoke("Parado: " + reason);
    }

    private void SafetyStopLocked(string reason)
    {
        StopAllLocked(reason);
        var cam = _cam();
        Tx(ViscaCommands.PanTiltStop(cam.ViscaAddress)); // repetição deliberada: UDP não garante entrega
        _requireNeutral = true;
        Message?.Invoke("Parada de segurança: " + reason);
    }

    public void SavePreset(int slot) { lock (_lock) { Tx(ViscaCommands.PresetSet(_cam().ViscaAddress, slot)); } }

    public void RecallPreset(int slot)
    {
        lock (_lock)
        {
            StopAllLocked("antes de acionar preset");
            Tx(ViscaCommands.PresetRecall(_cam().ViscaAddress, slot));
            CancelStopRepeats();   // uma parada repetida agora interromperia o deslocamento até o preset
        }
    }

    public void Home()
    {
        lock (_lock) { StopAllLocked("antes de ir ao Home"); Tx(ViscaCommands.Home(_cam().ViscaAddress)); CancelStopRepeats(); }
    }

    public void ToggleAutoFocus()
    {
        lock (_lock) { _autoFocus = !_autoFocus; Tx(ViscaCommands.AutoFocus(_cam().ViscaAddress, _autoFocus)); }
    }

    public bool AutoFocusOn => _autoFocus;
}
