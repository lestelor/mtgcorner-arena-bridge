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
internal static partial class PanelCartas
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
    private static int ANCHO_PANEL => MARGEN * 2 + POR_FILA * anchoCarta + (POR_FILA - 1) * AIRE
        // Similares/combos/sinergias: la carta a la izquierda y su raya (ver PanelRelacionadas).
        + (rel is null ? 0 : anchoCarta + AIRE * 2);

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
    private static int altoRegion, anchoRegion;

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
    public sealed record Enlace(string Texto, string Ruta, string? Logo = null, string? Autor = null, string? Avatar = null, string? Fecha = null,
        string? Origen = null, string? Coinciden = null, int Nivel = 0, string? Cartas = null, string? RotuloCartas = null);
    private const int ANCHO_AUTOR = 150, LADO_AVATAR = 18;

    /// <summary>
    /// Algo que comprar tras la partida: miniatura, nombre y una etiqueta con
    /// el mejor precio y su tienda. El clic abre el detalle en la web (el
    /// comparador), nunca una tienda: decidido con el usuario el 2026-09-27.
    /// </summary>
    public sealed record Compra(string Nombre, string Fichero, string? Ruta, string Etiqueta, string? Logo = null);
    private const int LADO_LOGO = 16, ANCHO_VISTA = 210, ALTO_VISTA = 294;

    /// <summary>Un bloque del panel de texto. <paramref name="Col"/>: 0 a lo ancho, 1 columna izquierda, 2 derecha.</summary>
    private sealed record Bloque(string Texto, bool Titulo, bool Vineta, int Alto, int Enlace = -1, bool Compras = false, string? Especial = null, int Col = 0);
    /// <summary>Dónde va cada bloque (calculado en MostrarTexto): arriba, izquierda y ancho.</summary>
    private static int[] topBloques = [], xBloques = [], anchoBloques = [];
    private const int HUECO_COLUMNAS = 28;
    /// <summary>Los avatares de los mazos sugeridos y los turnos del gráfico, para el ratón (zoom y notas).</summary>
    private static RECT[] rectAvatares = [];
    private static RECT[] rectTurnos = [];
    private const int BAJO_TURNO = -300, BAJO_AVATAR = -400;
    /// <summary>La carta de la jugada y la leyenda de la curva, en la ventana, para el ratón (zoom y explicación).</summary>
    private static RECT rectJugada, rectLeyenda;
    private const int BAJO_JUGADA = -500, BAJO_LEYENDA = -600;
    /// <summary>La fila de arriba de cada mazo sugerido: logo y origen, autor y fecha, y «N en común».</summary>
    private const int CABEZA_MAZO = 26;
    /// <summary>La etiqueta «N en común» de cada mazo, para el ratón (sus cartas al pasar).</summary>
    private static RECT[] rectCoinciden = [];
    private const int BAJO_COINCIDEN = -700;   // la etiqueta del mazo i: BAJO_COINCIDEN - i

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
        string RotuloJugada, string RotuloVidas, string RotuloTu, string RotuloRival, string RotuloTono,
        IReadOnlyDictionary<int, string>? NotasPorTurno = null,
        IReadOnlyDictionary<int, string>? AciertosPorTurno = null,
        string? RotuloVidasFinal = null);

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
    private const int AIRE_PARRAFO = 8, AIRE_ANTES_TITULO = 18, SANGRIA_VINETA = 22;
    /// <summary>Tamaños de letra del panel de texto: bajan un punto si el resumen no cabe sin scroll.</summary>
    private static int TAM_TITULO_SECCION = 21, TAM_TEXTO = 19;
    private const int ANCHO_BOTON = 112, ALTO_BOTON = 30;
    private const int BAJO_COPIAR = -3;

    /// <summary>Enseña el resumen por secciones. <paramref name="textoPlano"/> es lo que copia el botón.</summary>
    public static void MostrarTexto(string tituloPanel, IReadOnlyList<SeccionTexto> secciones, string textoPlano, IReadOnlyList<Enlace>? conEnlaces = null, string? tituloEnlaces = null, IReadOnlyList<Compra>? conCompras = null, string? tituloCompras = null, ResumenExtras? conExtras = null, Estadisticas? conEstadisticas = null, Importacion? conImportacion = null)
    {
        Cerrar();
        rel = null;
        titulo = tituloPanel;
        copia = textoPlano;
        extras = conExtras;
        est = conEstadisticas;
        PrepararImportacion(conImportacion);
        rectPuntosEst = []; centroPuntosEst = []; rectFichasEst = []; rectPeldanosEst = []; rectSegmentosEst = []; puntosEst = [];
        mazoEst = -1;
        rectTonos = new RECT[conExtras?.Tonos.Length ?? 0];
        enlaces = conEnlaces?.ToArray() ?? [];
        rectEnlaces = new RECT[enlaces.Length];
        rectAvatares = new RECT[enlaces.Length];
        rectCoinciden = new RECT[enlaces.Length];
        rectJugada = default; rectLeyenda = default;
        compras = (conCompras ?? []).Take(3).ToArray();
        rectCompras = new RECT[compras.Length];
        rectTurnos = new RECT[Math.Max(conExtras?.VidaYo.Length ?? 0, conExtras?.VidaRival.Length ?? 0)];
        copiado = false;
        desplazamiento = 0;
        huecos = [];
        var (anchoArena, altoArena) = MedidasDeArena();
        // EL ANCHO: con extras (el resumen), a dos columnas y tan ancho como
        // permita Arena hasta 1180; sin ellos, el de siempre (758).
        // Importar, algo más estrecho: son filas de texto, a dos columnas si hay muchas.
        var anchoDeseado = extras is not null || est is not null ? Math.Clamp(anchoArena - 80, 760, 1180)
            : imp is not null ? Math.Clamp(anchoArena - 80, 760, 980) : 758;
        (anchoCarta, altoCarta) = ((anchoDeseado - MARGEN * 2 - (POR_FILA - 1) * AIRE) / POR_FILA, 237);
        var altoMaximo = altoArena - 60;

        // Se mide y se dispone; si no cabe sin scroll, con la letra un punto
        // menor (dos veces como mucho). Sólo con extras: el resto ya cabía.
        TAM_TITULO_SECCION = 21; TAM_TEXTO = 19;
        for (var intento = 0; ; intento++)
        {
            Disponer(secciones, tituloEnlaces, tituloCompras);
            var altoNecesario = ALTO_CABECERA + MARGEN / 2 + altoTexto + MARGEN + ALTO_PIE + MARGEN / 2;
            if ((extras is null && est is null) || altoNecesario <= altoMaximo || intento >= 2) break;
            TAM_TITULO_SECCION -= 2; TAM_TEXTO -= 1;
        }
        alto = Math.Min(altoMaximo, ALTO_CABECERA + MARGEN / 2 + altoTexto + MARGEN + ALTO_PIE + MARGEN / 2);
        texto = copia;
        bajoRaton = -1;
        hilo = new Thread(Correr) { IsBackground = true, Name = "panel" };
        hilo.Start();
    }

    /// <summary>
    /// LA DISPOSICIÓN: cada bloque en su columna, medido con la fuente con la
    /// que se pinta. Con extras: el titular a lo ancho; a la izquierda la
    /// jugada y la curva; a la derecha el entrenador y las secciones; los
    /// mazos y las compras abajo a lo ancho (pedido del usuario el 2026-09-27:
    /// «haz dos columnas, porque normalmente se juega en pantalla ancha»).
    /// </summary>
    private static void Disponer(IReadOnlyList<SeccionTexto> secciones, string? tituloEnlaces, string? tituloCompras)
    {
        var hdc = GetDC(IntPtr.Zero);
        var fTitulo = CreateFont(TAM_TITULO_SECCION, 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        var fTexto = CreateFont(TAM_TEXTO, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        var anterior = SelectObject(hdc, fTexto);
        var anchoTodo = ANCHO_PANEL - MARGEN * 2;
        var dosColumnas = extras is not null || est is not null || imp is not null;
        // Stats reparte casi a medias (la evolución y los mazos piden sitio); importar, a medias; el resumen, 42/58.
        var anchoIzq = dosColumnas ? (anchoTodo - HUECO_COLUMNAS) * (est is not null ? 54 : imp is not null ? 50 : 42) / 100 : anchoTodo;
        var anchoDer = dosColumnas ? anchoTodo - HUECO_COLUMNAS - anchoIzq : anchoTodo;
        int AnchoDe(int col) => col == 1 ? anchoIzq : col == 2 ? anchoDer : anchoTodo;
        var colTexto = dosColumnas ? 2 : 0;

        var lista = new List<Bloque>();
        if (est is { } estadisticas) AnadirBloquesEstadisticas(lista, estadisticas);
        int Medir(string t, int anchoCol, bool titulo)
        {
            SelectObject(hdc, titulo ? fTitulo : fTexto);
            var r = new RECT { Left = 0, Top = 0, Right = anchoCol, Bottom = 0 };
            DrawText(hdc, t, -1, ref r, DT_LEFT | DT_WORDBREAK | DT_CALCRECT);
            return r.Bottom - r.Top;
        }
        if (imp is { } importacion) AnadirBloquesImportacion(lista, importacion, anchoTodo, Medir);
        if (extras is { } ex)
        {
            if (ex.Titular is { Length: > 0 })
            {
                var fGrande = CreateFont(26, 0, 0, 0, 800, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
                SelectObject(hdc, fGrande);
                var rt = new RECT { Left = 0, Top = 0, Right = anchoTodo - 90, Bottom = 0 };
                DrawText(hdc, ex.Titular, -1, ref rt, DT_LEFT | DT_WORDBREAK | DT_CALCRECT);
                DeleteObject(fGrande);
                // Alto mínimo 64: el círculo de la nota (radio 22) va a 30 px del
                // borde y así no se pega a la cabecera del panel (el usuario, 2026-09-27).
                lista.Add(new Bloque(ex.Titular, false, false, Math.Max(64, rt.Bottom - rt.Top), Especial: "titular", Col: 0));
            }
            if (ex.JugadaNombre is { Length: > 0 })
            {
                var altoFrase = Medir(ex.JugadaFrase ?? "", anchoIzq - 110, false);
                lista.Add(new Bloque("", false, false, Math.Max(ALTO_JUGADA + 40, 28 + 24 + altoFrase + 6), Especial: "jugada", Col: 1));
            }
            if (ex.VidaYo.Count(v => v is not null) >= 2) lista.Add(new Bloque("", false, false, ALTO_GRAFICO + 30, Especial: "vidas", Col: 1));
            lista.Add(new Bloque("", false, false, 112, Especial: "entrenador", Col: 2));
        }
        foreach (var sec in secciones)
        {
            lista.Add(new Bloque(sec.Titulo, true, false, Medir(sec.Titulo, AnchoDe(colTexto), true), Col: colTexto));
            foreach (var p in sec.Parrafos)
                lista.Add(new Bloque(p, false, sec.Lista, Medir(p, AnchoDe(colTexto) - (sec.Lista ? SANGRIA_VINETA : 0), false), Col: colTexto));
        }
        if (enlaces.Length > 0)
        {
            // BAJO LA CURVA, en la columna izquierda, cada mazo en dos filas
            // (el usuario, 2026-09-27): arriba, pequeño, DE DÓNDE VIENE —logo y
            // nombre del origen, autor con su avatar, fecha— y a la derecha
            // cuántas cartas coinciden; debajo, el nombre del mazo entero.
            var colMazos = dosColumnas ? 1 : 0;
            var rotulo = tituloEnlaces ?? "";
            lista.Add(new Bloque(rotulo, true, false, Medir(rotulo, AnchoDe(colMazos), true), Col: colMazos));
            for (var i = 0; i < enlaces.Length; i++)
                lista.Add(new Bloque(enlaces[i].Texto, false, true, CABEZA_MAZO + Medir(enlaces[i].Texto, AnchoDe(colMazos) - SANGRIA_VINETA, false) + 2, i, Col: colMazos));
        }
        if (compras.Length > 0)
        {
            var rotulo = tituloCompras ?? "";
            lista.Add(new Bloque(rotulo, true, false, Medir(rotulo, anchoTodo, true)));
            lista.Add(new Bloque("", false, false, ALTO_MINI_COMPRA + 6, -1, true));
        }
        SelectObject(hdc, anterior); DeleteObject(fTitulo); DeleteObject(fTexto); ReleaseDC(IntPtr.Zero, hdc);

        // La posición de cada bloque: cada columna lleva su cuenta; un bloque a
        // lo ancho espera a la más larga y las iguala.
        bloques = lista.ToArray();
        topBloques = new int[bloques.Length]; xBloques = new int[bloques.Length]; anchoBloques = new int[bloques.Length];
        int yIzq = 0, yDer = 0;
        bool primeroIzq = true, primeroDer = true, primeroTodo = true;
        for (var i = 0; i < bloques.Length; i++)
        {
            var b = bloques[i];
            if (b.Col == 0)
            {
                var y = Math.Max(yIzq, yDer);
                if (b.Titulo && !primeroTodo) y += AIRE_ANTES_TITULO;
                topBloques[i] = y; xBloques[i] = MARGEN; anchoBloques[i] = anchoTodo;
                yIzq = yDer = y + b.Alto + AIRE_PARRAFO; primeroTodo = false;
            }
            else if (b.Col == 1)
            {
                if (b.Titulo && !primeroIzq) yIzq += AIRE_ANTES_TITULO;
                topBloques[i] = yIzq; xBloques[i] = MARGEN; anchoBloques[i] = anchoIzq;
                yIzq += b.Alto + AIRE_PARRAFO; primeroIzq = false;
            }
            else
            {
                if (b.Titulo && !primeroDer) yDer += AIRE_ANTES_TITULO;
                topBloques[i] = yDer; xBloques[i] = MARGEN + anchoIzq + HUECO_COLUMNAS; anchoBloques[i] = anchoDer;
                yDer += b.Alto + AIRE_PARRAFO; primeroDer = false;
            }
        }
        altoTexto = Math.Max(yIzq, yDer);
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
    private static void PintarEspecial(IntPtr zona, string especial, ResumenExtras ex, int y, int alto, int arriba, IntPtr fTituloSec, IntPtr fTexto, int bx, int bw)
    {
        var anchoZona = bw;
        var MARGEN = bx;   // lo que sigue mide desde el borde de SU columna
        switch (especial)
        {
            case "entrenador":
            {
                // EL ENTRENADOR GRANDE: el avatar del tono en marcha (alto 100,
                // con su proporción) y a su derecha el rótulo y los tres tonos.
                IniciarGdiPlus();
                GdipCreateFromHDC(zona, out var g);
                if (g != IntPtr.Zero) GdipSetInterpolationMode(g, 7);
                var iActual = Array.IndexOf(ex.Tonos, ex.TonoActual);
                var imgGrande = iActual >= 0 && ex.TonoAvatares[iActual] is not null ? Imagen(ex.TonoAvatares[iActual]!) : IntPtr.Zero;
                var x = MARGEN;
                if (imgGrande != IntPtr.Zero && g != IntPtr.Zero && GdipGetImageWidth(imgGrande, out var gw) == 0 && GdipGetImageHeight(imgGrande, out var gh) == 0 && gh > 0)
                {
                    var altoG = 100; var anchoG = Math.Max(30, (int)Math.Round(altoG * (double)gw / gh));
                    GdipDrawImageRectI(g, imgGrande, x, y + 4, anchoG, altoG);
                    x += anchoG + 14;
                }
                SelectObject(zona, fTituloSec);
                SetTextColor(zona, Rgb(252, 211, 77));
                var rr = new RECT { Left = x, Top = y + 2, Right = MARGEN + anchoZona, Bottom = y + 30 };
                DrawText(zona, ex.RotuloTono, -1, ref rr, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
                SelectObject(zona, fTexto);
                var cx = x; var cy = y + 38;
                for (var i = 0; i < ex.Tonos.Length; i++)
                {
                    var activo = ex.Tonos[i] == ex.TonoActual;
                    var sobre = bajoRaton == BAJO_TONO - i;
                    var etiqueta = ex.TonoEtiquetas[i];
                    var rm = new RECT();
                    DrawText(zona, etiqueta, -1, ref rm, DT_CALCRECT | DT_SINGLELINE);
                    var imgAvatar = ex.TonoAvatares[i] is not null ? Imagen(ex.TonoAvatares[i]!) : IntPtr.Zero;
                    var conAvatar = imgAvatar != IntPtr.Zero;
                    var anchoAvatar = LADO_AVATAR_TONO;
                    if (conAvatar && GdipGetImageWidth(imgAvatar, out var aw) == 0 && GdipGetImageHeight(imgAvatar, out var ah) == 0 && ah > 0)
                        anchoAvatar = Math.Max(14, (int)Math.Round(LADO_AVATAR_TONO * (double)aw / ah));
                    var anchoFicha = 16 + (conAvatar ? anchoAvatar + 6 : 0) + (rm.Right - rm.Left);
                    if (cx + anchoFicha > MARGEN + anchoZona && cx > x) { cx = x; cy += ALTO_TONOS; }
                    var rf = new RECT { Left = cx, Top = cy, Right = cx + anchoFicha, Bottom = cy + ALTO_TONOS - 6 };
                    rectTonos[i] = new RECT { Left = rf.Left, Top = arriba + rf.Top, Right = rf.Right, Bottom = arriba + rf.Bottom };
                    var pincel = CreateSolidBrush(activo ? Rgb(56, 189, 248) : sobre ? Rgb(40, 52, 78) : Rgb(26, 34, 54));
                    FillRect(zona, ref rf, pincel);
                    DeleteObject(pincel);
                    var tx = cx + 8;
                    if (conAvatar && g != IntPtr.Zero) { GdipDrawImageRectI(g, imgAvatar, tx, cy + 2, anchoAvatar, LADO_AVATAR_TONO); tx += anchoAvatar + 6; }
                    SetTextColor(zona, activo ? Rgb(9, 13, 24) : Rgb(226, 232, 240));
                    var rt = new RECT { Left = tx, Top = rf.Top, Right = rf.Right - 8, Bottom = rf.Bottom };
                    DrawText(zona, etiqueta, -1, ref rt, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
                    cx += anchoFicha + 8;
                }
                if (g != IntPtr.Zero) GdipDeleteGraphics(g);
                break;
            }
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
                    var cx = MARGEN + anchoZona - 36; var cy = y + 30;
                    Ellipse(zona, cx - 22, cy - 22, cx + 22, cy + 22);
                    SelectObject(zona, plumaAnterior); SelectObject(zona, pincelAnterior); DeleteObject(pincel);
                    var fNota = CreateFont(22, 0, 0, 0, 800, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
                    SelectObject(zona, fNota);
                    SetTextColor(zona, Rgb(9, 13, 24));
                    var rn = new RECT { Left = cx - 22, Top = cy - 22, Right = cx + 22, Bottom = cy + 22 };
                    DrawText(zona, $"{nota}", -1, ref rn, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
                    DeleteObject(fNota);
                    SelectObject(zona, fTexto);
                    SetTextColor(zona, Rgb(148, 163, 184));
                    var r10 = new RECT { Left = cx - 30, Top = cy + 24, Right = cx + 30, Bottom = cy + 42 };
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
                        GdipDrawImageRectI(g, img, MARGEN, y + 28, 70, 98);
                        GdipDeleteGraphics(g);
                    }
                    rectJugada = new RECT { Left = MARGEN, Top = arriba + y + 28, Right = MARGEN + 70, Bottom = arriba + y + 28 + 98 };
                    tx += 80;
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
                var gx = MARGEN + 30; var gy = y + 34; var gw = anchoZona - 30 - 100; var gh = ALTO_GRAFICO - 30;
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
                // LAS MARCAS: una calavera en cada turno con un error del
                // entrenador; en el turno decisivo, trofeo si la jugada fue tuya
                // y calavera si fue del rival. Al pasar el ratón sale la nota.
                var fEmoji = CreateFont(20, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI Emoji");
                var anteriorEmoji = SelectObject(zona, fEmoji);
                for (var i = 0; i < n; i++)
                {
                    var turno = i + 1;
                    rectTurnos[i] = new RECT { Left = X(i) - 9, Top = arriba + gy, Right = X(i) + 9, Bottom = arriba + gy + gh + 22 };
                    // La calavera es SIEMPRE un error tuyo; la copa, algo que hiciste
                    // bien (un acierto del entrenador o tu jugada decisiva); el golpe
                    // decisivo del rival, una explosión (el usuario, 2026-09-27).
                    var esError = ex.NotasPorTurno?.ContainsKey(turno) == true;
                    var esAcierto = ex.AciertosPorTurno?.ContainsKey(turno) == true || (ex.JugadaTurno == turno && ex.JugadaBando != "rival");
                    var golpeRival = ex.JugadaTurno == turno && ex.JugadaBando == "rival";
                    if (!esError && !esAcierto && !golpeRival) continue;
                    var glifo = esError ? "\U0001F480" : esAcierto ? "\U0001F3C6" : "\U0001F4A5";
                    SetTextColor(zona, esError ? Rgb(248, 113, 113) : esAcierto ? Rgb(252, 211, 77) : Rgb(251, 146, 60));
                    var vy = ex.VidaYo.Length > i ? ex.VidaYo[i] : null;
                    var my = vy is { } vv ? Y(vv) - 24 : gy;
                    var rMarca = new RECT { Left = X(i) - 12, Top = Math.Max(gy - 2, my), Right = X(i) + 12, Bottom = Math.Max(gy - 2, my) + 22 };
                    DrawText(zona, glifo, -1, ref rMarca, DT_CENTER | DT_SINGLELINE);
                }
                SelectObject(zona, anteriorEmoji); DeleteObject(fEmoji);
                // El número de turno bajo el eje, cada pocos turnos.
                SelectObject(zona, fPieChico());
                SetTextColor(zona, Rgb(100, 116, 139));
                for (var i = 0; i < n; i += Math.Max(1, n / 8))
                {
                    var rtn = new RECT { Left = X(i) - 12, Top = gy + gh + 3, Right = X(i) + 12, Bottom = gy + gh + 18 };
                    DrawText(zona, $"{i + 1}", -1, ref rtn, DT_CENTER | DT_SINGLELINE);
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
                // LAS VIDAS AL ACABAR, junto a cada nombre y nunca por debajo de 0
                // (un «10 · 13» suelto no se entendía: el usuario, 2026-09-27); al
                // pasar el ratón por la leyenda se explica.
                var ultimoYo = ex.VidaYo.LastOrDefault(v => v is not null); var ultimoRival = ex.VidaRival.LastOrDefault(v => v is not null);
                SetTextColor(zona, Rgb(255, 255, 255));
                var rn1 = new RECT { Left = lx + 16, Top = gy - 2, Right = lx + 88, Bottom = gy + 14 }; DrawText(zona, ultimoYo is { } uy ? $"{Math.Max(0, uy)}" : "?", -1, ref rn1, DT_RIGHT | DT_SINGLELINE);
                var rn2 = new RECT { Left = lx + 16, Top = gy + 20, Right = lx + 88, Bottom = gy + 36 }; DrawText(zona, ultimoRival is { } ur ? $"{Math.Max(0, ur)}" : "?", -1, ref rn2, DT_RIGHT | DT_SINGLELINE);
                rectLeyenda = new RECT { Left = lx, Top = arriba + gy - 4, Right = lx + 92, Bottom = arriba + gy + 40 };
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
        rel = null;
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
        var y = ArribaContenido;
        var izquierda = IzquierdaContenido;
        foreach (var s in secciones)
        {
            if (s.Cartas.Count == 0) continue;
            if (s.Rotulo is not null)
            {
                lista.Add(new Hueco(izquierda, y, ANCHO_PANEL - izquierda - MARGEN, ALTO_ROTULO, null, s));
                y += ALTO_ROTULO;
            }
            for (var i = 0; i < s.Cartas.Count; i++)
            {
                var x = izquierda + (i % POR_FILA) * (anchoCarta + AIRE);
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
            anchoRegion = ANCHO_PANEL;
            SetWindowRgn(ventana, CreateRoundRectRgn(0, 0, ANCHO_PANEL + 1, alto + 1, 16, 16), true);
            // Stats casi opaco: sus gráficas no se leen con el juego asomando por detrás.
            SetLayeredWindowAttributes(ventana, 0, (byte)(est is not null || imp is not null ? 252 : 244), LWA_ALPHA);
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
        if (altoRegion != alto || anchoRegion != ANCHO_PANEL)
        {
            altoRegion = alto;
            anchoRegion = ANCHO_PANEL;
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
            var ci = QueHayEnImportacion(xr, yr);
            if (ci != 0) return ci;
            var rb = RectBotonCopiar();
            if (imp is null && xr >= rb.Left && xr < rb.Right && yr >= rb.Top && yr < rb.Bottom) return BAJO_COPIAR;
            // El avatar antes que su mazo: está dentro de su tarjeta.
            var rav = rectAvatares;
            for (var i = 0; i < rav.Length; i++)
                if (rav[i].Right > 0 && xr >= rav[i].Left && xr < rav[i].Right && yr >= rav[i].Top && yr < rav[i].Bottom) return BAJO_AVATAR - i;
            var ce = QueHayEnEstadisticas(xr, yr);
            if (ce != 0) return ce;
            var rco = rectCoinciden;
            for (var i = 0; i < rco.Length; i++)
                if (rco[i].Right > 0 && xr >= rco[i].Left && xr < rco[i].Right && yr >= rco[i].Top && yr < rco[i].Bottom) return BAJO_COINCIDEN - i;
            var rects = rectEnlaces;
            for (var i = 0; i < rects.Length; i++)
                if (xr >= rects[i].Left && xr < rects[i].Right && yr >= rects[i].Top && yr < rects[i].Bottom) return BAJO_ENLACE - i;
            var rc = rectCompras;
            for (var i = 0; i < rc.Length; i++)
                if (xr >= rc[i].Left && xr < rc[i].Right && yr >= rc[i].Top && yr < rc[i].Bottom) return BAJO_COMPRA - i;
            var rtn = rectTonos;
            for (var i = 0; i < rtn.Length; i++)
                if (xr >= rtn[i].Left && xr < rtn[i].Right && yr >= rtn[i].Top && yr < rtn[i].Bottom) return BAJO_TONO - i;
            var rtu = rectTurnos;
            for (var i = 0; i < rtu.Length; i++)
                if (rtu[i].Right > 0 && xr >= rtu[i].Left && xr < rtu[i].Right && yr >= rtu[i].Top && yr < rtu[i].Bottom) return BAJO_TURNO - i;
            if (rectJugada.Right > 0 && xr >= rectJugada.Left && xr < rectJugada.Right && yr >= rectJugada.Top && yr < rectJugada.Bottom) return BAJO_JUGADA;
            if (rectLeyenda.Right > 0 && xr >= rectLeyenda.Left && xr < rectLeyenda.Right && yr >= rectLeyenda.Top && yr < rectLeyenda.Bottom) return BAJO_LEYENDA;
        }
        var x = (short)(lParam.ToInt64() & 0xFFFF);
        var y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        var cr = QueHayEnRelacionadas(x, y);
        if (cr != 0) return cr;
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
                var pulsable = i == -2 || i == BAJO_COPIAR || (i <= BAJO_ENLACE && i > BAJO_TONO) || (i <= BAJO_TONO && i > BAJO_TURNO) || (i <= BAJO_AVATAR && i > BAJO_JUGADA) || (i <= BAJO_COINCIDEN && i > BAJO_PUNTO_EST)
                    || PulsableEstadisticas(i) || PulsableImportacion(i) || PulsableRelacionadas(i)
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
                if (PulsarEstadisticas(i, hWnd)) return IntPtr.Zero;
                if (PulsarImportacion(i, hWnd)) return IntPtr.Zero;
                if (PulsarRelacionadas(i, hWnd)) return IntPtr.Zero;
                if (i <= BAJO_COINCIDEN && BAJO_COINCIDEN - i < enlaces.Length && AlAbrir is { } abrirMazo)
                {
                    var rutaMazo = enlaces[BAJO_COINCIDEN - i].Ruta;
                    _ = Task.Run(() => { try { abrirMazo(rutaMazo); } catch { /* lo cuenta quien lo montó */ } });
                    return IntPtr.Zero;
                }
                if (i <= BAJO_AVATAR && BAJO_AVATAR - i < enlaces.Length && AlAbrir is { } abrirAvatar)
                {
                    var rutaAvatar = enlaces[BAJO_AVATAR - i].Ruta;
                    _ = Task.Run(() => { try { abrirAvatar(rutaAvatar); } catch { /* lo cuenta quien lo montó */ } });
                    return IntPtr.Zero;
                }
                if (i <= BAJO_TONO && i > BAJO_TURNO && extras is { } exT && BAJO_TONO - i < exT.Tonos.Length)
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

            // Ha llegado una pestaña (ver PanelRelacionadas.CargarPestana): a su tamaño y a pintar.
            case WM_REHACER:
                Recolocar();
                InvalidateRect(hWnd, IntPtr.Zero, false);
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
                rectJugada = default; rectLeyenda = default;
                for (var i = 0; i < bloques.Length; i++)
                {
                    var b = bloques[i];
                    var y = (i < topBloques.Length ? topBloques[i] : 0) - desplazamiento;
                    var bx = i < xBloques.Length ? xBloques[i] : MARGEN;
                    var bw = i < anchoBloques.Length ? anchoBloques[i] : ANCHO_PANEL - MARGEN * 2;
                    if (b.Especial is { } especialEst && especialEst.StartsWith("est_", StringComparison.Ordinal) && est is { } es)
                    {
                        if (y + b.Alto >= 0 && y < altoZona) PintarEstadistica(zona, especialEst, es, y, b.Alto, arriba, bx, bw);
                        continue;
                    }
                    if (b.Especial is { } especialImp && especialImp.StartsWith("imp_", StringComparison.Ordinal) && imp is { } im)
                    {
                        // Siempre, aunque no se vea: así deja a cero el sitio de lo que no está a la vista.
                        PintarImportacion(zona, especialImp, im, y, b.Alto, arriba, bx, bw, altoZona);
                        continue;
                    }
                    if (b.Especial is { } especial && extras is { } ex)
                    {
                        if (y + b.Alto >= 0 && y < altoZona) PintarEspecial(zona, especial, ex, y, b.Alto, arriba, fTituloSec, fTexto, bx, bw);
                        else if (especial == "tonos") for (var k = 0; k < rectTonos.Length; k++) rectTonos[k] = default;
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
                                var x = bx + c * (ANCHO_FICHA_COMPRA + AIRE_COMPRA);
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
                        continue;
                    }
                    if (b.Enlace >= 0 && b.Enlace < enlaces.Length)
                    {
                        // UN MAZO SUGERIDO, en dos filas. Arriba, pequeño: el logo y el
                        // nombre de su origen, el autor con su avatar y la fecha, y a la
                        // derecha «N en común». Debajo, en azul, el nombre del mazo entero.
                        // Su rectángulo en la ventana (la zona empieza en `arriba`), para el ratón.
                        rectEnlaces[b.Enlace] = new RECT { Left = bx, Top = arriba + y, Right = bx + bw, Bottom = arriba + y + b.Alto };
                        rectAvatares[b.Enlace] = default;
                        rectCoinciden[b.Enlace] = default;
                        if (y + b.Alto < 0 || y >= altoZona) continue;
                        var e = enlaces[b.Enlace];
                        var sobreEnlace = bajoRaton == BAJO_ENLACE - b.Enlace || bajoRaton == BAJO_COINCIDEN - b.Enlace;
                        if (sobreEnlace)
                        {
                            var pincelSobre = CreateSolidBrush(Rgb(26, 34, 54));
                            var rSobre = new RECT { Left = bx - 6, Top = y - 3, Right = bx + bw + 6, Bottom = y + b.Alto + 3 };
                            FillRect(zona, ref rSobre, pincelSobre); DeleteObject(pincelSobre);
                        }
                        // «N en común», a la derecha: verde con 3 o más, ámbar con 2, gris con 1.
                        var limite = bx + bw;
                        if (e.Coinciden is { Length: > 0 } coinciden)
                        {
                            var fEtiqueta = CreateFont(14, 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
                            var anteriorEtiqueta = SelectObject(zona, fEtiqueta);
                            var rMide = new RECT();
                            DrawText(zona, coinciden, -1, ref rMide, DT_CALCRECT | DT_SINGLELINE);
                            var anchoEtiqueta = rMide.Right - rMide.Left + 16;
                            var rEtiqueta = new RECT { Left = bx + bw - anchoEtiqueta, Top = y + 2, Right = bx + bw, Bottom = y + 22 };
                            var sobreEtiqueta = bajoRaton == BAJO_COINCIDEN - b.Enlace;
                            var (fondoEtiqueta, letraEtiqueta) = e.Nivel >= 3 ? (Rgb(22, 101, 52), Rgb(220, 252, 231))
                                : e.Nivel == 2 ? (Rgb(120, 53, 15), Rgb(254, 243, 199)) : (Rgb(51, 65, 85), Rgb(226, 232, 240));
                            var pincelEtiqueta = CreateSolidBrush(sobreEtiqueta ? Rgb(56, 189, 248) : fondoEtiqueta);
                            FillRect(zona, ref rEtiqueta, pincelEtiqueta); DeleteObject(pincelEtiqueta);
                            SetTextColor(zona, sobreEtiqueta ? Rgb(9, 13, 24) : letraEtiqueta);
                            DrawText(zona, coinciden, -1, ref rEtiqueta, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
                            SelectObject(zona, anteriorEtiqueta); DeleteObject(fEtiqueta);
                            rectCoinciden[b.Enlace] = new RECT { Left = rEtiqueta.Left, Top = arriba + rEtiqueta.Top, Right = rEtiqueta.Right, Bottom = arriba + rEtiqueta.Bottom };
                            limite = rEtiqueta.Left - 10;
                        }
                        // El logo del origen, delante.
                        var imgLogo = e.Logo is not null ? Imagen(e.Logo) : IntPtr.Zero;
                        var imgAvatar = e.Avatar is not null ? Imagen(e.Avatar) : IntPtr.Zero;
                        var xCab = bx + SANGRIA_VINETA;
                        IniciarGdiPlus();
                        if (imgLogo != IntPtr.Zero && GdipCreateFromHDC(zona, out var gLogo) == 0 && gLogo != IntPtr.Zero)
                        {
                            GdipSetInterpolationMode(gLogo, 7);
                            GdipDrawImageRectI(gLogo, imgLogo, bx, y + 4, LADO_LOGO, LADO_LOGO);
                            GdipDeleteGraphics(gLogo);
                        }
                        // Su nombre («MTG Corner · Meta», «Moxfield»), más claro.
                        var fOrigen = CreateFont(15, 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
                        var fAutor = CreateFont(15, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
                        var anteriorOrigen = SelectObject(zona, fOrigen);
                        int Escribir(string t, uint color, bool recortar)
                        {
                            if (xCab >= limite || t.Length == 0) return 0;
                            SetTextColor(zona, color);
                            var rMed = new RECT();
                            DrawText(zona, t, -1, ref rMed, DT_CALCRECT | DT_SINGLELINE);
                            var r = new RECT { Left = xCab, Top = y + 2, Right = Math.Min(limite, xCab + rMed.Right - rMed.Left), Bottom = y + 22 };
                            DrawText(zona, t, -1, ref r, DT_LEFT | DT_VCENTER | DT_SINGLELINE | (recortar ? DT_END_ELLIPSIS : 0));
                            return r.Right - r.Left;
                        }
                        xCab += Escribir(e.Origen ?? "", Rgb(203, 213, 225), true);
                        SelectObject(zona, fAutor);
                        var hayAutor = e.Autor is { Length: > 0 } || imgAvatar != IntPtr.Zero;
                        if (hayAutor || e.Fecha is { Length: > 0 }) xCab += Escribir("  ·  ", Rgb(100, 116, 139), false);
                        if (imgAvatar != IntPtr.Zero && xCab + LADO_AVATAR < limite && GdipCreateFromHDC(zona, out var gAvatar) == 0 && gAvatar != IntPtr.Zero)
                        {
                            GdipSetInterpolationMode(gAvatar, 7);
                            GdipDrawImageRectI(gAvatar, imgAvatar, xCab, y + 3, LADO_AVATAR, LADO_AVATAR);
                            GdipDeleteGraphics(gAvatar);
                            rectAvatares[b.Enlace] = new RECT { Left = xCab, Top = arriba + y + 3, Right = xCab + LADO_AVATAR, Bottom = arriba + y + 3 + LADO_AVATAR };
                            xCab += LADO_AVATAR + 6;
                        }
                        var resto = string.Join("  ·  ", new[] { e.Autor, e.Fecha }.Where(v => v is { Length: > 0 }));
                        Escribir(resto, Rgb(148, 163, 184), true);
                        SelectObject(zona, anteriorOrigen); DeleteObject(fOrigen); DeleteObject(fAutor);
                        // Debajo, el nombre del mazo entero, en azul (blanco al pasar).
                        SelectObject(zona, fTexto);
                        SetTextColor(zona, sobreEnlace ? Rgb(255, 255, 255) : Rgb(56, 189, 248));
                        var rNombre = new RECT { Left = bx + SANGRIA_VINETA, Top = y + CABEZA_MAZO, Right = bx + bw, Bottom = y + b.Alto };
                        DrawText(zona, b.Texto, -1, ref rNombre, DT_LEFT | DT_WORDBREAK);
                        continue;
                    }
                    if (y + b.Alto >= 0 && y < altoZona)
                    {
                        SelectObject(zona, b.Titulo ? fTituloSec : fTexto);
                        SetTextColor(zona, b.Titulo ? Rgb(252, 211, 77) : Rgb(226, 232, 240));
                        if (b.Vineta)
                        {
                            var rv = new RECT { Left = bx, Top = y, Right = bx + SANGRIA_VINETA, Bottom = y + b.Alto };
                            DrawText(zona, "•", -1, ref rv, DT_LEFT);
                        }
                        var rb = new RECT { Left = bx + (b.Vineta ? SANGRIA_VINETA : 0), Top = y, Right = bx + bw, Bottom = y + b.Alto };
                        DrawText(zona, b.Texto, -1, ref rb, DT_LEFT | DT_WORDBREAK | DT_END_ELLIPSIS);
                    }
                }
                BitBlt(hdc, 0, arriba, ANCHO_PANEL, altoZona, zona, 0, 0, SRCCOPY);
                SelectObject(zona, zonaAnterior); DeleteObject(lienzoZona); DeleteDC(zona);
                DeleteObject(fTituloSec); DeleteObject(fTexto);

                // EL ZOOM DEL AVATAR de un mazo sugerido: cuatro veces más grande,
                // junto a él y dentro del panel.
                if (bajoRaton <= BAJO_AVATAR && BAJO_AVATAR - bajoRaton < enlaces.Length && grafico != IntPtr.Zero)
                {
                    var e = enlaces[BAJO_AVATAR - bajoRaton];
                    var img = e.Avatar is not null ? Imagen(e.Avatar) : IntPtr.Zero;
                    if (img != IntPtr.Zero)
                    {
                        var rc = rectAvatares[BAJO_AVATAR - bajoRaton];
                        const int lado = 72;
                        var zx = Math.Min(rc.Right + 8, ANCHO_PANEL - MARGEN - lado);
                        var zy = Math.Clamp(rc.Top - lado / 2, ALTO_CABECERA, Math.Max(ALTO_CABECERA, alto - ALTO_PIE - MARGEN - lado));
                        GdipDrawImageRectI(grafico, img, zx, zy, lado, lado);
                    }
                }
                // STATS: el día (o la partida) bajo el ratón en la evolución.
                PintarAyudaEstadisticas(hdc);

                // LAS CARTAS EN COMÚN con un mazo sugerido, al pasar por su etiqueta.
                if (bajoRaton <= BAJO_COINCIDEN && BAJO_COINCIDEN - bajoRaton < enlaces.Length && enlaces[BAJO_COINCIDEN - bajoRaton].Cartas is { Length: > 0 } cartasComun)
                {
                    var ec = enlaces[BAJO_COINCIDEN - bajoRaton];
                    var textoCaja = (ec.RotuloCartas is { Length: > 0 } rot ? rot + "\n" : "") + cartasComun;
                    var fCaja = CreateFont(15, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
                    var anteriorCaja = SelectObject(hdc, fCaja);
                    const int anchoCaja = 260;
                    var rm = new RECT { Left = 0, Top = 0, Right = anchoCaja - 16, Bottom = 0 };
                    DrawText(hdc, textoCaja, -1, ref rm, DT_LEFT | DT_WORDBREAK | DT_CALCRECT);
                    var altoCaja = Math.Min(220, rm.Bottom - rm.Top + 16);
                    var rc = rectCoinciden[BAJO_COINCIDEN - bajoRaton];
                    var cx0 = Math.Clamp(rc.Right - anchoCaja, MARGEN, ANCHO_PANEL - MARGEN - anchoCaja);
                    var cy0 = rc.Bottom + 6 + altoCaja <= alto - ALTO_PIE ? rc.Bottom + 6 : Math.Max(ALTO_CABECERA, rc.Top - altoCaja - 6);
                    var rCaja = new RECT { Left = cx0, Top = cy0, Right = cx0 + anchoCaja, Bottom = cy0 + altoCaja };
                    var pincelCaja = CreateSolidBrush(Rgb(30, 41, 59)); FillRect(hdc, ref rCaja, pincelCaja); DeleteObject(pincelCaja);
                    var pincelBorde = CreateSolidBrush(Rgb(56, 189, 248));
                    var rBorde = new RECT { Left = rCaja.Left, Top = rCaja.Top, Right = rCaja.Right, Bottom = rCaja.Top + 2 }; FillRect(hdc, ref rBorde, pincelBorde); DeleteObject(pincelBorde);
                    SetTextColor(hdc, Rgb(241, 245, 249));
                    var rTexto = new RECT { Left = rCaja.Left + 8, Top = rCaja.Top + 8, Right = rCaja.Right - 8, Bottom = rCaja.Bottom - 6 };
                    DrawText(hdc, textoCaja, -1, ref rTexto, DT_LEFT | DT_WORDBREAK | DT_END_ELLIPSIS);
                    SelectObject(hdc, anteriorCaja); DeleteObject(fCaja);
                }

                // LA NOTA DEL TURNO bajo el ratón en la curva: el error de ese turno
                // o la jugada decisiva, en una caja encima del gráfico.
                if (extras is { } exN && ((bajoRaton <= BAJO_TURNO && bajoRaton > BAJO_AVATAR) || bajoRaton == BAJO_LEYENDA))
                {
                    string? nota = null; RECT rc;
                    if (bajoRaton == BAJO_LEYENDA)
                    {
                        rc = rectLeyenda;
                        var vYo = exN.VidaYo.LastOrDefault(v => v is not null); var vRival = exN.VidaRival.LastOrDefault(v => v is not null);
                        nota = $"{exN.RotuloVidasFinal}: {exN.RotuloTu} {(vYo is { } a1 ? Math.Max(0, a1).ToString() : "?")} · {exN.RotuloRival} {(vRival is { } a2 ? Math.Max(0, a2).ToString() : "?")}";
                    }
                    else
                    {
                        var turno = BAJO_TURNO - bajoRaton + 1;
                        rc = rectTurnos[BAJO_TURNO - bajoRaton];
                        var trozos = new List<string>();
                        if (exN.NotasPorTurno is { } notas && notas.TryGetValue(turno, out var n1)) trozos.Add(n1);
                        if (exN.AciertosPorTurno is { } aciertos && aciertos.TryGetValue(turno, out var n2)) trozos.Add(n2);
                        if (exN.JugadaTurno == turno) trozos.Add((exN.JugadaNombre ?? "") + ": " + (exN.JugadaFrase ?? ""));
                        if (trozos.Count > 0) nota = string.Join("\n\n", trozos);
                    }
                    if (nota is not null)
                    {
                        var fNota = CreateFont(15, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
                        var anteriorNota = SelectObject(hdc, fNota);
                        var anchoCaja = 300;
                        var rm = new RECT { Left = 0, Top = 0, Right = anchoCaja - 16, Bottom = 0 };
                        DrawText(hdc, nota, -1, ref rm, DT_LEFT | DT_WORDBREAK | DT_CALCRECT);
                        var altoCaja = Math.Min(200, rm.Bottom - rm.Top + 16);
                        var cx0 = Math.Clamp(rc.Left - anchoCaja / 2, MARGEN, ANCHO_PANEL - MARGEN - anchoCaja);
                        var cy0 = Math.Max(ALTO_CABECERA, rc.Top - altoCaja - 6);
                        var rCaja = new RECT { Left = cx0, Top = cy0, Right = cx0 + anchoCaja, Bottom = cy0 + altoCaja };
                        var pincelCaja = CreateSolidBrush(Rgb(30, 41, 59)); FillRect(hdc, ref rCaja, pincelCaja); DeleteObject(pincelCaja);
                        var pincelBorde = CreateSolidBrush(Rgb(56, 189, 248));
                        var rBorde = new RECT { Left = rCaja.Left, Top = rCaja.Top, Right = rCaja.Right, Bottom = rCaja.Top + 2 }; FillRect(hdc, ref rBorde, pincelBorde); DeleteObject(pincelBorde);
                        SetTextColor(hdc, Rgb(241, 245, 249));
                        var rTexto = new RECT { Left = rCaja.Left + 8, Top = rCaja.Top + 8, Right = rCaja.Right - 8, Bottom = rCaja.Bottom - 6 };
                        DrawText(hdc, nota, -1, ref rTexto, DT_LEFT | DT_WORDBREAK | DT_END_ELLIPSIS);
                        SelectObject(hdc, anteriorNota); DeleteObject(fNota);
                    }
                }

                // LA CARTA DE LA JUGADA, grande, al pasar el ratón por ella (el
                // usuario, 2026-09-27): a su derecha, dentro del panel.
                if (bajoRaton == BAJO_JUGADA && extras?.JugadaFichero is { } ficheroJugada && grafico != IntPtr.Zero)
                {
                    var imgJugada = Imagen(ficheroJugada);
                    if (imgJugada != IntPtr.Zero)
                    {
                        var rj = rectJugada;
                        var jx = rj.Right + 8 + ANCHO_VISTA <= ANCHO_PANEL - MARGEN ? rj.Right + 8 : Math.Max(MARGEN, rj.Left - 8 - ANCHO_VISTA);
                        var jy = Math.Clamp(rj.Top + (rj.Bottom - rj.Top) / 2 - ALTO_VISTA / 2, ALTO_CABECERA, Math.Max(ALTO_CABECERA, alto - ALTO_PIE - MARGEN - ALTO_VISTA));
                        GdipDrawImageRectI(grafico, imgJugada, jx, jy, ANCHO_VISTA, ALTO_VISTA);
                    }
                }

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

                // Importar lleva su propio pie: guardar y revisar en la web.
                if (imp is not null) PintarPieImportacion(hdc, altoZona);
                else
                {
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
            }

            // Similares/combos/sinergias: pestañas, la carta de la que se parte y, si no hay cartas, por qué.
            if (rel is not null) PintarRelacionadas(hdc);

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
