using System.Runtime.InteropServices;

namespace MtgCornerArenaBridge;

/// <summary>
/// LAS CARTAS, GRANDES, ENCIMA DE ARENA.
///
/// La columna llevaba a la web («Similares a Plains»), pero eso es salir del
/// juego: hay que cambiar de ventana, y a media partida no se hace. Pedido por
/// el usuario el 2026-09-24: que las cartas parecidas —y desde la 1.13.0 los
/// combos— salgan EN Arena, grandes, y se puedan cerrar.
///
/// Es otra ventana propia, como la columna y como el aviso (ver Columna.cs):
/// Arena no admite complementos, así que lo que se pinta encima es una ventana
/// sin bordes, siempre visible y sin barra de tareas, colocada sobre la del
/// juego. PULSABLE pero SIN ROBAR EL FOCO —`WS_EX_NOACTIVATE` y
/// `WM_MOUSEACTIVATE` → `MA_NOACTIVATE`—: pulsar una carta no puede sacarte de
/// la partida.
///
/// EN SECCIONES. Similares es una sola sección sin rótulo: una rejilla. Combos
/// son varias: cada una con su rótulo (lo que produce el combo, pulsable, que
/// abre el combo en la web) y debajo sus piezas. La geometría se calcula una
/// vez por contenido (<see cref="Disponer"/>) en una lista de huecos, y pintar
/// y acertar con el ratón leen esa misma lista: así no hay dos aritméticas que
/// puedan discrepar.
///
/// LAS IMÁGENES SE DIBUJAN CON GDI+, que es lo que sabe abrir un JPEG; GDI a
/// secas sólo entiende mapas de bits. Se bajan a una carpeta en %TEMP% y se
/// quedan ahí: la misma carta señalada dos veces no se vuelve a bajar, y
/// borrarlas no rompe nada. Y SE ABREN UNA VEZ por panel: cada repintado
/// volvía a leerlas del disco y, con el doble búfer aún sin poner, se veía el
/// fondo un instante antes que las cartas («al hacer hover hay un parpadeo»,
/// el usuario, 2026-09-24). Ahora todo se pinta en memoria y se vuelca de golpe.
///
/// SÓLO SE ENSEÑA CON TODO BAJADO. Mientras se recuperan las cartas, quien avisa
/// es la fila de la columna (Columna.EnCurso), que no tapa nada: la gente está
/// jugando y no quiere la mesa tapada por un panel a medias.
/// </summary>
internal static class PanelCartas
{
    /// <summary>Una carta del panel: su imagen en disco y a dónde lleva en la web.</summary>
    public sealed record Carta(string Nombre, string Fichero, string? Ruta, double? PrecioUsd);

    /// <summary>Un grupo de cartas con rótulo opcional (pulsable si lleva ruta): un combo, o la rejilla de similares.</summary>
    public sealed record Seccion(string? Rotulo, string? Ruta, IReadOnlyList<Carta> Cartas);

    /// <summary>Un hueco ya colocado: qué carta va dónde. Sin carta = es el rótulo de la sección.</summary>
    private sealed record Hueco(int X, int Y, int Ancho, int Alto, Carta? Carta, Seccion Seccion);

    // ── Medidas ──────────────────────────────────────────────────────────
    private const int AIRE = 14, MARGEN = 18, ALTO_CABECERA = 44, ALTO_PIE = 22, ALTO_ROTULO = 26, AIRE_SECCION = 16;
    private const int POR_FILA = 4;
    private const int ANCHO_PANEL = MARGEN * 2 + POR_FILA * 170 + (POR_FILA - 1) * AIRE;   // 758, con cartas grandes

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
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int ancho, int alto);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr destino, int x, int y, int ancho, int alto, IntPtr origen, int x1, int y1, uint op);
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
    private const uint DT_LEFT = 0x0, DT_CENTER = 0x1, DT_VCENTER = 0x4, DT_SINGLELINE = 0x20, DT_END_ELLIPSIS = 0x8000;
    private const uint SRCCOPY = 0x00CC0020;
    private const int TRANSPARENT_BK = 1, SW_SHOWNOACTIVATE = 4, MA_NOACTIVATE = 3;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    private static uint Rgb(int r, int g, int b) => (uint)(r | (g << 8) | (b << 16));

    private static WndProc? procedimiento;
    private static IntPtr ventana;
    private static Thread? hilo;
    private static string titulo = "";
    private static IntPtr gdiplus;

    /// <summary>El contenido colocado: huecos con su carta o su rótulo, y el alto total.</summary>
    private static Hueco[] huecos = [];
    private static int alto = ALTO_CABECERA + MARGEN + ALTO_PIE + MARGEN;
    private static int anchoCarta = 170, altoCarta = 237;
    private static int bajoRaton = -1;   // índice en `huecos`; -2 = la X; -1 = nada
    private static int altoRegion;

    /// <summary>LAS IMÁGENES, ABIERTAS UNA VEZ por panel. Se liberan al cerrarlo.</summary>
    private static readonly Dictionary<string, IntPtr> imagenes = new();

    /// <summary>Modo de taller: se ve aunque Arena no esté delante.</summary>
    public static bool SiempreVisible { get; set; }

    /// <summary>Lo que se hace al pulsar una carta o un rótulo con ruta: abrirla. Lo pone quien monta el panel.</summary>
    public static Action<string>? AlAbrir { get; set; }

    /// <summary>
    /// Enseña el panel con estas secciones, ya con todas sus imágenes. Si ya
    /// había uno, se sustituye: dos paneles encima del juego no los quiere
    /// nadie. `compacto`: cartas más pequeñas, para varios combos con sus
    /// piezas, que con las grandes no cabrían en una pantalla de 1080.
    /// </summary>
    public static void Mostrar(string tituloPanel, IReadOnlyList<Seccion> secciones, bool compacto = false)
    {
        Cerrar();
        titulo = tituloPanel;
        (anchoCarta, altoCarta) = compacto ? (130, 181) : (170, 237);
        (huecos, alto) = Disponer(secciones);
        bajoRaton = -1;
        if (huecos.Length == 0) return;
        hilo = new Thread(Correr) { IsBackground = true, Name = "panel" };
        hilo.Start();
    }

    /// <summary>La rejilla de siempre: una sección sin rótulo.</summary>
    public static void Mostrar(string tituloPanel, IReadOnlyList<Carta> cartas) =>
        Mostrar(tituloPanel, [new Seccion(null, null, cartas)]);

    /// <summary>Lo quita. Se puede llamar desde cualquier hilo.</summary>
    public static void Cerrar()
    {
        var v = ventana;
        if (v != IntPtr.Zero) PostMessage(v, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        var h = hilo;
        if (h is not null && h != Thread.CurrentThread) h.Join(TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// LA GEOMETRÍA, UNA VEZ. Recorre las secciones de arriba abajo: rótulo si lo
    /// hay, y las cartas de cuatro en cuatro. Devuelve los huecos y el alto que
    /// hace falta para todo.
    /// </summary>
    private static (Hueco[] Huecos, int Alto) Disponer(IReadOnlyList<Seccion> secciones)
    {
        var lista = new List<Hueco>();
        var y = ALTO_CABECERA + MARGEN;
        foreach (var s in secciones)
        {
            if (s.Cartas.Count == 0) continue;
            if (s.Rotulo is not null)
            {
                lista.Add(new Hueco(MARGEN, y, ANCHO_PANEL - MARGEN * 2, ALTO_ROTULO, null, s));
                y += ALTO_ROTULO;
            }
            for (var i = 0; i < s.Cartas.Count; i++)
            {
                var x = MARGEN + (i % POR_FILA) * (anchoCarta + AIRE);
                var fila = i / POR_FILA;
                lista.Add(new Hueco(x, y + fila * (altoCarta + AIRE), anchoCarta, altoCarta, s.Cartas[i], s));
            }
            var filas = (s.Cartas.Count + POR_FILA - 1) / POR_FILA;
            y += filas * (altoCarta + AIRE) - AIRE + AIRE_SECCION;
        }
        return (lista.ToArray(), y - AIRE_SECCION + MARGEN + ALTO_PIE + MARGEN / 2);
    }

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
                x, y, ANCHO_PANEL, alto, IntPtr.Zero, IntPtr.Zero, instancia, IntPtr.Zero);
            if (ventana == IntPtr.Zero) return;

            altoRegion = alto;
            SetWindowRgn(ventana, CreateRoundRectRgn(0, 0, ANCHO_PANEL + 1, alto + 1, 16, 16), true);
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

    private static IntPtr Imagen(string fichero)
    {
        if (imagenes.TryGetValue(fichero, out var img)) return img;
        img = GdipLoadImageFromFile(fichero, out var cargada) == 0 ? cargada : IntPtr.Zero;
        imagenes[fichero] = img;   // también el fallo, para no reintentar en cada pintado
        return img;
    }

    private static void SoltarImagenes()
    {
        foreach (var img in imagenes.Values) if (img != IntPtr.Zero) GdipDisposeImage(img);
        imagenes.Clear();
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
        return (r.Left + (r.Right - r.Left - ANCHO_PANEL) / 2, r.Top + Math.Max(12, (r.Bottom - r.Top - alto) / 2), ver);
    }

    private static void Recolocar()
    {
        var (x, y, ver) = Donde();
        SetWindowPos(ventana, HWND_TOPMOST, x, y, ANCHO_PANEL, alto, SWP_NOACTIVATE | (ver ? SWP_SHOWWINDOW : SWP_HIDEWINDOW));
        if (altoRegion != alto)
        {
            altoRegion = alto;
            SetWindowRgn(ventana, CreateRoundRectRgn(0, 0, ANCHO_PANEL + 1, alto + 1, 16, 16), true);
        }
    }

    /// <summary>Qué hay bajo un punto de la ventana: un hueco (0..n), la X (-2), o nada (-1).</summary>
    private static int QueHayEn(IntPtr lParam)
    {
        var x = (short)(lParam.ToInt64() & 0xFFFF);
        var y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        if (y < ALTO_CABECERA) return x >= ANCHO_PANEL - 44 ? -2 : -1;
        var lista = huecos;
        for (var i = 0; i < lista.Length; i++)
        {
            var h = lista[i];
            if (x >= h.X && x < h.X + h.Ancho && y >= h.Y && y < h.Y + h.Alto) return i;
        }
        return -1;
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
                var lista = huecos;
                if (i >= 0 && i < lista.Length && AlAbrir is { } abrir)
                {
                    // Una carta abre su ficha; un rótulo abre su combo.
                    var ruta = lista[i].Carta?.Ruta ?? lista[i].Seccion.Ruta;
                    if (ruta is not null) _ = Task.Run(() => { try { abrir(ruta); } catch { /* lo cuenta quien lo montó */ } });
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
                SoltarImagenes();
                PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return DefWindowProc(hWnd, mensaje, wParam, lParam);
    }

    private static void Pintar(IntPtr hWnd)
    {
        var pantalla = BeginPaint(hWnd, out var ps);
        // DOBLE BÚFER: todo se pinta en un mapa de bits en memoria y se vuelca a
        // la ventana de una sola vez. Pintando directo, el fondo se veía un
        // instante antes que las cartas en cada repintado.
        var hdc = CreateCompatibleDC(pantalla);
        var lienzo = CreateCompatibleBitmap(pantalla, ANCHO_PANEL, alto);
        var lienzoAnterior = SelectObject(hdc, lienzo);
        var fondo = CreateSolidBrush(Rgb(9, 13, 24));
        var marco = CreateSolidBrush(Rgb(56, 189, 248));
        var resalte = CreateSolidBrush(Rgb(26, 34, 54));
        var fTitulo = CreateFont(19, 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        var fRotulo = CreateFont(15, 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        var fPie = CreateFont(15, 0, 0, 0, 500, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        var fCerrar = CreateFont(17, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe MDL2 Assets");
        var grafico = IntPtr.Zero;
        try
        {
            var todo = new RECT { Left = 0, Top = 0, Right = ANCHO_PANEL, Bottom = alto };
            FillRect(hdc, ref todo, fondo);
            SetBkMode(hdc, TRANSPARENT_BK);

            // Cabecera: de qué carta se parte y la X.
            SelectObject(hdc, fTitulo);
            SetTextColor(hdc, Rgb(252, 211, 77));
            var rTitulo = new RECT { Left = MARGEN, Top = 0, Right = ANCHO_PANEL - 50, Bottom = ALTO_CABECERA };
            DrawText(hdc, titulo, -1, ref rTitulo, DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);

            var rX = new RECT { Left = ANCHO_PANEL - 44, Top = 0, Right = ANCHO_PANEL, Bottom = ALTO_CABECERA };
            if (bajoRaton == -2) FillRect(hdc, ref rX, resalte);
            SelectObject(hdc, fCerrar);
            SetTextColor(hdc, bajoRaton == -2 ? Rgb(255, 255, 255) : Rgb(186, 196, 214));
            DrawText(hdc, "", -1, ref rX, DT_CENTER | DT_VCENTER | DT_SINGLELINE);

            GdipCreateFromHDC(hdc, out grafico);
            if (grafico != IntPtr.Zero) GdipSetInterpolationMode(grafico, 7);   // bicúbica de calidad

            var lista = huecos;
            for (var i = 0; i < lista.Length; i++)
            {
                var h = lista[i];
                var r = new RECT { Left = h.X, Top = h.Y, Right = h.X + h.Ancho, Bottom = h.Y + h.Alto };

                if (h.Carta is null)
                {
                    // El rótulo de la sección: lo que produce el combo. Pulsable si tiene ruta.
                    var pulsable = h.Seccion.Ruta is not null;
                    if (i == bajoRaton && pulsable) FillRect(hdc, ref r, resalte);
                    SelectObject(hdc, fRotulo);
                    SetTextColor(hdc, i == bajoRaton && pulsable ? Rgb(255, 255, 255) : Rgb(186, 196, 214));
                    var rTexto = new RECT { Left = r.Left + 4, Top = r.Top, Right = r.Right - 4, Bottom = r.Bottom };
                    DrawText(hdc, h.Seccion.Rotulo ?? "", -1, ref rTexto, DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
                    continue;
                }

                // Bajo el ratón: un filo azul alrededor, que se vea cuál se va a abrir.
                if (i == bajoRaton)
                {
                    var rf = new RECT { Left = r.Left - 3, Top = r.Top - 3, Right = r.Right + 3, Bottom = r.Bottom + 3 };
                    FillRect(hdc, ref rf, marco);
                }

                var imagen = grafico != IntPtr.Zero ? Imagen(h.Carta.Fichero) : IntPtr.Zero;
                if (imagen != IntPtr.Zero)
                {
                    GdipDrawImageRectI(grafico, imagen, h.X, h.Y, h.Ancho, h.Alto);
                }
                else
                {
                    // Sin imagen, el nombre: mejor un hueco con letra que un agujero.
                    FillRect(hdc, ref r, resalte);
                    SelectObject(hdc, fPie);
                    SetTextColor(hdc, Rgb(226, 232, 240));
                    var rn = new RECT { Left = r.Left + 6, Top = r.Top, Right = r.Right - 6, Bottom = r.Bottom };
                    DrawText(hdc, h.Carta.Nombre, -1, ref rn, DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
                }
            }

            // El pie: qué pasa al pulsar; con el ratón sobre una carta, su nombre y precio.
            SelectObject(hdc, fPie);
            SetTextColor(hdc, Rgb(148, 163, 184));
            var rPie = new RECT { Left = MARGEN, Top = alto - ALTO_PIE - MARGEN / 2, Right = ANCHO_PANEL - MARGEN, Bottom = alto };
            var sobre = bajoRaton >= 0 && bajoRaton < lista.Length ? lista[bajoRaton].Carta : null;
            var pie = sobre?.PrecioUsd is { } p ? $"{sobre.Nombre} · ${p:0.00}" : Textos.T("panel_pie");
            DrawText(hdc, pie, -1, ref rPie, DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
        }
        finally
        {
            if (grafico != IntPtr.Zero) GdipDeleteGraphics(grafico);
            BitBlt(pantalla, 0, 0, ANCHO_PANEL, alto, hdc, 0, 0, SRCCOPY);
            SelectObject(hdc, lienzoAnterior);
            DeleteObject(lienzo);
            DeleteDC(hdc);
            EndPaint(hWnd, ref ps);
            DeleteObject(fondo);
            DeleteObject(marco);
            DeleteObject(resalte);
            DeleteObject(fTitulo);
            DeleteObject(fRotulo);
            DeleteObject(fPie);
            DeleteObject(fCerrar);
        }
    }
}
