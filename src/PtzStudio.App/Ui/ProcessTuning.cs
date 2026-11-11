using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PtzStudio.App.Ui;

/// <summary>
/// Ajustes do processo para o controle continuar fluido com a janela escondida na bandeja e com o OBS usando a CPU:
/// prioridade um pouco acima do normal (nunca alta, para não atrapalhar a transmissão) e sem o "modo de economia"
/// do Windows 11, que reduz a frequência dos temporizadores de programas minimizados.
/// </summary>
internal static class ProcessTuning
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessPowerThrottlingState { public uint Version, ControlMask, StateMask; }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref ProcessPowerThrottlingState info, int size);

    public static void Apply()
    {
        try
        {
            using var p = Process.GetCurrentProcess();
            p.PriorityClass = ProcessPriorityClass.AboveNormal;
        }
        catch { }
        try
        {
            // controla EXECUTION_SPEED (0x1) e IGNORE_TIMER_RESOLUTION (0x4); estado 0 = "não reduzir"
            var st = new ProcessPowerThrottlingState { Version = 1, ControlMask = 0x1 | 0x4, StateMask = 0 };
            using var p = Process.GetCurrentProcess();
            SetProcessInformation(p.Handle, 4, ref st, Marshal.SizeOf<ProcessPowerThrottlingState>());   // 4 = ProcessPowerThrottling
        }
        catch { }
    }
}
