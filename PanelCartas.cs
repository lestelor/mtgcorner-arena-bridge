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
    /// <summary>El ancho del panel sale del de la carta: cuatro por fila y sus aires.</summary>
    private static int ANCHO_PANEL => MARGEN * 2 + POR_FILA * anchoCarta + (POR_FILA - 1) * AIRE;

    /// <summary>
    /// TAMAÑOS DE CARTA QUE SE PRUEBAN, de mayor a menor. «Que se puedan leer»
    /// (el usuario, 2026-09-25): a 170 px el texto de reglas no se lee; a 260
    /// sí. Pero tres combos con sus piezas a 260 no caben en 1080, así que se
    /// baja hasta que el panel entero entra en la ventana de Arena con aire.
    /// </summary>
    private static readonly int[] ANCHOS = [280, 260, 240, 220, 200, 180, 160, 140, 120];

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
    [DllImport("user32.dll")] private static extern IntPtr SetCursor(IntPtr cursor);
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr contexto);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint formato, IntPtr datos);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint banderas, UIntPtr bytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr h);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr h);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] private static extern IntPtr CreatePen(int estilo, int ancho, uint color);
    [DllImport("gdi32.dll")] private static extern bool MoveToEx(IntPtr hdc, int x, int y, IntPtr anterior);
    [DllImport("gdi32.dll")] private static extern bool LineTo(IntPtr hdc, int x, int y);
    [DllImport("gdi32.dll")] private static extern bool Ellipse(IntPtr hdc, int izq, int arriba, int der, int abajo);
    [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int objeto);
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
    [DllImport("gdiplus.dll")] private static extern int GdipGetImageWidth(IntPtr imagen, out uint ancho);
    [DllImport("gdiplus.dll")] private static extern int GdipGetImageHeight(IntPtr imagen, out uint alto);

    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x8000000;
    private const uint WM_DESTROY = 0x2, WM_CLOSE = 0x10, WM_PAINT = 0xF, WM_TIMER = 0x113;
    private const uint WM_MOUSEMOVE = 0x200, WM_LBUTTONUP = 0x202, WM_MOUSELEAVE = 0x2A3, WM_MOUSEACTIVATE = 0x21, WM_MOUSEWHEEL = 0x20A, WM_SETCURSOR = 0x20;
    private const int IDC_HAND = 32649, IDC_ARROW = 32512;
    private const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40, SWP_HIDEWINDOW = 0x80;
    private const uint LWA_ALPHA = 0x2, TME_LEAVE = 0x2;
    private const uint DT_LEFT = 0x0, DT_RIGHT = 0x2, DT_CENTER = 0x1, DT_VCENTER = 0x4, DT_WORDBREAK = 0x10, DT_SINGLELINE = 0x20, DT_CALCRECT = 0x400, DT_END_ELLIPSIS = 0x8000;
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

    /// <summary>
    /// UN PANEL SÓLO DE TEXTO: el resumen de la partida. Va por secciones —cada
    /// una con su título y sus párrafos; los de una lista llevan viñeta—, con
    /// letra mayor que la de los pies (pedido del usuario el 2026-09-26), rueda
    /// del ratón si no cabe, y un botón «Copiar» que deja el texto entero en el
    /// portapapeles. NO se selecciona con el ratón: para eso la ventana tendría
    /// que coger el foco del teclado, y eso se lo quita a Arena.
    /// </summary>
    public sealed record SeccionTexto(string Titulo, IReadOnlyList<string> Parrafos, bool Lista);

    /// <summary>Un enlace al final del texto: los mazos que pudo llevar el rival, que abren la web.</summary>
    /// <summary>Un mazo sugerido: el texto, y a la derecha el autor (avatar y nombre) y el logo de su origen (MTG Corner o Moxfield).</summary>
    public sealed record Enlace(string Texto, string Ruta, string? Logo = null, string? Autor = null, string? Avatar = null);
    private const int ANCHO_AUTOR = 150, LADO_AVATAR = 18;

    /// <summary>
    /// Algo que comprar tras la partida: miniatura, nombre y una etiqueta con
    /// el mejor precio y su tienda. El clic abre el detalle en la web (el
    /// comparador), nunca una tienda: decidido con el usuario el 2026-09-27.
    /// </summary>
    public sealed record Compra(string Nombre, string Fichero, string? Ruta, string Etiqueta, string? Logo = null);
    private const int LADO_LOGO = 16, ANCHO_VISTA = 210, ALTO_VISTA = 294;

    private sealed record Bloque(string Texto, bool Titulo, bool Vineta, int Alto, int Enlace = -1, bool Compras = false, string? Especial = null);

    /// <summary>
    /// LO QUE HACE DIVERTIDO EL RESUMEN (ideas aprobadas por el usuario el
    /// 2026-09-27): el titular con la nota, el entrenador con su tono y su
    /// avatar, la jugada que decidió la partida y el gráfico de vidas.
    /// </summary>
    public sealed record ResumenExtras(
        string? Titular, int? Nota,
        string? JugadaNombre, string? JugadaFichero, string? JugadaBando, int? JugadaTurno, string? JugadaFrase,
        int?[] VidaYo, int?[] VidaRival,
        string[] Tonos, string[] TonoEtiquetas, string?[] TonoAvatares, string TonoActual, Action<string>? CambiarTono,
        string RotuloJugada, string RotuloVidas, string RotuloTu, string RotuloRival, string RotuloTono);

    private static ResumenExtras? extras;
    private static RECT[] rectTonos = [];
    private const int BAJO_TONO = -200;   // el tono i está bajo el ratón: BAJO_TONO - i
    private const int ALTO_TONOS = 44, LADO_AVATAR_TONO = 34, ALTO_GRAFICO = 120, ALTO_JUGADA = 104;

    private static Compra[] compras = [];
    private static RECT[] rectCompras = [];
    private const int BAJO_COMPRA = -100;   // la compra i está bajo el ratón: BAJO_COMPRA - i
    private const int ANCHO_MINI_COMPRA = 60, ALTO_MINI_COMPRA = 84, ANCHO_FICHA_COMPRA = 236, AIRE_COMPRA = 12;

    private static Enlace[] enlaces = [];
    /// <summary>Dónde se pintó cada enlace en el último repintado (coordenadas de la ventana), para el ratón.</summary>
    private static RECT[] rectEnlaces = [];
    private const int BAJO_ENLACE = -10;   // el enlace i está bajo el ratón: BAJO_ENLACE - i

    private static Bloque[] bloques = [];
    private static string copia = "";
    private static int desplazamiento, altoTexto;
    private static bool copiado;
    private const int TAM_TITULO_SECCION = 21, TAM_TEXTO = 19, AIRE_PARRAFO = 8, AIRE_ANTES_TITULO = 18, SANGRIA_VINETA = 22;
    private const int ANCHO_BOTON = 112, ALTO_BOTON = 30;
    private const int BAJO_COPIAR = -3;

    /// <summary>Enseña el resumen por secciones. <paramref name="textoPlano"/> es lo que copia el botón.</summary>
    public static void MostrarTexto(string tituloPanel, IReadOnlyList<SeccionTexto> secciones, string textoPlano, IReadOnlyList<Enlace>? conEnlaces = null, string? tituloEnlaces = null, IReadOnlyList<Compra>? conCompras = null, string? tituloCompras = null, ResumenExtras? conExtras = null)
    {
        Cerrar();
        titulo = tituloPanel;
        copia = textoPlano;
        extras = conExtras;
        rectTonos = new RECT[conExtras?.Tonos.Length ?? 0];
        enlaces = conEnlaces?.ToArray() ?? [];
        rectEnlaces = new RECT[enlaces.Length];
        compras = (conCompras ?? []).Take(3).ToArray();
        rectCompras = new RECT[compras.Length];
        copiado = false;
        desplazamiento = 0;
        huecos = [];
        (anchoCarta, altoCarta) = (170, 237);   // fija el ancho del panel (758)
        var (_, altoArena) = MedidasDeArena();

        // Se mide cada bloque con su fuente de verdad, para que el alto sea el
        // que luego se pinta.
        var hdc = GetDC(IntPtr.Zero);
        var fTitulo = CreateFont(TAM_TITULO_SECCION, 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        var fTexto = CreateFont(TAM_TEXTO, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        var anterior = SelectObject(hdc, fTexto);
        var lista = new List<Bloque>();
        if (extras is { } ex)
        {
            // El titular (grande) con la nota a la derecha.
            if (ex.Titular is { Length: > 0 })
            {
                var fGrande = CreateFont(26, 0, 0, 0, 800, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
                SelectObject(hdc, fGrande);
                var rt = new RECT { Left = 0, Top = 0, Right = ANCHO_PANEL - MARGEN * 2 - 90, Bottom = 0 };
                DrawText(hdc, ex.Titular, -1, ref rt, DT_LEFT | DT_WORDBREAK | DT_CALCRECT);
                DeleteObject(fGrande);
                lista.Add(new Bloque(ex.Titular, false, false, Math.Max(40, rt.Bottom - rt.Top), Especial: "titular"));
            }
            // El entrenador: los tonos, con avatar si lo hay.
            lista.Add(new Bloque("", false, false, ALTO_TONOS, Especial: "tonos"));
            // La jugada de la partida.
            if (ex.JugadaNombre is { Length: > 0 }) lista.Add(new Bloque("", false, false, ALTO_JUGADA, Especial: "jugada"));
            // El gráfico de vidas, si hay al menos dos turnos con vida.
            if (ex.VidaYo.Count(v => v is not null) >= 2) lista.Add(new Bloque("", false, false, ALTO_GRAFICO, Especial: "vidas"));
        }
        foreach (var sec in secciones)
        {
            SelectObject(hdc, fTitulo);
            var rt = new RECT { Left = 0, Top = 0, Right = ANCHO_PANEL - MARGEN * 2, Bottom = 0 };
            DrawText(hdc, sec.Titulo, -1, ref rt, DT_LEFT | DT_WORDBREAK | DT_CALCRECT);
            lista.Add(new Bloque(sec.Titulo, true, false, rt.Bottom - rt.Top));
            SelectObject(hdc, fTexto);
            foreach (var p in sec.Parrafos)
            {
                var rp = new RECT { Left = 0, Top = 0, Right = ANCHO_PANEL - MARGEN * 2 - (sec.Lista ? SANGRIA_VINETA : 0), Bottom = 0 };
                DrawText(hdc, p, -1, ref rp, DT_LEFT | DT_WORDBREAK | DT_CALCRECT);
                lista.Add(new Bloque(p, false, sec.Lista, rp.Bottom - rp.Top));
            }
        }
        // Los enlaces, como una sección más: su título y una línea por mazo.
        if (enlaces.Length > 0)
        {
            SelectObject(hdc, fTitulo);
            var rt = new RECT { Left = 0, Top = 0, Right = ANCHO_PANEL - MARGEN * 2, Bottom = 0 };
            var rotulo = tituloEnlaces ?? "";
            DrawText(hdc, rotulo, -1, ref rt, DT_LEFT | DT_WORDBREAK | DT_CALCRECT);
            lista.Add(new Bloque(rotulo, true, false, rt.Bottom - rt.Top));
            SelectObject(hdc, fTexto);
            for (var i = 0; i < enlaces.Length; i++)
            {
                var reserva = (enlaces[i].Logo is not null ? LADO_LOGO + 10 : 0) + (enlaces[i].Autor is { Length: > 0 } ? ANCHO_AUTOR + LADO_AVATAR + 12 : 0) + 8;
                var re = new RECT { Left = 0, Top = 0, Right = ANCHO_PANEL - MARGEN * 2 - SANGRIA_VINETA - reserva, Bottom = 0 };
                DrawText(hdc, enlaces[i].Texto, -1, ref re, DT_LEFT | DT_WORDBREAK | DT_CALCRECT);
                lista.Add(new Bloque(enlaces[i].Texto, false, true, re.Bottom - re.Top, i));
            }
        }
        // Las compras: su título y una fila de fichas con miniatura.
        if (compras.Length > 0)
        {
            SelectObject(hdc, fTitulo);
            var rt = new RECT { Left = 0, Top = 0, Right = ANCHO_PANEL - MARGEN * 2, Bottom = 0 };
            var rotulo = tituloCompras ?? "";
            DrawText(hdc, rotulo, -1, ref rt, DT_LEFT | DT_WORDBREAK | DT_CALCRECT);
            lista.Add(new Bloque(rotulo, true, false, rt.Bottom - rt.Top));
            lista.Add(new Bloque("", false, false, ALTO_MINI_COMPRA + 6, -1, true));
        }
        SelectObject(hdc, anterior); DeleteObject(fTitulo); DeleteObject(fTexto); ReleaseDC(IntPtr.Zero, hdc);
        bloques = lista.ToArray();
        altoTexto = 0;
        for (var i = 0; i < bloques.Length; i++)
            altoTexto += bloques[i].Alto + (bloques[i].Titulo ? (i > 0 ? AIRE_ANTES_TITULO : 0) + AIRE_PARRAFO : AIRE_PARRAFO);
        alto = Math.Min(altoArena - 60, ALTO_CABECERA + MARGEN / 2 + altoTexto + MARGEN + ALTO_PIE + MARGEN / 2);
        texto = copia;
        bajoRaton = -1;
        hilo = new Thread(Correr) { IsBackground = true, Name = "panel" };
        hilo.Start();
    }

    private static RECT RectBotonCopiar() => new()
    {
        Left = ANCHO_PANEL - MARGEN - ANCHO_BOTON, Top = alto - ALTO_PIE - MARGEN / 2 - (ALTO_BOTON - ALTO_PIE) / 2,
        Right = ANCHO_PANEL - MARGEN, Bottom = alto - ALTO_PIE - MARGEN / 2 - (ALTO_BOTON - ALTO_PIE) / 2 + ALTO_BOTON,
    };

    /// <summary>
    /// Los bloques del resumen que no son texto: el titular con la nota, los
    /// tonos del entrenador, la jugada de la partida y el gráfico de vidas.
    /// Se pintan en la zona de texto (que ya va desplazada por la rueda).
    /// </summary>
    private static void PintarEspecial(IntPtr zona, string especial, ResumenExtras ex, int y, int alto, int arriba, IntPtr fTituloSec, IntPtr fTexto)
    {
        var anchoZona = ANCHO_PANEL - MARGEN * 2;
        switch (especial)
        {
            case "titular":
            {
                var fGrande = CreateFont(26, 0, 0, 0, 800, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
                SelectObject(zona, fGrande);
                SetTextColor(zona, Rgb(255, 255, 255));
                var rt = new RECT { Left = MARGEN, Top = y, Right = MARGEN + anchoZona - 90, Bottom = y + alto };
                DrawText(zona, ex.Titular ?? "", -1, ref rt, DT_LEFT | DT_WORDBREAK);
                DeleteObject(fGrande);
                if (ex.Nota is { } nota)
                {
                    // La nota, en un círculo: verde de 8 para arriba, ámbar de 5 a 7, rojo por debajo.
                    var color = nota >= 8 ? Rgb(34, 197, 94) : nota >= 5 ? Rgb(245, 158, 11) : Rgb(239, 68, 68);
                    var pincel = CreateSolidBrush(color);
                    var pincelAnterior = SelectObject(zona, pincel);
                    var plumaAnterior = SelectObject(zona, GetStockObject(8 /* NULL_PEN */));
                    var cx = MARGEN + anchoZona - 36; var cy = y + 20;
                    Ellipse(zona, cx - 24, cy - 24, cx + 24, cy + 24);
                    SelectObject(zona, plumaAnterior); SelectObject(zona, pincelAnterior); DeleteObject(pincel);
                    var fNota = CreateFont(22, 0, 0, 0, 800, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
                    SelectObject(zona, fNota);
                    SetTextColor(zona, Rgb(9, 13, 24));
                    var rn = new RECT { Left = cx - 24, Top = cy - 24, Right = cx + 24, Bottom = cy + 24 };
                    DrawText(zona, $"{nota}", -1, ref rn, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
                    DeleteObject(fNota);
                    SelectObject(zona, fTexto);
                    SetTextColor(zona, Rgb(148, 163, 184));
                    var r10 = new RECT { Left = cx - 30, Top = cy + 26, Right = cx + 30, Bottom = cy + 44 };
                    DrawText(zona, "/10", -1, ref r10, DT_CENTER | DT_SINGLELINE);
                }
                break;
            }
            case "tonos":
            {
                SelectObject(zona, fTexto);
                SetTextColor(zona, Rgb(148, 163, 184));
                var rr = new RECT { Left = MARGEN, Top = y, Right = MARGEN + 120, Bottom = y + ALTO_TONOS };
                DrawText(zona, ex.RotuloTono + ":", -1, ref rr, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
                var x = MARGEN + 110;
                IniciarGdiPlus();
                GdipCreateFromHDC(zona, out var g);
                if (g != IntPtr.Zero) GdipSetInterpolationMode(g, 7);
                for (var i = 0; i < ex.Tonos.Length; i++)
                {
                    var activo = ex.Tonos[i] == ex.TonoActual;
                    var sobre = bajoRaton == BAJO_TONO - i;
                    var etiqueta = ex.TonoEtiquetas[i];
                    var rm = new RECT();
                    DrawText(zona, etiqueta, -1, ref rm, DT_CALCRECT | DT_SINGLELINE);
                    var imgAvatar = ex.TonoAvatares[i] is not null ? Imagen(ex.TonoAvatares[i]!) : IntPtr.Zero;
                    var conAvatar = imgAvatar != IntPtr.Zero;
                    // El avatar a la altura del chip, con su proporción (son retratos verticales).
                    var anchoAvatar = LADO_AVATAR_TONO;
                    if (conAvatar && GdipGetImageWidth(imgAvatar, out var aw) == 0 && GdipGetImageHeight(imgAvatar, out var ah) == 0 && ah > 0)
                        anchoAvatar = Math.Max(14, (int)Math.Round(LADO_AVATAR_TONO * (double)aw / ah));
                    var anchoFicha = 16 + (conAvatar ? anchoAvatar + 6 : 0) + (rm.Right - rm.Left);
                    var rf = new RECT { Left = x, Top = y + 3, Right = x + anchoFicha, Bottom = y + ALTO_TONOS - 3 };
                    rectTonos[i] = new RECT { Left = rf.Left, Top = arriba + rf.Top, Right = rf.Right, Bottom = arriba + rf.Bottom };
                    var pincel = CreateSolidBrush(activo ? Rgb(56, 189, 248) : sobre ? Rgb(40, 52, 78) : Rgb(26, 34, 54));
                    FillRect(zona, ref rf, pincel);
                    DeleteObject(pincel);
                    var tx = x + 8;
                    if (conAvatar && g != IntPtr.Zero) { GdipDrawImageRectI(g, imgAvatar, tx, y + 5, anchoAvatar, LADO_AVATAR_TONO); tx += anchoAvatar + 6; }
                    SetTextColor(zona, activo ? Rgb(9, 13, 24) : Rgb(226, 232, 240));
                    var rt = new RECT { Left = tx, Top = rf.Top, Right = rf.Right - 8, Bottom = rf.Bottom };
                    DrawText(zona, etiqueta, -1, ref rt, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
                    x += anchoFicha + 8;
                }
                if (g != IntPtr.Zero) GdipDeleteGraphics(g);
                break;
            }
            case "jugada":
            {
                var rival = ex.JugadaBando == "rival";
                SelectObject(zona, fTituloSec);
                SetTextColor(zona, rival ? Rgb(248, 113, 113) : Rgb(252, 211, 77));
                var rt = new RECT { Left = MARGEN, Top = y, Right = MARGEN + anchoZona, Bottom = y + 26 };
                DrawText(zona, ex.RotuloJugada + (ex.JugadaTurno is { } t ? $" · T{t}" : ""), -1, ref rt, DT_LEFT | DT_SINGLELINE);
                var img = ex.JugadaFichero is not null ? Imagen(ex.JugadaFichero) : IntPtr.Zero;
                var tx = MARGEN;
                if (img != IntPtr.Zero)
                {
                    IniciarGdiPlus();
                    if (GdipCreateFromHDC(zona, out var g) == 0 && g != IntPtr.Zero)
                    {
                        GdipSetInterpolationMode(g, 7);
                        GdipDrawImageRectI(g, img, MARGEN, y + 28, 52, 72);
                        GdipDeleteGraphics(g);
                    }
                    tx += 60;
                }
                SelectObject(zona, fTexto);
                SetTextColor(zona, Rgb(255, 255, 255));
                var rn = new RECT { Left = tx, Top = y + 28, Right = MARGEN + anchoZona, Bottom = y + 50 };
                DrawText(zona, ex.JugadaNombre ?? "", -1, ref rn, DT_LEFT | DT_SINGLELINE | DT_END_ELLIPSIS);
                SetTextColor(zona, Rgb(226, 232, 240));
                var rf = new RECT { Left = tx, Top = y + 50, Right = MARGEN + anchoZona, Bottom = y + ALTO_JUGADA };
                DrawText(zona, ex.JugadaFrase ?? "", -1, ref rf, DT_LEFT | DT_WORDBREAK | DT_END_ELLIPSIS);
                break;
            }
            case "vidas":
            {
                SelectObject(zona, fTituloSec);
                SetTextColor(zona, Rgb(252, 211, 77));
                var rt = new RECT { Left = MARGEN, Top = y, Right = MARGEN + anchoZona, Bottom = y + 24 };
                DrawText(zona, ex.RotuloVidas, -1, ref rt, DT_LEFT | DT_SINGLELINE);
                // El área del gráfico.
                var gx = MARGEN + 30; var gy = y + 30; var gw = anchoZona - 30 - 90; var gh = ALTO_GRAFICO - 40;
                var n = Math.Max(ex.VidaYo.Length, ex.VidaRival.Length);
                var maximo = Math.Max(20, Math.Max(ex.VidaYo.Max(v => v ?? 0), ex.VidaRival.Max(v => v ?? 0)));
                var eje = CreateSolidBrush(Rgb(51, 65, 85));
                var rEjeX = new RECT { Left = gx, Top = gy + gh, Right = gx + gw, Bottom = gy + gh + 1 };
                var rEjeY = new RECT { Left = gx, Top = gy, Right = gx + 1, Bottom = gy + gh };
                FillRect(zona, ref rEjeX, eje); FillRect(zona, ref rEjeY, eje);
                DeleteObject(eje);
                SelectObject(zona, fPieChico());
                SetTextColor(zona, Rgb(100, 116, 139));
                var r0 = new RECT { Left = gx - 30, Top = gy + gh - 8, Right = gx - 4, Bottom = gy + gh + 8 };
                DrawText(zona, "0", -1, ref r0, DT_RIGHT | DT_VCENTER | DT_SINGLELINE);
                var rM = new RECT { Left = gx - 30, Top = gy - 8, Right = gx - 4, Bottom = gy + 8 };
                DrawText(zona, $"{maximo}", -1, ref rM, DT_RIGHT | DT_VCENTER | DT_SINGLELINE);
                int X(int i) => n <= 1 ? gx : gx + (int)((long)i * gw / (n - 1));
                int Y(int v) => gy + gh - (int)((long)Math.Clamp(v, 0, maximo) * gh / maximo);
                // La línea del turno clave.
                if (ex.JugadaTurno is { } tc && tc >= 1 && tc <= n)
                {
                    var marca = CreateSolidBrush(Rgb(245, 158, 11));
                    var rk = new RECT { Left = X(tc - 1), Top = gy, Right = X(tc - 1) + 2, Bottom = gy + gh };
                    FillRect(zona, ref rk, marca); DeleteObject(marca);
                }
                foreach (var (serie, color) in new[] { (ex.VidaYo, Rgb(56, 189, 248)), (ex.VidaRival, Rgb(248, 113, 113)) })
                {
                    var pluma = CreatePen(0, 2, color);
                    var plumaAnterior = SelectObject(zona, pluma);
                    var hayPrevio = false; var prevV = 0;
                    for (var i = 0; i < n; i++)
                    {
                        var v = i < serie.Length ? serie[i] : null;
                        if (v is null) { if (hayPrevio) v = prevV; else continue; }
                        if (!hayPrevio) { MoveToEx(zona, X(i), Y(v.Value), IntPtr.Zero); hayPrevio = true; }
                        else LineTo(zona, X(i), Y(v.Value));
                        prevV = v.Value;
                    }
                    SelectObject(zona, plumaAnterior); DeleteObject(pluma);
                }
                // La leyenda, a la derecha.
                var lx = gx + gw + 12;
                var azul = CreateSolidBrush(Rgb(56, 189, 248)); var rojo = CreateSolidBrush(Rgb(248, 113, 113));
                var r1 = new RECT { Left = lx, Top = gy + 4, Right = lx + 12, Bottom = gy + 8 }; FillRect(zona, ref r1, azul);
                var r2 = new RECT { Left = lx, Top = gy + 26, Right = lx + 12, Bottom = gy + 30 }; FillRect(zona, ref r2, rojo);
                DeleteObject(azul); DeleteObject(rojo);
                SetTextColor(zona, Rgb(203, 213, 225));
                var rl1 = new RECT { Left = lx + 16, Top = gy - 2, Right = lx + 80, Bottom = gy + 14 }; DrawText(zona, ex.RotuloTu, -1, ref rl1, DT_LEFT | DT_SINGLELINE);
                var rl2 = new RECT { Left = lx + 16, Top = gy + 20, Right = lx + 80, Bottom = gy + 36 }; DrawText(zona, ex.RotuloRival, -1, ref rl2, DT_LEFT | DT_SINGLELINE);
                var ultimoYo = ex.VidaYo.LastOrDefault(v => v is not null); var ultimoRival = ex.VidaRival.LastOrDefault(v => v is not null);
                var rl3 = new RECT { Left = lx, Top = gy + 44, Right = lx + 90, Bottom = gy + 60 };
                DrawText(zona, $"{ultimoYo?.ToString() ?? "?"} · {ultimoRival?.ToString() ?? "?"}", -1, ref rl3, DT_LEFT | DT_SINGLELINE);
                break;
            }
        }
    }

    private static IntPtr fuentePieChico;
    private static IntPtr fPieChico()
    {
        if (fuentePieChico == IntPtr.Zero) fuentePieChico = CreateFont(13, 0, 0, 0, 500, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        return fuentePieChico;
    }

    /// <summary>Lo que se ve del texto: entre la cabecera y el pie.</summary>
    private static int AltoVisibleTexto => alto - ALTO_CABECERA - MARGEN / 2 - ALTO_PIE - MARGEN;

    /// <summary>Deja el texto en el portapapeles, como texto Unicode. Sin dueño: la ventana no coge el foco.</summary>
    private static bool Copiar(string t)
    {
        if (!OpenClipboard(IntPtr.Zero)) return false;
        try
        {
            EmptyClipboard();
            var bytes = (t.Length + 1) * 2;
            var h = GlobalAlloc(0x2 /* GMEM_MOVEABLE */, (UIntPtr)bytes);
            if (h == IntPtr.Zero) return false;
            var p = GlobalLock(h);
            if (p == IntPtr.Zero) return false;
            var datos = System.Text.Encoding.Unicode.GetBytes(t + "\0");
            Marshal.Copy(datos, 0, p, datos.Length);
            GlobalUnlock(h);
            return SetClipboardData(13 /* CF_UNICODETEXT */, h) != IntPtr.Zero;
        }
        catch { return false; }
        finally { CloseClipboard(); }
    }

    /// <summary>Un panel de sólo texto (sin secciones): un bloque de párrafos.</summary>
    private static string? texto;

    /// <summary>LAS IMÁGENES, ABIERTAS UNA VEZ por panel. Se liberan al cerrarlo.</summary>
    private static readonly Dictionary<string, IntPtr> imagenes = new();

    /// <summary>Modo de taller: se ve aunque Arena no esté delante.</summary>
    public static bool SiempreVisible { get; set; }

    /// <summary>Lo que se hace al pulsar una carta o un rótulo con ruta: abrirla. Lo pone quien monta el panel.</summary>
    public static Action<string>? AlAbrir { get; set; }

    /// <summary>
    /// Enseña el panel con estas secciones, ya con todas sus imágenes. Si ya
    /// había uno, se sustituye: dos paneles encima del juego no los quiere
    /// nadie. Las cartas, TAN GRANDES COMO QUEPAN: se prueba de mayor a menor
    /// hasta que el panel entero entra en la ventana de Arena.
    /// </summary>
    public static void Mostrar(string tituloPanel, IReadOnlyList<Seccion> secciones)
    {
        Cerrar();
        titulo = tituloPanel;
        texto = null;
        var (anchoArena, altoArena) = MedidasDeArena();
        foreach (var ancho in ANCHOS)
        {
            (anchoCarta, altoCarta) = (ancho, ancho * 680 / 488);   // proporción de una carta
            (huecos, alto) = Disponer(secciones);
            if (alto <= altoArena - 60 && ANCHO_PANEL <= anchoArena - 40) break;
        }
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

    /// <summary>Lo que mide la ventana de Arena, o una pantalla de 1080 si no está.</summary>
    private static (int Ancho, int Alto) MedidasDeArena()
    {
        var arena = Columna.VentanaDeArena();
        if (arena != IntPtr.Zero && GetWindowRect(arena, out var r) && r.Right > r.Left && r.Bottom > r.Top)
            return (r.Right - r.Left, r.Bottom - r.Top);
        return (1920, 1080);
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
        if (texto is not null)
        {
            var xr = (short)(lParam.ToInt64() & 0xFFFF);
            var yr = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
            var rb = RectBotonCopiar();
            if (xr >= rb.Left && xr < rb.Right && yr >= rb.Top && yr < rb.Bottom) return BAJO_COPIAR;
            var rects = rectEnlaces;
            for (var i = 0; i < rects.Length; i++)
                if (xr >= rects[i].Left && xr < rects[i].Right && yr >= rects[i].Top && yr < rects[i].Bottom) return BAJO_ENLACE - i;
            var rc = rectCompras;
            for (var i = 0; i < rc.Length; i++)
                if (xr >= rc[i].Left && xr < rc[i].Right && yr >= rc[i].Top && yr < rc[i].Bottom) return BAJO_COMPRA - i;
            var rtn = rectTonos;
            for (var i = 0; i < rtn.Length; i++)
                if (xr >= rtn[i].Left && xr < rtn[i].Right && yr >= rtn[i].Top && yr < rtn[i].Bottom) return BAJO_TONO - i;
        }
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

            // La mano sobre lo que se puede pulsar (cartas con ficha, rótulos con
            // enlace, la X, «Copiar», los mazos y las compras); la flecha en el resto.
            case WM_SETCURSOR:
            {
                var i = bajoRaton;
                var lista = huecos;
                var pulsable = i == -2 || i == BAJO_COPIAR || i <= BAJO_ENLACE || i <= BAJO_TONO
                    || (i >= 0 && i < lista.Length && (lista[i].Carta?.Ruta ?? lista[i].Seccion.Ruta) is not null);
                SetCursor(LoadCursor(IntPtr.Zero, pulsable ? IDC_HAND : IDC_ARROW));
                return new IntPtr(1);
            }

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

            case WM_MOUSEWHEEL:
            {
                if (texto is null) return IntPtr.Zero;
                var giro = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
                var tope = Math.Max(0, altoTexto - AltoVisibleTexto);
                desplazamiento = Math.Clamp(desplazamiento - giro / 120 * 48, 0, tope);
                InvalidateRect(hWnd, IntPtr.Zero, false);
                return IntPtr.Zero;
            }

            case WM_LBUTTONUP:
            {
                var i = QueHayEn(lParam);
                if (i == -2) { PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); return IntPtr.Zero; }
                if (i == BAJO_COPIAR) { copiado = Copiar(copia); InvalidateRect(hWnd, IntPtr.Zero, false); return IntPtr.Zero; }
                if (i <= BAJO_TONO && extras is { } exT && BAJO_TONO - i < exT.Tonos.Length)
                {
                    var tono = exT.Tonos[BAJO_TONO - i];
                    if (tono != exT.TonoActual && exT.CambiarTono is { } cambiar) _ = Task.Run(() => { try { cambiar(tono); } catch { /* lo cuenta quien lo montó */ } });
                    return IntPtr.Zero;
                }
                if (i <= BAJO_COMPRA && BAJO_COMPRA - i < compras.Length && AlAbrir is { } abrirCompra)
                {
                    var rutaCompra = compras[BAJO_COMPRA - i].Ruta;
                    if (rutaCompra is not null) _ = Task.Run(() => { try { abrirCompra(rutaCompra); } catch { /* lo cuenta quien lo montó */ } });
                    return IntPtr.Zero;
                }
                if (i <= BAJO_ENLACE && BAJO_ENLACE - i < enlaces.Length && AlAbrir is { } abrirEnlace)
                {
                    var rutaEnlace = enlaces[BAJO_ENLACE - i].Ruta;
                    _ = Task.Run(() => { try { abrirEnlace(rutaEnlace); } catch { /* lo cuenta quien lo montó */ } });
                    return IntPtr.Zero;
                }
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

            if (texto is not null)
            {
                // Las secciones: título en ámbar, párrafos en claro, viñetas
                // sangradas. Lo que se sale de la zona de texto se pinta en un
                // lienzo aparte y sólo se vuelca la ventana, así la rueda no
                // pisa la cabecera ni el pie.
                var fTituloSec = CreateFont(TAM_TITULO_SECCION, 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
                var fTexto = CreateFont(TAM_TEXTO, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
                var arriba = ALTO_CABECERA + MARGEN / 2;
                var altoZona = AltoVisibleTexto;
                var zona = CreateCompatibleDC(hdc);
                var lienzoZona = CreateCompatibleBitmap(hdc, ANCHO_PANEL, Math.Max(1, altoZona));
                var zonaAnterior = SelectObject(zona, lienzoZona);
                var rz = new RECT { Left = 0, Top = 0, Right = ANCHO_PANEL, Bottom = altoZona };
                FillRect(zona, ref rz, fondo);
                SetBkMode(zona, TRANSPARENT_BK);
                var y = -desplazamiento;
                for (var i = 0; i < bloques.Length; i++)
                {
                    var b = bloques[i];
                    if (b.Titulo && i > 0) y += AIRE_ANTES_TITULO;
                    if (b.Especial is { } especial && extras is { } ex)
                    {
                        if (y + b.Alto >= 0 && y < altoZona) PintarEspecial(zona, especial, ex, y, b.Alto, arriba, fTituloSec, fTexto);
                        else if (especial == "tonos") for (var k = 0; k < rectTonos.Length; k++) rectTonos[k] = default;
                        y += b.Alto + AIRE_PARRAFO;
                        continue;
                    }
                    if (b.Compras)
                    {
                        // Cada compra: miniatura a la izquierda y dos líneas al lado
                        // (nombre, y precio con tienda). Se apunta su rectángulo en la
                        // ventana para el ratón, y se pinta si asoma en la zona.
                        IniciarGdiPlus();
                        if (GdipCreateFromHDC(zona, out var gz) == 0 && gz != IntPtr.Zero)
                        {
                            GdipSetInterpolationMode(gz, 7);
                            for (var c = 0; c < compras.Length; c++)
                            {
                                var x = MARGEN + c * (ANCHO_FICHA_COMPRA + AIRE_COMPRA);
                                rectCompras[c] = new RECT { Left = x, Top = arriba + y, Right = x + ANCHO_FICHA_COMPRA, Bottom = arriba + y + ALTO_MINI_COMPRA };
                                if (y + b.Alto < 0 || y >= altoZona) continue;
                                var sobre = bajoRaton == BAJO_COMPRA - c;
                                var img = Imagen(compras[c].Fichero);
                                if (img != IntPtr.Zero) GdipDrawImageRectI(gz, img, x, y, ANCHO_MINI_COMPRA, ALTO_MINI_COMPRA);
                                SelectObject(zona, fTexto);
                                SetTextColor(zona, sobre ? Rgb(255, 255, 255) : Rgb(56, 189, 248));
                                var rn = new RECT { Left = x + ANCHO_MINI_COMPRA + 8, Top = y + 4, Right = x + ANCHO_FICHA_COMPRA, Bottom = y + 4 + 44 };
                                DrawText(zona, compras[c].Nombre, -1, ref rn, DT_LEFT | DT_WORDBREAK | DT_END_ELLIPSIS);
                                SetTextColor(zona, Rgb(252, 211, 77));
                                var conLogo = compras[c].Logo is not null && Imagen(compras[c].Logo!) != IntPtr.Zero;
                                if (conLogo) GdipDrawImageRectI(gz, Imagen(compras[c].Logo!), x + ANCHO_MINI_COMPRA + 8, y + 56, LADO_LOGO, LADO_LOGO);
                                var rp = new RECT { Left = x + ANCHO_MINI_COMPRA + 8 + (conLogo ? LADO_LOGO + 5 : 0), Top = y + 54, Right = x + ANCHO_FICHA_COMPRA, Bottom = y + ALTO_MINI_COMPRA };
                                DrawText(zona, compras[c].Etiqueta, -1, ref rp, DT_LEFT | DT_SINGLELINE | DT_END_ELLIPSIS);
                            }
                            GdipDeleteGraphics(gz);
                        }
                        y += b.Alto + AIRE_PARRAFO;
                        continue;
                    }
                    if (b.Enlace >= 0 && b.Enlace < rectEnlaces.Length)
                    {
                        // Dónde queda en la ventana (la zona empieza en `arriba`), para el ratón.
                        rectEnlaces[b.Enlace] = new RECT { Left = MARGEN + SANGRIA_VINETA, Top = arriba + y, Right = ANCHO_PANEL - MARGEN, Bottom = arriba + y + b.Alto };
                    }
                    if (y + b.Alto >= 0 && y < altoZona)
                    {
                        SelectObject(zona, b.Titulo ? fTituloSec : fTexto);
                        var sobreEnlace = b.Enlace >= 0 && bajoRaton == BAJO_ENLACE - b.Enlace;
                        SetTextColor(zona, b.Titulo ? Rgb(252, 211, 77) : b.Enlace >= 0 ? (sobreEnlace ? Rgb(255, 255, 255) : Rgb(56, 189, 248)) : Rgb(226, 232, 240));
                        if (b.Vineta)
                        {
                            var rv = new RECT { Left = MARGEN, Top = y, Right = MARGEN + SANGRIA_VINETA, Bottom = y + b.Alto };
                            DrawText(zona, b.Enlace >= 0 ? "›" : "•", -1, ref rv, DT_LEFT);
                        }
                        var derecha = ANCHO_PANEL - MARGEN;
                        if (b.Enlace >= 0 && b.Enlace < enlaces.Length)
                        {
                            // A la derecha: el logo del origen y, si lo hay, el autor con su avatar.
                            var e = enlaces[b.Enlace];
                            var imgLogoDer = e.Logo is not null ? Imagen(e.Logo) : IntPtr.Zero;
                            var imgAvatar = e.Avatar is not null ? Imagen(e.Avatar) : IntPtr.Zero;
                            IniciarGdiPlus();
                            if (GdipCreateFromHDC(zona, out var gd) == 0 && gd != IntPtr.Zero)
                            {
                                GdipSetInterpolationMode(gd, 7);
                                if (imgLogoDer != IntPtr.Zero) { derecha -= LADO_LOGO; GdipDrawImageRectI(gd, imgLogoDer, derecha, y + 3, LADO_LOGO, LADO_LOGO); derecha -= 10; }
                                if (e.Autor is { Length: > 0 })
                                {
                                    var fAutor = CreateFont(15, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
                                    var anteriorAutor = SelectObject(zona, fAutor);
                                    SetTextColor(zona, Rgb(148, 163, 184));
                                    var rMed = new RECT();
                                    DrawText(zona, e.Autor, -1, ref rMed, DT_CALCRECT | DT_SINGLELINE);
                                    var anchoAutor = Math.Min(ANCHO_AUTOR, rMed.Right - rMed.Left);
                                    var ra = new RECT { Left = derecha - anchoAutor, Top = y + 2, Right = derecha, Bottom = y + 2 + LADO_AVATAR };
                                    DrawText(zona, e.Autor, -1, ref ra, DT_RIGHT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
                                    derecha -= anchoAutor + 6;
                                    if (imgAvatar != IntPtr.Zero) { derecha -= LADO_AVATAR; GdipDrawImageRectI(gd, imgAvatar, derecha, y + 2, LADO_AVATAR, LADO_AVATAR); derecha -= 6; }
                                    SelectObject(zona, anteriorAutor); DeleteObject(fAutor);
                                    SelectObject(zona, fTexto);
                                    SetTextColor(zona, sobreEnlace ? Rgb(255, 255, 255) : Rgb(56, 189, 248));
                                }
                                GdipDeleteGraphics(gd);
                            }
                            derecha -= 8;
                        }
                        var rb = new RECT { Left = MARGEN + (b.Vineta ? SANGRIA_VINETA : 0), Top = y, Right = derecha, Bottom = y + b.Alto };
                        DrawText(zona, b.Texto, -1, ref rb, DT_LEFT | DT_WORDBREAK | DT_END_ELLIPSIS);
                    }
                    y += b.Alto + AIRE_PARRAFO;
                }
                BitBlt(hdc, 0, arriba, ANCHO_PANEL, altoZona, zona, 0, 0, SRCCOPY);
                SelectObject(zona, zonaAnterior); DeleteObject(lienzoZona); DeleteDC(zona);
                DeleteObject(fTituloSec); DeleteObject(fTexto);

                // LA CARTA GRANDE al pasar el ratón por una compra: encima de
                // todo, al lado de su ficha (a la derecha si cabe, si no a la
                // izquierda), sin salirse del panel.
                if (bajoRaton <= BAJO_COMPRA && BAJO_COMPRA - bajoRaton < compras.Length && BAJO_COMPRA - bajoRaton < rectCompras.Length)
                {
                    var c = BAJO_COMPRA - bajoRaton;
                    var img = Imagen(compras[c].Fichero);
                    if (img != IntPtr.Zero && grafico != IntPtr.Zero)
                    {
                        var rc = rectCompras[c];
                        var vx = rc.Right + 8 + ANCHO_VISTA <= ANCHO_PANEL - MARGEN ? rc.Right + 8 : Math.Max(MARGEN, rc.Left - 8 - ANCHO_VISTA);
                        var vy = Math.Clamp(rc.Top + (rc.Bottom - rc.Top) / 2 - ALTO_VISTA / 2, ALTO_CABECERA, Math.Max(ALTO_CABECERA, alto - ALTO_PIE - MARGEN - ALTO_VISTA));
                        GdipDrawImageRectI(grafico, img, vx, vy, ANCHO_VISTA, ALTO_VISTA);
                    }
                }

                // El botón «Copiar» / «Copiado» en el pie, a la derecha.
                var rBoton = RectBotonCopiar();
                var pincelBoton = CreateSolidBrush(bajoRaton == BAJO_COPIAR ? Rgb(56, 189, 248) : Rgb(26, 34, 54));
                FillRect(hdc, ref rBoton, pincelBoton);
                DeleteObject(pincelBoton);
                SelectObject(hdc, fPie);
                SetTextColor(hdc, bajoRaton == BAJO_COPIAR ? Rgb(9, 13, 24) : Rgb(226, 232, 240));
                DrawText(hdc, Textos.T(copiado ? "panel_copiado" : "panel_copiar"), -1, ref rBoton, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
                // Y a la izquierda, si hace falta, que se puede bajar con la rueda.
                if (altoTexto > altoZona)
                {
                    SetTextColor(hdc, Rgb(148, 163, 184));
                    var rAviso = new RECT { Left = MARGEN, Top = alto - ALTO_PIE - MARGEN / 2, Right = rBoton.Left - AIRE, Bottom = alto };
                    DrawText(hdc, Textos.T("panel_rueda"), -1, ref rAviso, DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
                }
            }

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
            if (texto is null)
            {
                var sobre = bajoRaton >= 0 && bajoRaton < lista.Length ? lista[bajoRaton].Carta : null;
                var pie = sobre?.PrecioUsd is { } p ? $"{sobre.Nombre} · ${p:0.00}" : Textos.T("panel_pie");
                DrawText(hdc, pie, -1, ref rPie, DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
            }
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
