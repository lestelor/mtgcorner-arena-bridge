using System.Runtime.InteropServices;

namespace MtgCornerArenaBridge;

/// <summary>
/// EL RASTREADOR DE TU MAZO EN PARTIDA, como el de Untapped (pedido del
/// usuario el 2026-10-03).
///
/// Una lista estrecha pegada al borde izquierdo de Arena, debajo de la columna:
/// cada carta del mazo con cuántas quedan en la biblioteca y la probabilidad de
/// robarla en el siguiente robo, y arriba cuántas cartas quedan y la de robar
/// tierra. Las que ya no quedan se apagan. Pulsando la cabecera se pliega.
///
/// Sólo con partida y con Arena delante. Los datos los pone Program: el mazo
/// sale del registro (Contexto.CartasMazo) y lo que ya ha salido de la
/// biblioteca, de las zonas de la partida (Contexto.FueraDeBiblioteca).
///
/// Win32 a pelo, como la columna: ventana sin bordes, siempre encima, que no
/// coge el foco (WS_EX_NOACTIVATE y MA_NOACTIVATE): pulsarla no te saca del
/// juego.
/// </summary>
internal static class Rastreador
{
    /// <summary>Una carta del mazo: cuántas quedan de las que tiene el mazo, y su ilustración en disco.</summary>
    public sealed record Fila(int Grp, string Nombre, int Quedan, int Total, double Cmc, bool Tierra, string? Arte);

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
    [StructLayout(LayoutKind.Sequential)] private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize, style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }
    [StructLayout(LayoutKind.Sequential)] private struct TRACKMOUSEEVENT { public uint cbSize, dwFlags; public IntPtr hwndTrack; public uint dwHoverTime; }
    [StructLayout(LayoutKind.Sequential)] private struct PuntoG { public int X, Y; }
    private struct GdiplusStartupInput { public int GdiplusVersion; public IntPtr DebugEventCallback; public bool SuppressBackgroundThread, SuppressExternalCodecs; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WNDCLASSEX clase);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint exStyle, string clase, string titulo, uint estilo, int x, int y, int ancho, int alto, IntPtr padre, IntPtr menu, IntPtr instancia, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int comando);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int codigo);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
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
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int ancho, int alto);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr destino, int x, int y, int ancho, int alto, IntPtr origen, int x1, int y1, uint op);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int izq, int arriba, int der, int abajo, int anchoElipse, int altoElipse);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFont(int alto, int ancho, int escape, int orientacion, int grosor, uint cursiva, uint subrayado, uint tachado, uint juego, uint precision, uint recorte, uint calidad, uint paso, string cara);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr objeto);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr objeto);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr hdc, int modo);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr hdc, uint color);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? nombre);
    [DllImport("gdiplus.dll")] private static extern int GdiplusStartup(out IntPtr token, ref GdiplusStartupInput entrada, IntPtr salida);
    [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)] private static extern int GdipLoadImageFromFile(string fichero, out IntPtr imagen);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateFromHDC(IntPtr hdc, out IntPtr grafico);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteGraphics(IntPtr grafico);
    [DllImport("gdiplus.dll")] private static extern int GdipSetInterpolationMode(IntPtr grafico, int modo);
    [DllImport("gdiplus.dll")] private static extern int GdipDrawImageRectRectI(IntPtr g, IntPtr imagen, int dx, int dy, int dw, int dh, int sx, int sy, int sw, int sh, int unidad, IntPtr atributos, IntPtr llamada, IntPtr datos);
    [DllImport("gdiplus.dll")] private static extern int GdipGetImageWidth(IntPtr imagen, out uint ancho);
    [DllImport("gdiplus.dll")] private static extern int GdipGetImageHeight(IntPtr imagen, out uint alto);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateLineBrushI(ref PuntoG p1, ref PuntoG p2, uint c1, uint c2, int envoltura, out IntPtr brocha);
    [DllImport("gdiplus.dll")] private static extern int GdipFillRectangleI(IntPtr g, IntPtr brocha, int x, int y, int ancho, int alto);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteBrush(IntPtr brocha);

    private const uint WS_POPUP = 0x80000000, WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x8000000;
    private const uint WM_DESTROY = 0x2, WM_PAINT = 0xF, WM_ERASEBKGND = 0x14, WM_CLOSE = 0x10, WM_MOUSEACTIVATE = 0x21, WM_TIMER = 0x113;
    private const uint WM_MOUSEMOVE = 0x200, WM_LBUTTONUP = 0x202, WM_MOUSELEAVE = 0x2A3;
    private const uint DT_LEFT = 0x0, DT_RIGHT = 0x2, DT_CENTER = 0x1, DT_VCENTER = 0x4, DT_SINGLELINE = 0x20, DT_END_ELLIPSIS = 0x8000;
    private const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40, SWP_HIDEWINDOW = 0x80, SWP_NOZORDER = 0x4, LWA_ALPHA = 0x2, TME_LEAVE = 0x2;
    private const int MA_NOACTIVATE = 3, SW_HIDE = 0, TRANSPARENT_BK = 1, IDC_ARROW = 32512;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static uint Rgb(int r, int g, int b) => (uint)(r | (g << 8) | (b << 16));

    // ── Medidas y estado ──────────────────────────────────────────────────
    private const int ANCHO = 236, ALTO_CABECERA = 48, MARGEN_BORDE = 10, ANCHO_ARTE = 84;
    private static int altoFila = 22;
    private static volatile Fila[] filas = [];
    private static volatile int biblioteca;
    private static volatile bool activo, plegado;
    private static int bajoRaton = -1;   // -2 la cabecera; i la fila i
    private static bool siguiendo;
    /// <summary>Cuántas filas caben, calculado al colocar la ventana.</summary>
    private static int visibles;
    private static int altoRegion;
    private static IntPtr ventana, gdiplus;
    private static Thread? hilo;
    private static WndProc? procedimiento;
    private static readonly Dictionary<string, IntPtr> imagenes = new();
    /// <summary>Modo de taller: se ve aunque Arena no sea la ventana activa.</summary>
    public static bool SiempreVisible;

    /// <summary>Las cartas y la biblioteca de ahora. Por coste como en el desplegable, las tierras al final.</summary>
    public static void Actualizar(IReadOnlyList<Fila> nuevas, int tamBiblioteca)
    {
        filas = nuevas.OrderBy(f => f.Tierra).ThenBy(f => f.Cmc).ThenBy(f => f.Nombre, StringComparer.OrdinalIgnoreCase).ToArray();
        biblioteca = tamBiblioteca;
        if (ventana != IntPtr.Zero) InvalidateRect(ventana, IntPtr.Zero, false);
    }

    /// <summary>Con partida, se ve (y se crea la primera vez); sin ella, se esconde. Una partida nueva lo despliega.</summary>
    public static void Activo(bool enPartida)
    {
        if (enPartida && !activo) plegado = false;
        activo = enPartida;
        if (enPartida && hilo is null)
        {
            hilo = new Thread(Correr) { IsBackground = true, Name = "rastreador" };
            hilo.Start();
        }
    }

    public static void Cerrar()
    {
        var v = ventana;
        if (v != IntPtr.Zero) PostMessage(v, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    private static int Alto => plegado ? ALTO_CABECERA : ALTO_CABECERA + Math.Min(visibles, filas.Length) * altoFila + 8;

    private static void Correr()
    {
        try
        {
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { /* Windows viejos */ }
            procedimiento = Procedimiento;
            var instancia = GetModuleHandle(null);
            var clase = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(procedimiento),
                hInstance = instancia,
                hCursor = LoadCursor(IntPtr.Zero, IDC_ARROW),
                lpszClassName = "MtgCornerRastreador",
            };
            RegisterClassEx(ref clase);
            ventana = CreateWindowEx(WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE,
                "MtgCornerRastreador", "MTG Corner", WS_POPUP, 0, 0, ANCHO, ALTO_CABECERA, IntPtr.Zero, IntPtr.Zero, instancia, IntPtr.Zero);
            if (ventana == IntPtr.Zero) return;
            SetLayeredWindowAttributes(ventana, 0, 232, LWA_ALPHA);
            SetTimer(ventana, new IntPtr(1), 500, IntPtr.Zero);
            Recolocar();
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        catch { /* sin escritorio: el programa sigue sin rastreador */ }
        finally { ventana = IntPtr.Zero; hilo = null; }
    }

    /// <summary>
    /// Pegado al borde izquierdo de Arena, por debajo de la columna (que está
    /// en el tercio de arriba), y sin bajar hasta el retrato y la biblioteca de
    /// la esquina. Si no caben todas las filas, se estrechan; si aún no, se
    /// enseñan las que quepan.
    /// </summary>
    private static void Recolocar()
    {
        var arena = Columna.VentanaDeArena();
        var hay = arena != IntPtr.Zero && !IsIconic(arena) && GetWindowRect(arena, out var r) && r.Right > r.Left;
        var ver = activo && filas.Length > 0 && hay && (SiempreVisible || GetForegroundWindow() == arena || GetForegroundWindow() == ventana);
        if (!hay) { SetWindowPos(ventana, HWND_TOPMOST, 0, 0, ANCHO, Alto, SWP_NOACTIVATE | SWP_HIDEWINDOW); return; }
        GetWindowRect(arena, out r);
        var altoArena = r.Bottom - r.Top;
        var arriba = r.Top + altoArena * 46 / 100;
        var libre = r.Bottom - 170 - arriba - ALTO_CABECERA - 8;
        altoFila = filas.Length * 22 <= libre ? 22 : 18;
        visibles = Math.Max(1, libre / altoFila);
        // Sin tocar el orden: ya nace encima de todo (WS_EX_TOPMOST), y subirse
        // cada medio segundo le disputaba el sitio a la columna cuando ésta,
        // abierta, se le pone encima: parpadeaban.
        SetWindowPos(ventana, HWND_TOPMOST, r.Left + MARGEN_BORDE, arriba, ANCHO, Alto, SWP_NOACTIVATE | SWP_NOZORDER | (ver ? SWP_SHOWWINDOW : SWP_HIDEWINDOW));
        if (altoRegion != Alto)
        {
            altoRegion = Alto;
            SetWindowRgn(ventana, CreateRoundRectRgn(0, 0, ANCHO + 1, Alto + 1, 12, 12), true);
        }
    }

    private static int QueHayEn(IntPtr lParam)
    {
        var y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        if (y < ALTO_CABECERA) return -2;
        if (plegado) return -1;
        var i = (y - ALTO_CABECERA) / altoFila;
        return i >= 0 && i < Math.Min(visibles, filas.Length) ? i : -1;
    }

    private static IntPtr Procedimiento(IntPtr hWnd, uint mensaje, IntPtr wParam, IntPtr lParam)
    {
        switch (mensaje)
        {
            case WM_PAINT: Pintar(hWnd); return IntPtr.Zero;
            case WM_ERASEBKGND: return new IntPtr(1);
            case WM_MOUSEACTIVATE: return new IntPtr(MA_NOACTIVATE);
            case WM_TIMER: Recolocar(); return IntPtr.Zero;
            case WM_MOUSEMOVE:
            {
                if (!siguiendo)
                {
                    var t = new TRACKMOUSEEVENT { cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(), dwFlags = TME_LEAVE, hwndTrack = hWnd };
                    siguiendo = TrackMouseEvent(ref t);
                }
                var i = QueHayEn(lParam);
                if (i != bajoRaton) { bajoRaton = i; InvalidateRect(hWnd, IntPtr.Zero, false); }
                return IntPtr.Zero;
            }
            case WM_MOUSELEAVE:
                siguiendo = false;
                bajoRaton = -1;
                InvalidateRect(hWnd, IntPtr.Zero, false);
                return IntPtr.Zero;
            case WM_LBUTTONUP:
                // La cabecera pliega y despliega.
                if (QueHayEn(lParam) == -2) { plegado = !plegado; Recolocar(); InvalidateRect(hWnd, IntPtr.Zero, false); }
                return IntPtr.Zero;
            case WM_CLOSE: DestroyWindow(hWnd); return IntPtr.Zero;
            case WM_DESTROY: PostQuitMessage(0); return IntPtr.Zero;
        }
        return DefWindowProc(hWnd, mensaje, wParam, lParam);
    }

    private static void IniciarGdiPlus()
    {
        if (gdiplus != IntPtr.Zero) return;
        var e = new GdiplusStartupInput { GdiplusVersion = 1 };
        GdiplusStartup(out gdiplus, ref e, IntPtr.Zero);
    }

    private static IntPtr Imagen(string fichero)
    {
        IniciarGdiPlus();
        if (!imagenes.TryGetValue(fichero, out var img))
        {
            img = GdipLoadImageFromFile(fichero, out var cargada) == 0 ? cargada : IntPtr.Zero;
            imagenes[fichero] = img;
        }
        return img;
    }

    private static string Pct(int quedan, int total) => total <= 0 ? "—" : $"{Math.Round(100.0 * quedan / total)}%";

    private static void Pintar(IntPtr hWnd)
    {
        var pantalla = BeginPaint(hWnd, out var ps);
        var alto = Alto;
        var hdc = CreateCompatibleDC(pantalla);
        var lienzo = CreateCompatibleBitmap(pantalla, ANCHO, Math.Max(1, alto));
        var anterior = SelectObject(hdc, lienzo);
        var fCabeza = CreateFont(13, 0, 0, 0, 800, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        var fSub = CreateFont(13, 0, 0, 0, 500, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        var fFila = CreateFont(altoFila <= 18 ? 13 : 14, 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        var fGlifo = CreateFont(14, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe MDL2 Assets");
        try
        {
            var todo = new RECT { Left = 0, Top = 0, Right = ANCHO, Bottom = alto };
            var fondo = CreateSolidBrush(Rgb(9, 13, 24)); FillRect(hdc, ref todo, fondo); DeleteObject(fondo);
            SetBkMode(hdc, TRANSPARENT_BK);
            var lista = filas;
            var tam = biblioteca;

            // La cabecera: cuántas quedan y la probabilidad de robar tierra; la flecha de plegar.
            if (bajoRaton == -2) { var r0 = new RECT { Left = 0, Top = 0, Right = ANCHO, Bottom = ALTO_CABECERA }; var p0 = CreateSolidBrush(Rgb(17, 24, 39)); FillRect(hdc, ref r0, p0); DeleteObject(p0); }
            SelectObject(hdc, fCabeza);
            SetTextColor(hdc, Rgb(252, 211, 77));
            var rT = new RECT { Left = 12, Top = 6, Right = ANCHO - 30, Bottom = 24 };
            DrawText(hdc, Textos.T("rast_biblioteca", tam).ToUpperInvariant(), -1, ref rT, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
            var tierras = lista.Where(f => f.Tierra).Sum(f => f.Quedan);
            SelectObject(hdc, fSub);
            SetTextColor(hdc, Rgb(148, 163, 184));
            var rS = new RECT { Left = 12, Top = 24, Right = ANCHO - 30, Bottom = 42 };
            DrawText(hdc, Textos.T("rast_tierra", Pct(tierras, tam)), -1, ref rS, DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
            SelectObject(hdc, fGlifo);
            SetTextColor(hdc, Rgb(148, 163, 184));
            var rF = new RECT { Left = ANCHO - 30, Top = 0, Right = ANCHO - 6, Bottom = ALTO_CABECERA };
            DrawText(hdc, plegado ? "" : "", -1, ref rF, DT_CENTER | DT_VCENTER | DT_SINGLELINE);

            if (!plegado)
            {
                IniciarGdiPlus();
                if (GdipCreateFromHDC(hdc, out var g) == 0 && g != IntPtr.Zero) GdipSetInterpolationMode(g, 7);
                var n = Math.Min(visibles, lista.Length);
                for (var i = 0; i < n; i++)
                {
                    var f = lista[i];
                    var y = ALTO_CABECERA + i * altoFila;
                    var apagada = f.Quedan <= 0;
                    var fondoFila = bajoRaton == i ? Rgb(30, 41, 64) : Rgb(17, 24, 39);
                    var rB = new RECT { Left = 6, Top = y + 1, Right = ANCHO - 6, Bottom = y + altoFila - 1 };
                    var pb = CreateSolidBrush(fondoFila); FillRect(hdc, ref rB, pb); DeleteObject(pb);
                    // La ilustración a la derecha, fundida; apagada si ya no queda.
                    var img = !apagada && f.Arte is not null && g != IntPtr.Zero ? Imagen(f.Arte) : IntPtr.Zero;
                    if (img != IntPtr.Zero)
                    {
                        GdipGetImageWidth(img, out var iw); GdipGetImageHeight(img, out var ih);
                        var hB = rB.Bottom - rB.Top; var xA = rB.Right - ANCHO_ARTE;
                        var sh = (int)Math.Min(ih, iw * (double)hB / ANCHO_ARTE);
                        GdipDrawImageRectRectI(g, img, xA, rB.Top, ANCHO_ARTE, hB, 0, (int)(ih - sh) / 2, (int)iw, sh, 2, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                        var p1 = new PuntoG { X = xA - 1, Y = 0 }; var p2 = new PuntoG { X = rB.Right, Y = 0 };
                        var (r1, g1, b1) = ((int)(fondoFila & 0xFF), (int)((fondoFila >> 8) & 0xFF), (int)((fondoFila >> 16) & 0xFF));
                        if (GdipCreateLineBrushI(ref p1, ref p2, (uint)((255 << 24) | (r1 << 16) | (g1 << 8) | b1), (uint)((70 << 24) | (r1 << 16) | (g1 << 8) | b1), 0, out var br) == 0)
                        {
                            GdipFillRectangleI(g, br, xA, rB.Top, ANCHO_ARTE, hB);
                            GdipDeleteBrush(br);
                        }
                    }
                    SelectObject(hdc, fFila);
                    // Cuántas quedan, en ámbar; gris si ninguna.
                    SetTextColor(hdc, apagada ? Rgb(71, 85, 105) : Rgb(252, 211, 77));
                    var rN = new RECT { Left = 8, Top = rB.Top, Right = 30, Bottom = rB.Bottom };
                    DrawText(hdc, $"{Math.Max(0, f.Quedan)}", -1, ref rN, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
                    // La probabilidad del siguiente robo, a la derecha.
                    SetTextColor(hdc, apagada ? Rgb(71, 85, 105) : Rgb(226, 232, 240));
                    var rP = new RECT { Left = rB.Right - 44, Top = rB.Top, Right = rB.Right - 6, Bottom = rB.Bottom };
                    DrawText(hdc, apagada ? "—" : Pct(f.Quedan, tam), -1, ref rP, DT_RIGHT | DT_VCENTER | DT_SINGLELINE);
                    SetTextColor(hdc, apagada ? Rgb(71, 85, 105) : bajoRaton == i ? Rgb(255, 255, 255) : Rgb(203, 213, 225));
                    var rNom = new RECT { Left = 34, Top = rB.Top, Right = rB.Right - 48, Bottom = rB.Bottom };
                    DrawText(hdc, f.Nombre, -1, ref rNom, DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
                }
                if (g != IntPtr.Zero) GdipDeleteGraphics(g);
            }
        }
        finally
        {
            BitBlt(pantalla, 0, 0, ANCHO, alto, hdc, 0, 0, 0x00CC0020);
            SelectObject(hdc, anterior);
            DeleteObject(lienzo); DeleteDC(hdc);
            EndPaint(hWnd, ref ps);
            DeleteObject(fCabeza); DeleteObject(fSub); DeleteObject(fFila); DeleteObject(fGlifo);
        }
    }
}
