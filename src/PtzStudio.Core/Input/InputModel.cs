using PtzStudio.Core.Models;

namespace PtzStudio.Core.Input;

/// <summary>Leitura bruta de um joystick (DirectInput). Eixos normalizados 0..1; POV em centésimos de grau (-1 = centro).</summary>
public sealed class InputSnapshot
{
    public bool[] Buttons { get; set; } = Array.Empty<bool>();
    public double[] Axes { get; set; } = new double[8];
    public int[] Pov { get; set; } = { -1, -1, -1, -1 };
    public static readonly string[] AxisNames = { "X", "Y", "Z", "Rot X", "Rot Y", "Rot Z", "Slider 1", "Slider 2" };
}

/// <summary>
/// Identificadores de "botão" usados nos mapeamentos:
/// 0..127 = botões físicos; 1000..1003 = direcional (cima, direita, baixo, esquerda);
/// 2000+eixo = eixo empurrado para o lado positivo; 3000+eixo = lado negativo (gatilhos analógicos).
/// </summary>
public static class VirtualButtons
{
    public const int DpadUp = 1000, DpadRight = 1001, DpadDown = 1002, DpadLeft = 1003;
    public static int AxisPlus(int axis) => 2000 + axis;
    public static int AxisMinus(int axis) => 3000 + axis;

    public static string Describe(int id)
    {
        if (id < 0) return "não detectado";
        if (id < 128) return $"Botão {id}";
        return id switch
        {
            DpadUp => "Direcional ↑", DpadRight => "Direcional →", DpadDown => "Direcional ↓", DpadLeft => "Direcional ←",
            >= 2000 and < 3000 => $"Eixo {InputSnapshot.AxisNames[Math.Clamp(id - 2000, 0, 7)]} (+)",
            >= 3000 and < 4000 => $"Eixo {InputSnapshot.AxisNames[Math.Clamp(id - 3000, 0, 7)]} (−)",
            _ => $"Entrada {id}"
        };
    }
}

public sealed class InterpretedInput
{
    public double X { get; set; }   // direita +
    public double Y { get; set; }   // cima +
    public double Z { get; set; }   // aproximar +
    public HashSet<int> Pressed { get; } = new();
}

public static class InputInterpreter
{
    public const double AxisPressThreshold = 0.35;

    public static InterpretedInput Interpret(InputSnapshot s, AxisMap map, double[] rest, IEnumerable<int> watchedVirtual)
    {
        var r = new InterpretedInput();
        for (int i = 0; i < s.Buttons.Length; i++) if (s.Buttons[i]) r.Pressed.Add(i);

        if (s.Pov.Length > 0 && s.Pov[0] >= 0)
        {
            int a = s.Pov[0];
            if (a >= 31500 || a <= 4500) r.Pressed.Add(VirtualButtons.DpadUp);
            if (a >= 4500 && a <= 13500) r.Pressed.Add(VirtualButtons.DpadRight);
            if (a >= 13500 && a <= 22500) r.Pressed.Add(VirtualButtons.DpadDown);
            if (a >= 22500 && a <= 31500) r.Pressed.Add(VirtualButtons.DpadLeft);
        }

        foreach (int id in watchedVirtual)
        {
            if (id < 2000) continue;
            int axis = id % 1000;
            if (axis >= s.Axes.Length) continue;
            double rv = axis < rest.Length ? rest[axis] : 0;
            double delta = s.Axes[axis] - rv;
            if (id < 3000 ? delta > AxisPressThreshold : delta < -AxisPressThreshold) r.Pressed.Add(id);
        }

        double Norm(int i) => i < 0 || i >= s.Axes.Length ? 0 : (s.Axes[i] - 0.5) * 2.0;
        double Trig(int i)
        {
            if (i < 0 || i >= s.Axes.Length) return 0;
            double rv = i < rest.Length ? rest[i] : 0;
            return Math.Clamp(s.Axes[i] - rv, 0, 1);
        }

        r.X = Norm(map.PanAxis) * (map.PanAxisReversed ? -1 : 1);
        r.Y = -Norm(map.TiltAxis) * (map.TiltAxisReversed ? -1 : 1);                    // em DirectInput, empurrar para cima dá valor menor
        if (map.FullPanTilt && map.LimitDiagonal)
        {
            // um só analógico: o vetor (X, Y) nunca passa de raio 1, então a diagonal respeita os limites de velocidade
            double mag = Math.Sqrt(r.X * r.X + r.Y * r.Y);
            if (mag > 1) { r.X /= mag; r.Y /= mag; }
        }
        r.Z = -Norm(map.ZoomAxis) + Trig(map.ZoomTeleTriggerAxis) - Trig(map.ZoomWideTriggerAxis);
        r.Z = Math.Clamp(r.Z, -1, 1);
        return r;
    }
}

public sealed class ButtonRouter
{
    private HashSet<(int, int, ButtonAction)> _prevActive = new();
    public event Action<ButtonAction, int>? Triggered;
    public int HeldPan { get; private set; }
    public int HeldTilt { get; private set; }
    public int HeldZoom { get; private set; }
    public int HeldFocus { get; private set; }
    /// <summary>Brilho / exposição da imagem: -1, 0 ou +1 enquanto o botão estiver segurado.</summary>
    public int HeldBright { get; private set; }
    public int HeldExposure { get; private set; }

    public void Update(IReadOnlyCollection<int> pressed, Profile profile)
    {
        var now = new HashSet<int>(pressed);
        var active = new List<ButtonBinding>();
        foreach (var b in profile.Bindings)
        {
            if (b.Action == ButtonAction.Nenhuma) continue;
            if (!now.Contains(b.Button)) continue;
            if (b.Modifier >= 0 && !now.Contains(b.Modifier)) continue;
            // Uma combinação ativa tem prioridade sobre o botão isolado.
            if (b.Modifier < 0 && profile.Bindings.Any(o => o.Modifier >= 0 && o.Button == b.Button &&
                    o.Action != ButtonAction.Nenhuma && now.Contains(o.Modifier))) continue;
            active.Add(b);
        }

        int pan = 0, tilt = 0, zoom = 0, focus = 0, bright = 0, expo = 0;
        var keys = new HashSet<(int, int, ButtonAction)>();
        foreach (var b in active)
        {
            keys.Add((b.Button, b.Modifier, b.Action));
            switch (b.Action)
            {
                case ButtonAction.PanEsquerda: pan -= 1; break;
                case ButtonAction.PanDireita: pan += 1; break;
                case ButtonAction.TiltCima: tilt += 1; break;
                case ButtonAction.TiltBaixo: tilt -= 1; break;
                case ButtonAction.ZoomAproximar: zoom += 1; break;
                case ButtonAction.ZoomAfastar: zoom -= 1; break;
                case ButtonAction.FocoDistante: focus += 1; break;
                case ButtonAction.FocoProximo: focus -= 1; break;
                case ButtonAction.BrilhoMais: bright += 1; break;
                case ButtonAction.BrilhoMenos: bright -= 1; break;
                case ButtonAction.ExposicaoMais: expo += 1; break;
                case ButtonAction.ExposicaoMenos: expo -= 1; break;
                default:
                    if (!_prevActive.Contains((b.Button, b.Modifier, b.Action))) Triggered?.Invoke(b.Action, b.Parameter);
                    break;
            }
        }
        _prevActive = keys;
        HeldPan = Math.Clamp(pan, -1, 1); HeldTilt = Math.Clamp(tilt, -1, 1);
        HeldZoom = Math.Clamp(zoom, -1, 1); HeldFocus = Math.Clamp(focus, -1, 1);
        HeldBright = Math.Clamp(bright, -1, 1); HeldExposure = Math.Clamp(expo, -1, 1);
    }
}

public static class BindingValidator
{
    /// <summary>Retorna uma atribuição já existente para o mesmo botão (e mesmo modificador), se houver.</summary>
    public static ButtonBinding? FindConflict(Profile p, int button, int modifier) =>
        p.Bindings.FirstOrDefault(b => b.Button == button && b.Modifier == modifier && b.Action != ButtonAction.Nenhuma);
}
