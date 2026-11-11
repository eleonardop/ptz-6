using System.IO;
using System.Windows;
using PtzStudio.Core.Storage;

namespace PtzStudio.App;

public partial class App : Application
{
    private DateTime _lastBox = DateTime.MinValue;
    private static Mutex? _single;
    private EventWaitHandle? _showSignal;

    /// <summary>
    /// Só uma cópia do programa pode controlar a câmera. Se o usuário abrir outra (por exemplo, com a primeira escondida
    /// ao lado do relógio), a nova apenas pede à primeira para aparecer e fecha.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        _single = new Mutex(true, @"Local\PTZStudio.SingleInstance", out bool first);
        if (!first)
        {
            try { EventWaitHandle.OpenExisting(@"Local\PTZStudio.Show").Set(); } catch { }
            Shutdown();
            return;
        }
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\PTZStudio.Show");
        var waiter = new Thread(() =>
        {
            while (_showSignal.WaitOne())
                Dispatcher.BeginInvoke(new Action(() => (MainWindow as PtzStudio.App.MainWindow)?.ShowFromTray()));
        }) { IsBackground = true, Name = "PTZStudio-Show" };
        waiter.Start();
        Ui.ProcessTuning.Apply();
        base.OnStartup(e);
    }

    public App()
    {
        // Só para falhas inesperadas (não há registro contínuo de uso). O arquivo é pequeno e limitado a 200 KB.
        DispatcherUnhandledException += (_, e) =>
        {
            try
            {
                var dir = Path.Combine(SettingsStore.DefaultDirectory(), "logs");
                var file = Path.Combine(dir, "erros.log");
                Directory.CreateDirectory(dir);
                if (!File.Exists(file) || new FileInfo(file).Length < 200_000)
                    File.AppendAllText(file, $"{DateTime.Now:s} {e.Exception}\n\n");
            }
            catch { }
            if ((DateTime.Now - _lastBox).TotalSeconds > 5)   // evita uma pilha de caixas de erro se algo falhar em repetição
            {
                _lastBox = DateTime.Now;
                MessageBox.Show("Ocorreu um erro inesperado, mas o PTZ Studio continua aberto.\n\n" + e.Exception.Message,
                    "PTZ Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            e.Handled = true;
        };
    }
}
