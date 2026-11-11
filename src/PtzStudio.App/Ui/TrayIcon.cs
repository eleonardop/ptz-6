using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace PtzStudio.App.Ui;

/// <summary>
/// Ícone ao lado do relógio do Windows (área de notificação), feito direto com a API do Windows:
/// não usa Windows Forms e quase não consome recursos. Clique abre a janela; botão direito abre o menu.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const int WmTray = 0x8000 + 1;              // WM_APP + 1
    private const int WmLButtonUp = 0x0202, WmLButtonDblClk = 0x0203, WmRButtonUp = 0x0205, WmNull = 0;
    private const uint NimAdd = 0, NimModify = 1, NimDelete = 2;
    private const uint NifMessage = 0x1, NifIcon = 0x2, NifTip = 0x4, NifInfo = 0x10;
    private const uint NiifInfo = 0x1, NiifNoSound = 0x10;
    private const uint MfString = 0x0, MfGrayed = 0x1, MfSeparator = 0x800;
    private const uint TpmRightButton = 0x2, TpmReturnCmd = 0x100, TpmNoNotify = 0x80;
    private const int IdOpen = 1, IdExit = 4;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[]? small, uint count);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string? text);
    [DllImport("user32.dll")] private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr reserved);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);

    private readonly HwndSource _source;
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private IntPtr _icon;
    private bool _ownsIcon;
    private bool _added;
    private string _tip;

    public event Action? OpenRequested, ExitRequested;

    public TrayIcon(string tip)
    {
        _tip = Trim(tip, 127);
        // janela invisível "somente mensagens": só recebe os cliques do ícone
        _source = new HwndSource(new HwndSourceParameters("PTZStudioTray") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
        _source.AddHook(WndProc);
        LoadAppIcon();
    }

    private void LoadAppIcon()
    {
        try
        {
            var small = new IntPtr[1];
            string? exe = Environment.ProcessPath;
            if (exe != null && ExtractIconEx(exe, 0, null, small, 1) > 0 && small[0] != IntPtr.Zero)
            {
                _icon = small[0]; _ownsIcon = true; return;
            }
        }
        catch { }
        _icon = LoadIcon(IntPtr.Zero, new IntPtr(32512));   // IDI_APPLICATION: ícone padrão do Windows, se o do programa faltar
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max];

    private NotifyIconData Data(uint flags) => new()
    {
        cbSize = Marshal.SizeOf<NotifyIconData>(), hWnd = _source.Handle, uID = 1, uFlags = flags,
        uCallbackMessage = WmTray, hIcon = _icon, szTip = _tip, szInfo = "", szInfoTitle = ""
    };

    /// <summary>Mostra o ícone ao lado do relógio.</summary>
    public void Show()
    {
        var d = Data(NifMessage | NifIcon | NifTip);
        _added = Shell_NotifyIcon(NimAdd, ref d);
    }

    /// <summary>Texto que aparece ao passar o mouse sobre o ícone (máx. 127 caracteres).</summary>
    public void SetTip(string tip)
    {
        _tip = Trim(tip, 127);
        if (!_added) return;
        var d = Data(NifTip);
        Shell_NotifyIcon(NimModify, ref d);
    }

    /// <summary>Aviso pequeno (balão) perto do relógio, sem som.</summary>
    public void ShowBalloon(string title, string text)
    {
        if (!_added) return;
        var d = Data(NifInfo);
        d.szInfoTitle = Trim(title, 63); d.szInfo = Trim(text, 255); d.dwInfoFlags = NiifInfo | NiifNoSound;
        Shell_NotifyIcon(NimModify, ref d);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmTray)
        {
            int mouse = (int)(lParam.ToInt64() & 0xFFFF);
            if (mouse == WmLButtonUp || mouse == WmLButtonDblClk) { OpenRequested?.Invoke(); handled = true; }
            else if (mouse == WmRButtonUp) { ShowMenu(); handled = true; }
        }
        else if (_taskbarCreated != 0 && (uint)msg == _taskbarCreated)
        {
            if (_added) Show();      // o Explorer reiniciou: o Windows apagou o ícone, então colocamos de novo
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            AppendMenu(menu, MfString, new UIntPtr((uint)IdOpen), "Abrir PTZ Studio");
            AppendMenu(menu, MfSeparator, UIntPtr.Zero, null);
            AppendMenu(menu, MfString, new UIntPtr((uint)IdExit), "Sair do PTZ Studio");
            GetCursorPos(out var p);
            SetForegroundWindow(_source.Handle);    // sem isto o menu não fecha ao clicar fora
            int cmd = TrackPopupMenuEx(menu, TpmRightButton | TpmReturnCmd | TpmNoNotify, p.X, p.Y, _source.Handle, IntPtr.Zero);
            PostMessage(_source.Handle, WmNull, IntPtr.Zero, IntPtr.Zero);
            switch (cmd)
            {
                case IdOpen: OpenRequested?.Invoke(); break;
                case IdExit: ExitRequested?.Invoke(); break;
            }
        }
        finally { DestroyMenu(menu); }
    }

    public void Dispose()
    {
        try
        {
            if (_added)
            {
                var d = Data(0);
                Shell_NotifyIcon(NimDelete, ref d);
                _added = false;
            }
        }
        catch { }
        try { _source.RemoveHook(WndProc); _source.Dispose(); } catch { }
        if (_ownsIcon && _icon != IntPtr.Zero) DestroyIcon(_icon);
        _icon = IntPtr.Zero;
    }
}
