using System.Text.Json;
using System.Text.Json.Serialization;
using PtzStudio.Core.Input; // VirtualButtons (ids do D-pad/eixos usados em DefaultLayout)

namespace PtzStudio.Core.Models;

public enum ViscaTransportMode { UdpPuro = 0, ViscaOverIp = 1 }
public enum ResponseCurve { Linear, Progressiva, Suave, Agressiva }
public enum AxisPriority { Nenhuma, Horizontal, Vertical }
public enum InputSource { Fisico, Virtual }

public enum ButtonAction
{
    Nenhuma, PanEsquerda, PanDireita, TiltCima, TiltBaixo, ZoomAproximar, ZoomAfastar,
    FocoProximo, FocoDistante, Parar,
    /// <summary>REMOVIDA. Só existe para conseguir LER arquivos antigos; a normalização apaga as atribuições com este valor.</summary>
    ParadaEmergencia,
    SalvarPreset, AcionarPreset,
    /// <summary>REMOVIDA (os 3 níveis de velocidade deram lugar ao controle de 1% a 100%). Só leitura de arquivos antigos.</summary>
    AlternarVelocidade,
    AlternarPerfil, Home, AutoFoco, AlternarEntrada,
    // Imagem da câmera (segurar o botão repete o ajuste; soltar para)
    BrilhoMais, BrilhoMenos, ExposicaoMais, ExposicaoMenos
}

public static class ActionCatalog
{
    public static readonly (ButtonAction Action, string Nome, bool Segurar, bool UsaParametro)[] Todas =
    {
        (ButtonAction.Nenhuma, "(sem função)", false, false),
        (ButtonAction.PanEsquerda, "Pan esquerda", true, false),
        (ButtonAction.PanDireita, "Pan direita", true, false),
        (ButtonAction.TiltCima, "Tilt para cima", true, false),
        (ButtonAction.TiltBaixo, "Tilt para baixo", true, false),
        (ButtonAction.ZoomAproximar, "Zoom aproximar", true, false),
        (ButtonAction.ZoomAfastar, "Zoom afastar", true, false),
        (ButtonAction.FocoProximo, "Foco próximo", true, false),
        (ButtonAction.FocoDistante, "Foco distante", true, false),
        (ButtonAction.Parar, "Parar movimentos", false, false),
        (ButtonAction.SalvarPreset, "Salvar preset (nº no parâmetro)", false, true),
        (ButtonAction.AcionarPreset, "Acionar preset (nº no parâmetro)", false, true),
        (ButtonAction.AlternarPerfil, "Alternar perfil", false, false),
        (ButtonAction.Home, "Posição inicial (Home)", false, false),
        (ButtonAction.AutoFoco, "Alternar foco automático", false, false),
        (ButtonAction.AlternarEntrada, "Alternar joystick físico/virtual", false, false),
        (ButtonAction.BrilhoMais, "Brilho da imagem +", true, false),
        (ButtonAction.BrilhoMenos, "Brilho da imagem −", true, false),
        (ButtonAction.ExposicaoMais, "Exposição +", true, false),
        (ButtonAction.ExposicaoMenos, "Exposição −", true, false),
    };

    public static string Nome(ButtonAction a) => Todas.FirstOrDefault(t => t.Action == a).Nome ?? a.ToString();
    public static bool UsesParameter(ButtonAction a) => Todas.FirstOrDefault(t => t.Action == a).UsaParametro;
}

public sealed class CameraConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Câmera 1";
    public string IpAddress { get; set; } = "192.168.1.100";
    /// <summary>Porta UDP. 1259 é comum em câmeras PTZ genéricas, mas DEVE ser confirmada no manual da câmera.</summary>
    public int Port { get; set; } = 1259;
    public ViscaTransportMode Transport { get; set; } = ViscaTransportMode.UdpPuro;
    public int ViscaAddress { get; set; } = 1;
    public int MaxPanSpeed { get; set; } = 24;   // limite VISCA: 0x18
    public int MaxTiltSpeed { get; set; } = 20;  // limite VISCA: 0x14
    public int MaxZoomSpeed { get; set; } = 8;   // 1..8 (códigos VISCA 0..7)
    public int TimeoutMs { get; set; } = 1000;
    public int CommandIntervalMs { get; set; } = 40;
    public int ReconnectAttempts { get; set; } = 5;
    /// <summary>Envia consulta periódica para saber se a câmera responde.</summary>
    public bool HealthCheck { get; set; } = true;

    // ---- Vídeo ao vivo (RTSP), mostrado na tela Controle PTZ. Desligado por padrão; arquivos antigos carregam sem mudança. ----
    /// <summary>Mostra o vídeo da câmera na tela Controle PTZ.</summary>
    public bool VideoEnabled { get; set; } = false;
    /// <summary>Endereço RTSP do fluxo principal (maior resolução). Ex.: rtsp://192.168.1.100/1</summary>
    public string RtspUrl { get; set; } = "";
    /// <summary>Endereço RTSP do fluxo secundário (menor resolução), usado na prévia quando existir.</summary>
    public string RtspSubUrl { get; set; } = "";
    public string RtspUser { get; set; } = "";
    /// <summary>Guardada em texto simples no arquivo de configurações (como o resto), inclusive em backups e exportações.</summary>
    public string RtspPassword { get; set; } = "";
    /// <summary>Verdadeiro: a prévia usa o fluxo secundário (mais leve). Falso: usa o principal.</summary>
    public bool VideoUsePreviewStream { get; set; } = true;

    public CameraConfig Clone() => JsonClone.Clone(this);
}

public sealed class AxisSettings
{
    public bool Invert { get; set; }
    public double Sensitivity { get; set; } = 1.0;
    public double Deadzone { get; set; } = 0.12;
    public int MinSpeed { get; set; } = 1;
    public int MaxSpeed { get; set; } = 12;
    public ResponseCurve Curve { get; set; } = ResponseCurve.Progressiva;
    public double CurveExponent { get; set; } = 2.0;
    /// <summary>Unidades de velocidade por segundo. 0 = instantâneo.</summary>
    public double AccelPerSecond { get; set; } = 40;
    public double DecelPerSecond { get; set; } = 120;
    public double Smoothing { get; set; } = 0.0;
    public double NoiseThreshold { get; set; } = 0.01;
    /// <summary>Se falso (padrão), soltar o analógico para a câmera imediatamente.</summary>
    public bool SoftStopOnRelease { get; set; } = false;
}

public sealed class MotionSettings
{
    public AxisSettings Pan { get; set; } = new() { MaxSpeed = 12 };
    public AxisSettings Tilt { get; set; } = new() { MaxSpeed = 10 };
    public AxisSettings Zoom { get; set; } = new() { MaxSpeed = 5, Deadzone = 0.10 };
    public double AngularToleranceDeg { get; set; } = 18;
    public AxisPriority Priority { get; set; } = AxisPriority.Nenhuma;
    public bool LockSingleAxis { get; set; } = false;
    /// <summary>Velocidade de Pan/Tilt do perfil, de 1% a 100% (multiplica a velocidade calculada pelo analógico). O Zoom não é afetado.</summary>
    public int PanTiltSpeedPercent { get; set; } = 65;
    /// <summary>Intensidade do zoom do joystick virtual (botões Aproximar/Afastar da tela), de 1% a 100% da velocidade máxima de zoom do perfil.</summary>
    public int VirtualZoomPercent { get; set; } = 50;

    // Só para LER arquivos antigos (3 níveis de velocidade). A normalização converte o nível padrão em porcentagem e zera estes campos,
    // que então deixam de ser gravados.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double[]? SpeedLevels { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? DefaultSpeedLevel { get; set; }
}

/// <summary>Quais eixos do DirectInput controlam o quê. Índices: 0=X 1=Y 2=Z 3=RotX 4=RotY 5=RotZ 6=Slider1 7=Slider2. -1 = nenhum.</summary>
public sealed class AxisMap
{
    public int PanAxis { get; set; } = 0;
    public int TiltAxis { get; set; } = 1;
    public int ZoomAxis { get; set; } = 4;
    public int ZoomTeleTriggerAxis { get; set; } = -1;
    public int ZoomWideTriggerAxis { get; set; } = -1;

    /// <summary>
    /// "Pan/Tilt completo (Analógico)": um único analógico (eixos PanAxis = X e TiltAxis = Y) controla Pan e Tilt ao mesmo tempo,
    /// diagonais incluídas. Falso = comportamento anterior (eixos independentes, sem limitar o vetor).
    /// </summary>
    public bool FullPanTilt { get; set; }
    /// <summary>Polaridade física do eixo horizontal (definida pela calibração): verdadeiro se "empurrar para a direita" dá valor MENOR.</summary>
    public bool PanAxisReversed { get; set; }
    /// <summary>Polaridade física do eixo vertical (definida pela calibração): verdadeiro se "empurrar para cima" dá valor MAIOR.</summary>
    public bool TiltAxisReversed { get; set; }
    /// <summary>Com FullPanTilt: limita o vetor (X, Y) ao raio 1, para que uma diagonal nunca ultrapasse os limites configurados.</summary>
    public bool LimitDiagonal { get; set; } = true;
}

public sealed class ButtonBinding
{
    /// <summary>Identificador do botão (ver VirtualButtons).</summary>
    public int Button { get; set; }
    /// <summary>Botão modificador para combinações; -1 = sem combinação.</summary>
    public int Modifier { get; set; } = -1;
    public ButtonAction Action { get; set; }
    public int Parameter { get; set; }
}

public sealed class Profile
{
    public string Name { get; set; } = "Perfil";
    public List<ButtonBinding> Bindings { get; set; } = new();
    public MotionSettings Motion { get; set; } = new();
    public AxisMap AxisMap { get; set; } = new();
    public Profile Clone() => JsonClone.Clone(this);
}

public sealed class PresetEntry
{
    public Guid CameraId { get; set; }
    public int Slot { get; set; }
    public string Name { get; set; } = "";
    /// <summary>True somente se a câmera respondeu "concluído" ao comando de gravar.</summary>
    public bool ConfirmedOnCamera { get; set; }
    public DateTime? SentAt { get; set; }
}

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public List<CameraConfig> Cameras { get; set; } = new();
    public Guid ActiveCameraId { get; set; }
    public List<Profile> Profiles { get; set; } = new();
    public string ActiveProfile { get; set; } = "Igreja";
    public List<PresetEntry> Presets { get; set; } = new();
    /// <summary>Peça do desenho do controle -> identificador do botão detectado (ver VirtualButtons).</summary>
    public Dictionary<string, int> Layout { get; set; } = new();
    public InputSource InputSource { get; set; } = InputSource.Fisico;
    /// <summary>Ao minimizar, esconde a janela e deixa o ícone ao lado do relógio do Windows (o programa continua funcionando).</summary>
    public bool MinimizeToTray { get; set; } = true;
    /// <summary>O botão fechar (X) esconde a janela na bandeja em vez de encerrar, para não fechar sem querer durante a transmissão.</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>Ajustes de movimento de fábrica de um perfil (pelo nome; se não existir, os do perfil "Igreja"). Usado por "Restaurar padrões".</summary>
    public static MotionSettings DefaultMotionFor(string profileName)
    {
        var all = CreateDefault().Profiles;
        var p = all.FirstOrDefault(x => x.Name == profileName) ?? all[0];
        return p.Motion;
    }

    public static AppSettings CreateDefault()
    {
        var cam = new CameraConfig();
        var s = new AppSettings { ActiveCameraId = cam.Id, Layout = DefaultLayout() };
        s.Cameras.Add(cam);
        s.Profiles.Add(MakeProfile("Igreja", 1.0));
        s.Profiles.Add(MakeProfile("Louvor", 1.4));
        s.Profiles.Add(MakeProfile("Pregação", 0.6));
        s.Profiles.Add(MakeProfile("Operação", 1.0));
        return s;
    }

    public static Dictionary<string, int> DefaultLayout() => new()
    {
        ["A"] = 0, ["B"] = 1, ["X"] = 2, ["Y"] = 3, ["LB"] = 4, ["RB"] = 5,
        ["VIEW"] = 6, ["MENU"] = 7, ["L3"] = 8, ["R3"] = 9,
        ["DPAD_UP"] = VirtualButtons.DpadUp, ["DPAD_RIGHT"] = VirtualButtons.DpadRight,
        ["DPAD_DOWN"] = VirtualButtons.DpadDown, ["DPAD_LEFT"] = VirtualButtons.DpadLeft,
        ["LT"] = -1, ["RT"] = -1,
        // botões sem número fixo no Windows: ficam livres até serem ligados com DETECTAR BOTÃO
        ["HOME"] = -1, ["SCREENSHOT"] = -1, ["M"] = -1, ["L4"] = -1, ["R4"] = -1,
    };

    private static Profile MakeProfile(string name, double speedFactor)
    {
        var p = new Profile { Name = name };
        p.AxisMap.FullPanTilt = true;   // novas instalações: Pan/Tilt completo no analógico esquerdo
        p.Motion.Pan.MaxSpeed = Math.Clamp((int)Math.Round(12 * speedFactor), 1, 24);
        p.Motion.Tilt.MaxSpeed = Math.Clamp((int)Math.Round(10 * speedFactor), 1, 20);
        p.Motion.Zoom.MaxSpeed = Math.Clamp((int)Math.Round(5 * speedFactor), 1, 8);
        if (name == "Pregação") { p.Motion.Pan.Smoothing = 0.3; p.Motion.Tilt.Smoothing = 0.3; }
        p.Motion.PanTiltSpeedPercent = name switch { "Louvor" => 75, "Pregação" => 40, _ => 65 };   // 65% = o antigo "nível 2" 
        var d = DefaultLayout();
        void B(string part, ButtonAction a, int prm = 0) =>
            p.Bindings.Add(new ButtonBinding { Button = d[part], Action = a, Parameter = prm });
        // Sugestões iniciais. Confirme na tela "Mapeamento do controle".
        B("X", ButtonAction.Parar);
        B("Y", ButtonAction.Home);
        B("LB", ButtonAction.FocoProximo);
        B("RB", ButtonAction.FocoDistante);
        B("DPAD_UP", ButtonAction.AcionarPreset, 1);
        B("DPAD_RIGHT", ButtonAction.AcionarPreset, 2);
        B("DPAD_DOWN", ButtonAction.AcionarPreset, 3);
        B("DPAD_LEFT", ButtonAction.AcionarPreset, 4);
        B("MENU", ButtonAction.AlternarPerfil);
        B("VIEW", ButtonAction.AlternarEntrada);
        return p;
    }
}

public static class JsonClone
{
    private static readonly JsonSerializerOptions Opt = new();
    public static T Clone<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Opt), Opt)!;
}
