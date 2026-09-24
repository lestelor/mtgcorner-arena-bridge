using System.Runtime.InteropServices;

namespace MtgCornerArenaBridge;

/// <summary>
/// LAS CARTAS, GRANDES, ENCIMA DE ARENA.
///
/// La columna ya llevaba a la web («Similares a Plains»), pero eso es salir del
/// juego: hay que cambiar de ventana, y a media partida no se hace. Pedido por
/// el usuario el 2026-09-24: que las cartas parecidas salgan EN Arena, grandes,
/// y se puedan cerrar.
///
/// Es otra ventana propia, como la columna y como el aviso (ver Columna.cs):
/// Arena no admite complementos, así que lo que se pinta encima es una ventana
/// sin bordes, siempre visible y sin barra de tareas, colocada sobre la del
/// juego. PULSABLE pero SIN ROBAR EL FOCO —`WS_EX_NOACTIVATE` y
/// `WM_MOUSEACTIVATE` → `MA_NOACTIVATE`—: pulsar una carta no puede sacarte de
/// la partida.
///
/// LAS IMÁGENES SE DIBUJAN CON GDI+, que es lo que sabe abrir un JPEG; GDI a
/// secas sólo entiende mapas de bits. Se bajan a una carpeta en %TEMP% y se
/// quedan ahí: la misma carta señalada dos veces no se vuelve a bajar, y
/// borrarlas no rompe nada.
///
/// ARRIBA DEL TODO, la carta de la que se parte, para que no haya que adivinar
/// de qué carta se está hablando («no sé a qué carta se refiere hasta que le
/// doy», el usuario) — el título la nombra y su imagen abre la fila.
/// </summary>
internal static class PanelCartas
{
    /// <summary>Una carta del panel: su imagen ya bajada y a dónde lleva en la web.</summary>
    public sealed record Carta(string Nombre, string Fichero, string? Ruta, double? PrecioUsd);

    // ── Medidas ──────────────────────────────────────────────────────────
    private const int ANCHO_CARTA = 170, ALTO_CARTA = 237;   // proporción de una carta (488×680)
    private const int AIRE = 14, MARGEN = 18, ALTO_CABECERA = 44, ALTO_PIE = 22;
    private const int POR_FILA = 4;

    // ── Windows ──────────────────────────────────────────────────────────

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc; public bool fErase; public RECT rcPaint; public bool fRestore, fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName, lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TRACKMOUSEEVENT { public uint cbSize, dwFlags; public IntPtr hwndTrack; public uint dwHoverTime; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WNDCLASSEX clase);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string clase, string titulo, uint estilo,
        int x, int y, int ancho, int alto, IntPtr padre, IntPtr menu, IntPtr instancia, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int comando);
    [DllImport("user32.dll")] private static extern bool UpdateWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int codigo);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr SetTimer(IntPtr hWnd, IntPtr id, uint ms, IntPtr fn);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint clave, byte alfa, uint banderas);
    [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT ps);
    [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] private static extern int FillRect(IntPtr hdc, ref RECT rc, IntPtr pincel);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int DrawText(IntPtr hdc, string texto, int largo, ref RECT rc, uint formato);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rc);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hWnd, IntPtr region, bool repintar);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr despues, int x, int y, int ancho, int alto, uint banderas);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr hWnd, IntPtr rc, bool borrar);
    [DllImport("user32.dll")] private static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT e);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr LoadCursor(IntPtr instancia, int cursor);
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr contexto);

    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int izq, int arriba, int der, int abajo, int anchoElipse, int altoElipse);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFont(int alto, int ancho, int escape, int orientacion, int grosor,
        uint cursiva, uint subrayado, uint tachado, uint juego, uint precision, uint recorte, uint calidad, uint paso, string cara);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr objeto);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr objeto);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr hdc, int modo);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr hdc, uint color);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? nombre);

    // GDI+: lo único que sabe abrir un JPEG.
    [StructLayout(LayoutKind.Sequential)]
    private struct GdiplusStartupInput { public int GdiplusVersion; public IntPtr DebugEventCallback; public bool SuppressBackgroundThread, SuppressExternalCodecs; }
    [DllImport("gdiplus.dll")] private static extern int GdiplusStartup(out IntPtr token, ref GdiplusStartupInput entrada, IntPtr salida);
    [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)] private static extern int GdipLoadImageFromFile(string fichero, out IntPtr imagen);
    [DllImport("gdiplus.dll")] private static extern int GdipDisposeImage(IntPtr imagen);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateFromHDC(IntPtr hdc, out IntPtr grafico);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteGraphics(IntPtr grafico);
    [DllImport("gdiplus.dll")] private static extern int GdipSetInterpolationMode(IntPtr grafico, int modo);
    [DllImport("gdiplus.dll")] private static extern int GdipDrawImageRectI(IntPtr grafico, IntPtr imagen, int x, int y, int ancho, int alto);

    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x8000000;
    private const uint WM_DESTROY = 0x2, WM_CLOSE = 0x10, WM_PAINT = 0xF, WM_TIMER = 0x113;
    private const uint WM_MOUSEMOVE = 0x200, WM_LBUTTONUP = 0x202, WM_MOUSELEAVE = 0x2A3, WM_MOUSEACTIVATE = 0x21;
    private const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40, SWP_HIDEWINDOW = 0x80;
    private const uint LWA_ALPHA = 0x2, TME_LEAVE = 0x2;
    private const uint DT_LEFT = 0x0, DT_CENTER = 0x1, DT_RIGHT = 0x2, DT_VCENTER = 0x4, DT_SINGLELINE = 0x20, DT_END_ELLIPSIS = 0x8000;
    private const int TRANSPARENT_BK = 1, SW_SHOWNOACTIVATE = 4, MA_NOACTIVATE = 3;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    private static uint Rgb(int r, int g, int b) => (uint)(r | (g << 8) | (b << 16));

    private static WndProc? procedimiento;
    private static IntPtr ventana;
    private static Thread? hilo;
    private static string titulo = "";
    private static Carta[] cartas = [];
    private static int bajoRaton = -1;
    private static IntPtr gdiplus;

    /// <summary>Modo de taller: se ve aunque Arena no esté delante.</summary>
    public static bool SiempreVisible { get; set; }

    private static int Ancho => MARGEN * 2 + POR_FILA * ANCHO_CARTA + (POR_FILA - 1) * AIRE;
    private static int Filas => Math.Max(1, (cartas.Length + POR_FILA - 1) / POR_FILA);
    private static int Alto => ALTO_CABECERA + MARGEN + Filas * ALTO_CARTA + (Filas - 1) * AIRE + ALTO_PIE + MARGEN;

    /// <summary>
    /// Enseña el panel con estas cartas. Si ya había uno, se sustituye: dos
    /// paneles encima del juego no los quiere nadie.
    /// </summary>
    public static void Mostrar(string tituloPanel, IReadOnlyList<Carta> lista)
    {
        Cerrar();
        titulo = tituloPanel;
        cartas = [.. lista];
        bajoRaton = -1;
        if (cartas.Length == 0) return;
        hilo = new Thread(Correr) { IsBackground = true, Name = "panel" };
        hilo.Start();
    }

    /// <summary>Lo quita. Se puede llamar desde cualquier hilo.</summary>
    public static void Cerrar()
    {
        var v = ventana;
        if (v != IntPtr.Zero) PostMessage(v, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        var h = hilo;
        if (h is not null && h != Thread.CurrentThread) h.Join(TimeSpan.FromSeconds(2));
    }

    /// <summary>Lo que se hace al pulsar una carta: abrir su página. Lo pone quien monta el panel.</summary>
    public static Action<Carta>? AlPulsar { get; set; }

    private static void Correr()
    {
        try
        {
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { /* Windows viejos */ }
            IniciarGdiPlus();

            procedimiento = Procedimiento;
            var instancia = GetModuleHandle(null);
            var clase = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(procedimiento),
                hInstance = instancia,
                hCursor = LoadCursor(IntPtr.Zero, 32512),   // IDC_ARROW
                lpszClassName = "MtgCornerPanel",
            };
            RegisterClassEx(ref clase);

            var (x, y, ver) = Donde();
            ventana = CreateWindowEx(
                WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE,
                "MtgCornerPanel", "MTG Corner", WS_POPUP,
                x, y, Ancho, Alto, IntPtr.Zero, IntPtr.Zero, instancia, IntPtr.Zero);
            if (ventana == IntPtr.Zero) return;

            SetWindowRgn(ventana, CreateRoundRectRgn(0, 0, Ancho + 1, Alto + 1, 16, 16), true);
            SetLayeredWindowAttributes(ventana, 0, 244, LWA_ALPHA);
            SetTimer(ventana, new IntPtr(1), 500, IntPtr.Zero);   // seguir a Arena
            ShowWindow(ventana, ver ? SW_SHOWNOACTIVATE : 0);
            UpdateWindow(ventana);

            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        catch { /* sin escritorio o sin permisos: el programa sigue sin panel */ }
        finally { ventana = IntPtr.Zero; hilo = null; }
    }

    private static void IniciarGdiPlus()
    {
        if (gdiplus != IntPtr.Zero) return;
        var entrada = new GdiplusStartupInput { GdiplusVersion = 1 };
        GdiplusStartup(out gdiplus, ref entrada, IntPtr.Zero);
    }

    /// <summary>Centrado sobre Arena, y sólo con Arena delante (o el propio panel).</summary>
    private static (int X, int Y, bool Ver) Donde()
    {
        var arena = Columna.VentanaDeArena();
        if (arena == IntPtr.Zero || IsIconic(arena) || !GetWindowRect(arena, out var r) || r.Right <= r.Left)
        {
            return (40, 40, SiempreVisible);
        }
        var delante = GetForegroundWindow();
        var ver = SiempreVisible || delante == arena || delante == ventana;
        return (r.Left + (r.Right - r.Left - Ancho) / 2, r.Top + (r.Bottom - r.Top - Alto) / 2, ver);
    }

    private static void Recolocar()
    {
        var (x, y, ver) = Donde();
        SetWindowPos(ventana, HWND_TOPMOST, x, y, Ancho, Alto, SWP_NOACTIVATE | (ver ? SWP_SHOWWINDOW : SWP_HIDEWINDOW));
    }

    /// <summary>Qué hay bajo un punto de la ventana: una carta (0..n), la X (-2), o nada (-1).</summary>
    private static int QueHayEn(IntPtr lParam)
    {
        var x = (short)(lParam.ToInt64() & 0xFFFF);
        var y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        if (y < ALTO_CABECERA) return x >= Ancho - 44 ? -2 : -1;
        var dentroY = y - ALTO_CABECERA - MARGEN;
        var fila = dentroY / (ALTO_CARTA + AIRE);
        if (fila < 0 || fila >= Filas || dentroY - fila * (ALTO_CARTA + AIRE) > ALTO_CARTA) return -1;
        var dentroX = x - MARGEN;
        var col = dentroX / (ANCHO_CARTA + AIRE);
        if (col < 0 || col >= POR_FILA || dentroX - col * (ANCHO_CARTA + AIRE) > ANCHO_CARTA) return -1;
        var i = fila * POR_FILA + col;
        return i < cartas.Length ? i : -1;
    }

    private static IntPtr Procedimiento(IntPtr hWnd, uint mensaje, IntPtr wParam, IntPtr lParam)
    {
        switch (mensaje)
        {
            case WM_PAINT:
                Pintar(hWnd);
                return IntPtr.Zero;

            // Pulsar el panel NO saca a nadie de la partida.
            case WM_MOUSEACTIVATE:
                return new IntPtr(MA_NOACTIVATE);

            case WM_MOUSEMOVE:
            {
                var i = QueHayEn(lParam);
                if (i != bajoRaton) { bajoRaton = i; InvalidateRect(hWnd, IntPtr.Zero, false); }
                var t = new TRACKMOUSEEVENT { cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(), dwFlags = TME_LEAVE, hwndTrack = hWnd, dwHoverTime = 0 };
                TrackMouseEvent(ref t);
                return IntPtr.Zero;
            }

            case WM_MOUSELEAVE:
                bajoRaton = -1;
                InvalidateRect(hWnd, IntPtr.Zero, false);
                return IntPtr.Zero;

            case WM_LBUTTONUP:
            {
                var i = QueHayEn(lParam);
                if (i == -2) { PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); return IntPtr.Zero; }
                if (i >= 0 && i < cartas.Length && AlPulsar is { } abrir)
                {
                    var carta = cartas[i];
                    _ = Task.Run(() => { try { abrir(carta); } catch { /* lo cuenta quien lo montó */ } });
                }
                return IntPtr.Zero;
            }

            case WM_TIMER:
                Recolocar();
                return IntPtr.Zero;

            case WM_CLOSE:
                DestroyWindow(hWnd);
                return IntPtr.Zero;

            case WM_DESTROY:
                PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return DefWindowProc(hWnd, mensaje, wParam, lParam);
    }

    private static void Pintar(IntPtr hWnd)
    {
        var hdc = BeginPaint(hWnd, out var ps);
        var fondo = CreateSolidBrush(Rgb(9, 13, 24));
        var marco = CreateSolidBrush(Rgb(56, 189, 248));
        var resalte = CreateSolidBrush(Rgb(26, 34, 54));
        var fTitulo = CreateFont(19, 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        var fPie = CreateFont(15, 0, 0, 0, 500, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        var fCerrar = CreateFont(17, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe MDL2 Assets");
        var grafico = IntPtr.Zero;
        try
        {
            var todo = new RECT { Left = 0, Top = 0, Right = Ancho, Bottom = Alto };
            FillRect(hdc, ref todo, fondo);
            SetBkMode(hdc, TRANSPARENT_BK);

            // Cabecera: de qué carta se parte y la X.
            SelectObject(hdc, fTitulo);
            SetTextColor(hdc, Rgb(252, 211, 77));
            var rTitulo = new RECT { Left = MARGEN, Top = 0, Right = Ancho - 50, Bottom = ALTO_CABECERA };
            DrawText(hdc, titulo, -1, ref rTitulo, DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);

            var rX = new RECT { Left = Ancho - 44, Top = 0, Right = Ancho, Bottom = ALTO_CABECERA };
            if (bajoRaton == -2) FillRect(hdc, ref rX, resalte);
            SelectObject(hdc, fCerrar);
            SetTextColor(hdc, bajoRaton == -2 ? Rgb(255, 255, 255) : Rgb(186, 196, 214));
            DrawText(hdc, "", -1, ref rX, DT_CENTER | DT_VCENTER | DT_SINGLELINE);

            GdipCreateFromHDC(hdc, out grafico);
            if (grafico != IntPtr.Zero) GdipSetInterpolationMode(grafico, 7);   // bicúbica de calidad

            for (var i = 0; i < cartas.Length; i++)
            {
                var fila = i / POR_FILA;
                var col = i % POR_FILA;
                var x = MARGEN + col * (ANCHO_CARTA + AIRE);
                var y = ALTO_CABECERA + MARGEN + fila * (ALTO_CARTA + AIRE);

                // Bajo el ratón: un filo azul alrededor, que se vea cuál se va a abrir.
                if (i == bajoRaton)
                {
                    var r = new RECT { Left = x - 3, Top = y - 3, Right = x + ANCHO_CARTA + 3, Bottom = y + ALTO_CARTA + 3 };
                    FillRect(hdc, ref r, marco);
                }

                if (grafico != IntPtr.Zero && GdipLoadImageFromFile(cartas[i].Fichero, out var imagen) == 0)
                {
                    GdipDrawImageRectI(grafico, imagen, x, y, ANCHO_CARTA, ALTO_CARTA);
                    GdipDisposeImage(imagen);
                }
                else
                {
                    // Sin imagen, el nombre: mejor un hueco con letra que un agujero.
                    var r = new RECT { Left = x, Top = y, Right = x + ANCHO_CARTA, Bottom = y + ALTO_CARTA };
                    FillRect(hdc, ref r, resalte);
                    SelectObject(hdc, fPie);
                    SetTextColor(hdc, Rgb(226, 232, 240));
                    DrawText(hdc, cartas[i].Nombre, -1, ref r, DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
                }
            }

            // El pie: qué pasa al pulsar una carta.
            SelectObject(hdc, fPie);
            SetTextColor(hdc, Rgb(148, 163, 184));
            var rPie = new RECT { Left = MARGEN, Top = Alto - ALTO_PIE - MARGEN / 2, Right = Ancho - MARGEN, Bottom = Alto };
            var precio = bajoRaton >= 0 && bajoRaton < cartas.Length && cartas[bajoRaton].PrecioUsd is { } p
                ? $"{cartas[bajoRaton].Nombre} · ${p:0.00}"
                : Textos.T("panel_pie");
            DrawText(hdc, precio, -1, ref rPie, DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
        }
        finally
        {
            if (grafico != IntPtr.Zero) GdipDeleteGraphics(grafico);
            EndPaint(hWnd, ref ps);
            DeleteObject(fondo);
            DeleteObject(marco);
            DeleteObject(resalte);
            DeleteObject(fTitulo);
            DeleteObject(fPie);
            DeleteObject(fCerrar);
        }
    }
}
