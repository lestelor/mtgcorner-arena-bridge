using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace MtgCornerArenaBridge;

/// <summary>
/// UN AVISO ENCIMA DE ARENA, SIN TOCAR ARENA.
///
/// QUÉ ES Y QUÉ NO ES. Magic: The Gathering Arena no tiene extensiones ni nada
/// donde enganchar un mensaje, y meterse en su proceso para pintar dentro es
/// justo lo que no se va a hacer. Esto es lo que hacen los demás programas del
/// ramo: una ventana PROPIA, sin bordes y siempre encima, colocada sobre la
/// ventana del juego. Parece que esté dentro y no lo está; Arena no se entera
/// de que existe.
///
/// NO SE PUEDE PULSAR. La ventana lleva WS_EX_TRANSPARENT, así que el ratón la
/// atraviesa: si te sale justo encima de un botón, pulsas el botón. Un aviso
/// que se come un clic en mitad de una partida es peor que no avisar.
///
/// SALVO LAS ALERTAS FIJAS (`fijo`): el combo o la sinergia que ha enseñado el
/// rival se queda hasta que se cierra con su X, y al pasar el ratón por una
/// miniatura sale la carta en grande a su izquierda (pedido del usuario el
/// 2026-09-27: en 8 segundos no daba tiempo a leer las cartas). Esas sí se
/// pueden pulsar —si no, la X no serviría—, así que ocupan su rincón de verdad;
/// siguen sin robar el foco al juego (WM_MOUSEACTIVATE → MA_NOACTIVATE). La
/// carta grande es otra ventana, ésa sí transparente al ratón.
///
/// NI ROBA EL FOCO. Con WS_EX_NOACTIVATE aparece sin quitarle el teclado al
/// juego, que es lo que hace que una ventana emergente tire a alguien de una
/// partida.
///
/// EL LÍMITE, DICHO CLARO: en PANTALLA COMPLETA EXCLUSIVA no se ve. Ninguna
/// superposición se ve ahí, tampoco las de los demás; Windows le da la pantalla
/// entera al juego. Arena viene por defecto en ventana sin bordes, donde sí
/// funciona, pero quien la haya cambiado no verá nada y no es un fallo que se
/// pueda arreglar desde aquí.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUÉ WIN32 A PELO Y NO WINFORMS, que era como estaba escrito
///
/// Porque WinForms viene entero: el ejecutable pasaba de 34 a 68 MB por un
/// recuadro de seis segundos. Y desde que el programa se ACTUALIZA SOLO, ese
/// peso se lo descarga todo el mundo en cada versión. Recortarlo con el trimmer
/// no era opción —WinForms usa reflexión y el recorte lo rompe en ejecución—,
/// así que la ventana se crea a mano: registrar una clase, un bucle de
/// mensajes y pintar con GDI. Son las mismas cuatro cosas que hacía WinForms
/// por debajo, sin traerse el resto del framework.
/// </summary>
internal static class Superposicion
{
    // ── Lo que hace falta de Windows ──────────────────────────────────────

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public bool fErase;
        public RECT rcPaint;
        public bool fRestore, fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd; public uint message; public IntPtr wParam, lParam;
        public uint time; public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX clase);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string clase, string titulo, uint estilo,
        int x, int y, int ancho, int alto, IntPtr padre, IntPtr menu, IntPtr instancia, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int comando);
    [DllImport("user32.dll")] private static extern bool UpdateWindow(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int codigo);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr SetTimer(IntPtr hWnd, IntPtr id, uint ms, IntPtr fn);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint clave, byte alfa, uint banderas);
    [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT ps);
    [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] private static extern int FillRect(IntPtr hdc, ref RECT rc, IntPtr pincel);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DrawText(IntPtr hdc, string texto, int largo, ref RECT rc, uint formato);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rc);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hWnd, IntPtr region, bool repintar);
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint accion, uint param, ref RECT rc, uint win);
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr contexto);

    [DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr hWnd, IntPtr rc, bool borrar);
    [StructLayout(LayoutKind.Sequential)]
    private struct TRACKMOUSEEVENT { public uint cbSize, dwFlags; public IntPtr hwndTrack; public uint dwHoverTime; }
    [DllImport("user32.dll")] private static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT e);
    [DllImport("user32.dll")] private static extern IntPtr SetCursor(IntPtr cursor);
    [DllImport("user32.dll")] private static extern IntPtr LoadCursor(IntPtr instancia, int cursor);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int ancho, int alto);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr destino, int x, int y, int ancho, int alto, IntPtr origen, int xo, int yo, uint op);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint color);
    // GDI+ para las miniaturas de las cartas del aviso (ver PanelCartas.cs).
    private struct GdiplusStartupInput { public int GdiplusVersion; public IntPtr DebugEventCallback; public bool SuppressBackgroundThread, SuppressExternalCodecs; }
    [DllImport("gdiplus.dll")] private static extern int GdiplusStartup(out IntPtr token, ref GdiplusStartupInput entrada, IntPtr salida);
    [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)] private static extern int GdipLoadImageFromFile(string fichero, out IntPtr imagen);
    [DllImport("gdiplus.dll")] private static extern int GdipDisposeImage(IntPtr imagen);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr despues, int x, int y, int ancho, int alto, uint banderas);
    [DllImport("user32.dll")] private static extern bool KillTimer(IntPtr hWnd, IntPtr id);
    [StructLayout(LayoutKind.Sequential)] private struct PuntoG { public int X, Y; }
    [DllImport("gdiplus.dll")] private static extern int GdipSetSmoothingMode(IntPtr g, int modo);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateSolidFill(uint argb, out IntPtr brocha);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteBrush(IntPtr brocha);
    [DllImport("gdiplus.dll")] private static extern int GdipCreatePen1(uint argb, float ancho, int unidad, out IntPtr pluma);
    [DllImport("gdiplus.dll")] private static extern int GdipDeletePen(IntPtr pluma);
    [DllImport("gdiplus.dll")] private static extern int GdipCreatePath(int relleno, out IntPtr camino);
    [DllImport("gdiplus.dll")] private static extern int GdipDeletePath(IntPtr camino);
    [DllImport("gdiplus.dll")] private static extern int GdipAddPathArcI(IntPtr camino, int x, int y, int ancho, int alto, float inicio, float barrido);
    [DllImport("gdiplus.dll")] private static extern int GdipAddPathLineI(IntPtr camino, int x1, int y1, int x2, int y2);
    [DllImport("gdiplus.dll")] private static extern int GdipClosePathFigure(IntPtr camino);
    [DllImport("gdiplus.dll")] private static extern int GdipFillPath(IntPtr g, IntPtr brocha, IntPtr camino);
    [DllImport("gdiplus.dll")] private static extern int GdipDrawPath(IntPtr g, IntPtr pluma, IntPtr camino);
    [DllImport("gdiplus.dll")] private static extern int GdipGetImageWidth(IntPtr imagen, out uint ancho);
    [DllImport("gdiplus.dll")] private static extern int GdipGetImageHeight(IntPtr imagen, out uint alto);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateFromHDC(IntPtr hdc, out IntPtr grafico);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteGraphics(IntPtr grafico);
    [DllImport("gdiplus.dll")] private static extern int GdipSetInterpolationMode(IntPtr grafico, int modo);
    [DllImport("gdiplus.dll")] private static extern int GdipDrawImageRectI(IntPtr grafico, IntPtr imagen, int x, int y, int ancho, int alto);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int izq, int arriba, int der, int abajo, int anchoElipse, int altoElipse);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFont(int alto, int ancho, int escape, int orientacion, int grosor,
        uint cursiva, uint subrayado, uint tachado, uint juego, uint precision, uint recorte,
        uint calidad, uint paso, string cara);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr objeto);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr objeto);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr hdc, int modo);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr hdc, uint color);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? nombre);

    private const uint WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80;
    private const uint WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x8000000;
    private const uint WS_POPUP = 0x80000000;
    private const uint WM_DESTROY = 0x2, WM_CLOSE = 0x10, WM_PAINT = 0xF, WM_TIMER = 0x113;
    private const uint WM_MOUSEMOVE = 0x200, WM_LBUTTONUP = 0x202, WM_MOUSELEAVE = 0x2A3, WM_MOUSEACTIVATE = 0x21, WM_SETCURSOR = 0x20, WM_ERASEBKGND = 0x14;
    private const int MA_NOACTIVATE = 3, IDC_HAND = 32649, IDC_ARROW = 32512;
    private const uint TME_LEAVE = 0x2, SRCCOPY = 0x00CC0020;
    private const int SW_SHOWNOACTIVATE = 4;
    private const uint LWA_ALPHA = 0x2;
    private const int TRANSPARENT_BK = 1;
    private const uint DT_LEFT = 0x0, DT_CENTER = 0x1, DT_VCENTER = 0x4, DT_SINGLELINE = 0x20, DT_WORDBREAK = 0x10, DT_END_ELLIPSIS = 0x8000, DT_CALCRECT = 0x400;
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
    private const uint SPI_GETWORKAREA = 0x30;

    /// <summary>Un color de GDI: 0x00BBGGRR, al revés que en la web.</summary>
    private static uint Rgb(int r, int g, int b) => (uint)(r | (g << 8) | (b << 16));

    /// <summary>Ancho y alto del aviso, y el aire que deja contra el borde del juego.</summary>
    private const int ANCHO_BASE = 380, ALTO_TEXTO_MIN = 104, MARGEN = 28;
    /// <summary>
    /// LA FIGURA DEL ENTRENADOR a la izquierda, en los consejos (mulligan y cada
    /// turno): el que el usuario eligió en el resumen (pedido el 2026-10-03,
    /// «estaría gracioso que saliese la figura del entrenador»). Con ella, el
    /// aviso es más ancho y el texto empieza a su derecha.
    /// </summary>
    private const int ANCHO_AVATAR = 76;
    /*
     * CADA AVISO, SU ESTADO (2026-10-04). Desde que se apilan pueden estar varios
     * en pantalla, y cada uno vive en su propio hilo con su propia ventana y su
     * propio bucle de mensajes: todo lo de abajo que describe UN aviso —tamaño,
     * textos, figura, miniaturas, la X, la animación— es [ThreadStatic], así
     * que cada ventana ve el suyo. Correr lo inicializa entero al empezar (los
     * valores de las declaraciones sólo valen para el primer hilo).
     */
    [ThreadStatic] private static int ANCHO;
    /// <summary>Dónde empieza el texto: 18, o a la derecha de la figura.</summary>
    [ThreadStatic] private static int izq;
    [ThreadStatic] private static string? avatarFichero;
    [ThreadStatic] private static IntPtr avatarImg;

    /// <summary>
    /// EL ENTRENADOR DE TODOS LOS AVISOS (el usuario, 2026-10-03: «que salga
    /// siempre el muñeco en esa ventana», «y quizás se puede hacer más
    /// divertida»): su figura, su nombre y su color. Con él, el aviso es su
    /// bocadillo de cómic: el texto en un globo con el pico hacia él, su color
    /// en el borde y «EL SARGENTO DICE» arriba, entra deslizándose y él da
    /// unos botes como si hablara. Sin figura (aún no ha bajado), el aviso de
    /// siempre. Lo pone Program.
    /// </summary>
    public static Func<(string? Figura, string Nombre, (int R, int G, int B) Color)>? Entrenador { get; set; }
    [ThreadStatic] private static string? nombreEntrenador;
    [ThreadStatic] private static uint colorEntrenador;
    [ThreadStatic] private static (int R, int G, int B) colorEntrenadorRgb;

    /// <summary>La entrada: cuándo empezó y cuánto se ha movido la figura (adónde va lo dice la pila).</summary>
    [ThreadStatic] private static System.Diagnostics.Stopwatch? reloj;
    [ThreadStatic] private static int saltoFigura;
    private const int DESLIZ = 70, MS_ENTRADA = 260, MS_BOTES = 1500;
    /// <summary>
    /// LO QUE MIDE LA PARTE DE TEXTO: lo que pida el cuerpo, entre las dos
    /// líneas de siempre y unas ocho. Era fijo y el consejo de cada turno, que
    /// dice jugada, ataque y por qué, se cortaba a media frase (2026-10-03).
    /// </summary>
    [ThreadStatic] private static int ALTO_TEXTO;
    /// <summary>Las miniaturas: cartas pequeñas en fila bajo el texto (pedido del usuario el 2026-09-26: «no hace falta que sean muy grandes»).</summary>
    private const int ANCHO_MINI = 70, ALTO_MINI = 98, AIRE_MINI = 8, MAX_MINIS = 4;
    /// <summary>El alto del aviso que hay en pantalla: el del texto, y las miniaturas si las lleva.</summary>
    [ThreadStatic] private static int ALTO;
    [ThreadStatic] private static string[]? ficherosMini;
    [ThreadStatic] private static List<IntPtr>? minis;
    /// <summary>El arranque de GDI+ es del proceso, uno para todos los avisos (ver ArrancarGdiplus).</summary>
    private static IntPtr gdiplus;
    private static readonly object cerrojoGdiplus = new();

    /// <summary>GDI+ se arranca una vez por proceso; con varios avisos a la vez, dos hilos podían hacerlo a la par.</summary>
    private static void ArrancarGdiplus()
    {
        lock (cerrojoGdiplus)
        {
            if (gdiplus != IntPtr.Zero) return;
            var entrada = new GdiplusStartupInput { GdiplusVersion = 1 };
            GdiplusStartup(out gdiplus, ref entrada, IntPtr.Zero);
        }
    }
    /// <summary>La alerta en pantalla es fija: se queda hasta su X y se puede pulsar.</summary>
    [ThreadStatic] private static bool esFijo;
    /// <summary>
    /// EL PRIMERO DE LA PILA HABLA CON FIGURA; LOS DEMÁS SON SUS BOCADILLOS
    /// (el usuario, 2026-10-04: «en dos bocadillos diferentes, como dos mensajes
    /// del coach»). Con el entrenador, sólo el de arriba lleva figura, banda,
    /// «EL SARGENTO DICE» y pico; los de debajo son el globo solo, alineado con
    /// el suyo, y lo de alrededor deja ver el juego (ver AplicarForma). Cambia
    /// en vivo: si se cierra el primero, el siguiente pasa a llevar la figura
    /// (la pila se lo dice con WM_PRIMERA, cada ventana vive en su hilo).
    /// </summary>
    [ThreadStatic] private static bool esPrimera;
    private const uint WM_PRIMERA = 0x8001; // WM_APP + 1
    /// <summary>
    /// LAS DOS ALTURAS de la parte de texto: como primero (con sitio para la
    /// figura) y de seguida (sólo su texto, y sin la línea de «… DICE», que no
    /// repite: por eso su texto sube SUBE_SEGUIDA). La pila coloca con la que
    /// toca y, al cambiar de papel, la ventana se redimensiona (AjustarAlto).
    /// </summary>
    [ThreadStatic] private static int altoTextoPrimera, altoTextoSeguida;
    private const int SUBE_SEGUIDA = 16;
    /// <summary>
    /// CON SU X: las alertas fijas y, desde el 2026-10-03, los consejos (los
    /// avisos importantes: mulligan y cada turno). «Que las ventanas de
    /// consejos tengan una X para cerrarlas» (el usuario). Los consejos siguen
    /// yéndose solos a su tiempo; la X es para quitarlos antes.
    /// </summary>
    [ThreadStatic] private static bool conCierre;
    /// <summary>Qué hay bajo el ratón: la X (-2), la miniatura i (0..n) o nada (-1).</summary>
    [ThreadStatic] private static int bajoRaton;
    private const int BAJO_X = -2, LADO_X = 28;
    /// <summary>La carta en grande al pasar por una miniatura: la mitad de la imagen «normal» de Scryfall (488×680), así sale nítida.</summary>
    private const int ANCHO_ZOOM = 244, ALTO_ZOOM = 340;
    [ThreadStatic] private static IntPtr ventanaZoom;
    [ThreadStatic] private static int miniZoom;

    // El procedimiento de ventana se guarda en un campo estático A PROPÓSITO:
    // Windows se queda con su puntero, y si el recolector se llevara el
    // delegado, el primer mensaje que llegara saltaría a memoria liberada.
    private static WndProc? procedimiento;
    [ThreadStatic] private static string? textoTitulo, textoCuerpo;

    /// <summary>La ventana principal de Arena, o cero si no está abierto.</summary>
    private static IntPtr VentanaDeArena()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("MTGA"))
            {
                using (p)
                {
                    if (p.MainWindowHandle != IntPtr.Zero) return p.MainWindowHandle;
                }
            }
        }
        catch { /* sin permisos para listar procesos: se usa la pantalla */ }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Enseña el aviso y VUELVE CUANDO SE HA IDO.
    ///
    /// Bloquea a propósito: esto se llama al final del trabajo, y si no
    /// esperase, el programa terminaría y se llevaría por delante la ventana
    /// antes de que nadie la leyera. Cualquier fallo se traga: un aviso que no
    /// sale no puede estropear una importación que ya salió bien.
    /// </summary>
    public static void Mostrar(string titulo, string texto, int segundos = 6, IReadOnlyList<string>? imagenes = null, bool fijo = false, string? avatar = null)
    {
        try
        {
            var hilo = MostrarSinEsperar(titulo, texto, segundos, imagenes, fijo, avatar: avatar);
            // Una fija dura lo que tarde alguien en cerrarla.
            if (fijo) hilo?.Join(); else hilo?.Join(TimeSpan.FromSeconds(segundos + 3));
        }
        catch { /* ni con esas: el programa sigue igual */ }
    }

    /// <summary>
    /// El mismo aviso, pero SIN esperar a que se apague: para quien tiene
    /// trabajo que hacer mientras se lee («Subiendo…» y a subir). Devuelve el
    /// hilo, por si alguien quiere esperarlo igual.
    ///
    /// APILADOS, NO UNO SOLO (el usuario, 2026-10-04: «si hay varios avisos que
    /// se solapan deberían aparecer unos debajo de los otros»). Antes cada aviso
    /// cerraba el anterior, y con el consejo de cada turno en pantalla los demás
    /// se DESCARTABAN —los de combos del rival se marcaban como avisados y no
    /// volvían a salir—. Ahora van en una pila arriba a la derecha (ver
    /// Recolocar), y sólo se sustituyen los de la misma RANURA:
    ///  · el consejo (`importante`: mulligan y cada turno), al anterior consejo;
    ///  · los informativos («subiendo…», «hecho»), al informativo anterior: un
    ///    «hecho» tapando su propio «subiendo» era justo lo que se quería;
    ///  · las alertas fijas (combos y sinergias del rival) no sustituyen a nadie:
    ///    se quedan, cada una con su X.
    /// </summary>
    public static Thread? MostrarSinEsperar(string titulo, string texto, int segundos = 6, IReadOnlyList<string>? imagenes = null, bool fijo = false, bool importante = false, string? avatar = null)
    {
        try
        {
            var ranura = importante ? RANURA_CONSEJO : fijo ? null : RANURA_INFO;
            if (ranura is not null) CerrarRanura(ranura);
            var ficheros = (imagenes ?? []).Where(f => !string.IsNullOrEmpty(f) && File.Exists(f)).Take(MAX_MINIS).ToArray();
            var ent = Entrenador?.Invoke();
            var figura = avatar ?? ent?.Figura;
            var conAvatar = figura is { Length: > 0 } && File.Exists(figura) ? figura : null;
            var quien = ent is { } e ? (e.Nombre, e.Color) : ("", (245, 158, 11));
            var hilo = new Thread(() => { try { Correr(titulo, texto, segundos, ficheros, fijo, conAvatar, importante, ranura, quien); } catch (Exception ex) { Console.Error.WriteLine($"[aviso] {ex.GetType().Name}: {ex.Message}"); } });
            hilo.IsBackground = true;
            hilo.Start();
            return hilo;
        }
        catch { return null; }
    }

    /// <summary>Quita el aviso informativo que haya («subiendo…»), si hay alguno. Desde cualquier hilo.</summary>
    public static void Ocultar() => CerrarRanura(RANURA_INFO);

    // ── LA PILA ────────────────────────────────────────────────────────────

    private const string RANURA_CONSEJO = "consejo", RANURA_INFO = "info";
    /// <summary>Entre aviso y aviso, y cuántos caben: más de cuatro ya tapan medio juego.</summary>
    private const int HUECO_PILA = 10, MAX_PILA = 4;

    /// <summary>Un aviso en pantalla: lo que hace falta para colocarlo en la pila.</summary>
    private sealed class Apilado
    {
        public IntPtr Ventana;
        public string? Ranura;
        public bool Importante;
        public long Orden;
        public int Ancho, AltoPrimera, AltoSeguida, X, Y;
        /// <summary>Mientras entra deslizándose se coloca él solo (su reloj de fotogramas).</summary>
        public bool Animando;
        /// <summary>Lleva entrenador (bocadillo): entre dos así no hace falta hueco, ya tienen su margen.</summary>
        public bool ConFigura;
        /// <summary>El de arriba del todo: el único que pinta la figura (ver esPrimera).</summary>
        public bool Primera;
    }
    private static readonly List<Apilado> pila = new();
    private static long ordenPila;

    /// <summary>Cierra el aviso de esa ranura, si hay. Sale de la pila YA: el nuevo se coloca sin su hueco.</summary>
    private static void CerrarRanura(string ranura)
    {
        List<IntPtr> cerrar;
        lock (pila)
        {
            var fuera = pila.Where(a => a.Ranura == ranura).ToList();
            foreach (var a in fuera) pila.Remove(a);
            cerrar = fuera.Select(a => a.Ventana).Where(v => v != IntPtr.Zero).ToList();
        }
        foreach (var v in cerrar) PostMessage(v, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>
    /// COLOCA LA PILA: arriba a la derecha del juego (o de la pantalla), el
    /// consejo primero y los demás debajo por orden de llegada, cada uno pegado
    /// al borde derecho. Al cerrarse uno, los de debajo suben. A las ventanas
    /// de otros hilos se las mueve sin esperarlas (SWP_ASYNCWINDOWPOS): esperar
    /// a otro hilo de aviso desde éste podría trabar a los dos.
    /// </summary>
    private static void Recolocar()
    {
        var (derecha, arriba) = Esquina();
        var mover = new List<Apilado>();
        var cambianDePapel = new List<(IntPtr Ventana, bool Primera)>();
        lock (pila)
        {
            var y = arriba;
            Apilado? anterior = null;
            foreach (var a in pila.OrderBy(a => a.Importante ? 0 : 1).ThenBy(a => a.Orden))
            {
                // Dos bocadillos seguidos se separan con su propio margen; con un aviso sin figura, aire de por medio.
                if (anterior is not null) y += anterior.ConFigura && a.ConFigura ? 0 : HUECO_PILA;
                var x = derecha - a.Ancho;
                if (a.X != x || a.Y != y) { a.X = x; a.Y = y; if (!a.Animando && a.Ventana != IntPtr.Zero) mover.Add(a); }
                var primera = anterior is null;
                if (a.Primera != primera) { a.Primera = primera; if (a.Ventana != IntPtr.Zero) cambianDePapel.Add((a.Ventana, primera)); }
                y += a.Primera || !a.ConFigura ? a.AltoPrimera : a.AltoSeguida;
                anterior = a;
            }
        }
        foreach (var a in mover)
            SetWindowPos(a.Ventana, IntPtr.Zero, a.X, a.Y, 0, 0, 0x1 /* NOSIZE */ | 0x4 /* NOZORDER */ | 0x10 /* NOACTIVATE */ | 0x4000 /* ASYNCWINDOWPOS */);
        foreach (var (v, primera) in cambianDePapel) PostMessage(v, WM_PRIMERA, new IntPtr(primera ? 1 : 0), IntPtr.Zero);
    }

    /// <summary>La parte de texto y el total, según sea el primero o uno de seguida.</summary>
    private static void AjustarAlto()
    {
        ALTO_TEXTO = DeSeguida ? altoTextoSeguida : altoTextoPrimera;
        ALTO = ALTO_TEXTO + (ficherosMini is { Length: > 0 } ? ALTO_MINI + AIRE_MINI : 0);
    }

    /// <summary>¿Este aviso se pinta como bocadillo de seguida (sin figura, sólo el globo)?</summary>
    private static bool DeSeguida => avatarFichero is not null && !esPrimera;

    /// <summary>
    /// La forma de la ventana: el rectángulo redondeado de siempre o, si es un
    /// bocadillo de seguida, sólo el globo (las miniaturas incluidas): lo demás
    /// no se pinta y deja ver el juego, y el ratón pasa por ahí al juego.
    /// </summary>
    private static void AplicarForma(IntPtr ventana)
    {
        // El globo va de (izq-10, 8) a (ANCHO-12, ALTO_TEXTO-8) (ver PintarBocadillo):
        // un píxel más por cada lado, para no cortar la mitad de fuera de su borde.
        var abajo = ficherosMini is { Length: > 0 } ? ALTO - 3 : ALTO_TEXTO - 6;
        var forma = DeSeguida
            ? CreateRoundRectRgn(izq - 11, 7, ANCHO - 10, abajo, 26, 26)
            : CreateRoundRectRgn(0, 0, ANCHO + 1, ALTO + 1, 18, 18);
        SetWindowRgn(ventana, forma, true);
    }

    private static Apilado? EnPila(IntPtr ventana)
    {
        lock (pila) return pila.FirstOrDefault(a => a.Ventana == ventana);
    }

    private static void Correr(string titulo, string texto, int segundos, string[] ficheros, bool fijo, string? avatar, bool importante, string? ranura, (string Nombre, (int R, int G, int B) Color) quien)
    {
        // EN PÍXELES DE VERDAD. `GetWindowRect` devuelve píxeles físicos; sin
        // declararse consciente del DPI, Windows virtualiza las coordenadas y
        // en una pantalla al 150% el aviso saldría desplazado respecto a Arena,
        // que es justo lo único que tiene que hacer bien.
        try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } // PER_MONITOR_AWARE_V2
        catch { /* en Windows viejos no existe: se pierde precisión, nada más */ }

        textoTitulo = titulo;
        textoCuerpo = texto;
        ficherosMini = ficheros;
        minis = new();
        reloj = new();
        ventanaZoom = IntPtr.Zero;
        nombreEntrenador = quien.Nombre;
        colorEntrenadorRgb = quien.Color;
        colorEntrenador = Rgb(quien.Color.R, quien.Color.G, quien.Color.B);
        esFijo = fijo;
        // TODOS CON SU X (el usuario, 2026-10-04: «hay uno que no tiene la x de
        // cerrar»). Los informativos la tenían sin ella para dejar pasar el clic
        // al juego; apilados con los demás, que se vayan cuando uno quiera.
        conCierre = true;
        bajoRaton = -1;
        miniZoom = -1;
        avatarFichero = avatar;
        avatarImg = IntPtr.Zero;
        ANCHO = ANCHO_BASE + (avatar is null ? 0 : ANCHO_AVATAR + 12);
        izq = avatar is null ? 18 : 18 + ANCHO_AVATAR + 12;
        var alto = AltoDelCuerpo(texto);
        // Con la figura, al menos lo que ella mide (retratos: algo más altos que anchos).
        altoTextoPrimera = avatar is null ? alto : Math.Max(alto, 14 + ANCHO_AVATAR * 4 / 3 + 14);
        altoTextoSeguida = avatar is null ? alto : alto - SUBE_SEGUIDA;
        esPrimera = true;
        AjustarAlto();
        procedimiento = Procedimiento;

        var instancia = GetModuleHandle(null);
        var clase = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(procedimiento),
            hInstance = instancia,
            lpszClassName = "MtgCornerAviso",
        };
        if (RegisterClassEx(ref clase) == 0) { var err = Marshal.GetLastWin32Error(); if (err != 1410 /* ERROR_CLASS_ALREADY_EXISTS */) Console.Error.WriteLine($"[aviso] RegisterClassEx falló: error {err}"); }

        // Su sitio en la pila: se apunta antes de crear la ventana para nacer ya
        // donde le toca (y empujar a los de debajo si es un consejo).
        var animada = avatar is not null;
        var extraMinis = ficheros.Length > 0 ? ALTO_MINI + AIRE_MINI : 0;
        var yo = new Apilado
        {
            Ranura = ranura, Importante = importante, Ancho = ANCHO, Animando = animada, ConFigura = avatar is not null,
            AltoPrimera = altoTextoPrimera + extraMinis, AltoSeguida = altoTextoSeguida + extraMinis,
        };
        List<IntPtr> sobran;
        lock (pila)
        {
            yo.Orden = ++ordenPila;
            pila.Add(yo);
            // Más de los que caben: fuera los más antiguos que no sean un consejo.
            var fuera = pila.Where(a => a != yo && !a.Importante).OrderBy(a => a.Orden).Take(Math.Max(0, pila.Count - MAX_PILA)).ToList();
            foreach (var a in fuera) pila.Remove(a);
            sobran = fuera.Select(a => a.Ventana).Where(v => v != IntPtr.Zero).ToList();
        }
        foreach (var v in sobran) PostMessage(v, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        Recolocar();
        var x = yo.X;
        var y = yo.Y;
        lock (pila) esPrimera = yo.Primera;
        AjustarAlto();
        // Con figura, entra deslizándose desde un poco más a la derecha.
        if (animada) x += DESLIZ;
        var ventana = CreateWindowEx(
            // La fija no lleva WS_EX_TRANSPARENT: tiene que recibir el ratón (la X y el zoom).
            WS_EX_TOPMOST | (conCierre ? 0 : WS_EX_TRANSPARENT) | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE,
            "MtgCornerAviso", "MTG Corner", WS_POPUP,
            x, y, ANCHO, ALTO, IntPtr.Zero, IntPtr.Zero, instancia, IntPtr.Zero);
        if (ventana == IntPtr.Zero)
        {
            Console.Error.WriteLine($"[aviso] CreateWindowEx falló: error {Marshal.GetLastWin32Error()}");
            lock (pila) pila.Remove(yo);
            Recolocar();
            return;
        }
        lock (pila) yo.Ventana = ventana;

        // Esquinas redondeadas y un punto de transparencia, para que no parezca
        // un cuadro de diálogo de 1998 (o sólo el globo, si es un bocadillo de seguida).
        AplicarForma(ventana);
        SetLayeredWindowAttributes(ventana, 0, (byte)(animada ? 0 : 240), LWA_ALPHA);

        if (!fijo) SetTimer(ventana, new IntPtr(1), (uint)Math.Max(1, segundos) * 1000, IntPtr.Zero);
        // La animación: otro reloj, de fotogramas (el 1 es el que cierra el aviso).
        saltoFigura = 0;
        if (animada) { reloj.Restart(); SetTimer(ventana, new IntPtr(2), 16, IntPtr.Zero); }
        ShowWindow(ventana, SW_SHOWNOACTIVATE);   // sin robarle el foco al juego
        UpdateWindow(ventana);

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    private static IntPtr Procedimiento(IntPtr ventana, uint mensaje, IntPtr wParam, IntPtr lParam)
    {
        // LA CARTA GRANDE es otra ventana de la misma clase: sólo se pinta y se va.
        if (ventana == ventanaZoom && ventanaZoom != IntPtr.Zero)
        {
            switch (mensaje)
            {
                case WM_PAINT: PintarZoom(ventana); return IntPtr.Zero;
                case WM_ERASEBKGND: return new IntPtr(1);
                case WM_DESTROY: ventanaZoom = IntPtr.Zero; return IntPtr.Zero;
            }
            return DefWindowProc(ventana, mensaje, wParam, lParam);
        }
        switch (mensaje)
        {
            case WM_PAINT:
                Pintar(ventana);
                return IntPtr.Zero;

            case WM_PRIMERA:
                esPrimera = wParam != IntPtr.Zero;
                AjustarAlto();
                SetWindowPos(ventana, IntPtr.Zero, 0, 0, ANCHO, ALTO, 0x2 /* NOMOVE */ | 0x4 /* NOZORDER */ | 0x10 /* NOACTIVATE */);
                AplicarForma(ventana);
                InvalidateRect(ventana, IntPtr.Zero, false);
                return IntPtr.Zero;

            case WM_ERASEBKGND:
                return new IntPtr(1);   // lo pinta todo Pintar, de una vez: sin parpadeo

            // Pulsar la alerta no saca a nadie de la partida.
            case WM_MOUSEACTIVATE:
                return new IntPtr(MA_NOACTIVATE);

            case WM_SETCURSOR:
                SetCursor(LoadCursor(IntPtr.Zero, bajoRaton == BAJO_X ? IDC_HAND : IDC_ARROW));
                return new IntPtr(1);

            case WM_MOUSEMOVE:
            {
                var i = QueHayEn(lParam);
                if (i != bajoRaton) { bajoRaton = i; InvalidateRect(ventana, IntPtr.Zero, false); Zoom(ventana, i); }
                var t = new TRACKMOUSEEVENT { cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(), dwFlags = TME_LEAVE, hwndTrack = ventana };
                TrackMouseEvent(ref t);
                return IntPtr.Zero;
            }

            case WM_MOUSELEAVE:
                bajoRaton = -1;
                InvalidateRect(ventana, IntPtr.Zero, false);
                Zoom(ventana, -1);
                return IntPtr.Zero;

            case WM_LBUTTONUP:
                if (conCierre && QueHayEn(lParam) == BAJO_X) PostMessage(ventana, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                return IntPtr.Zero;

            case WM_TIMER when wParam.ToInt64() == 2:
            {
                // LA ENTRADA: se desliza a su sitio y aparece (curva que frena al
                // final); luego la figura da unos botes, como quien habla.
                var ms = reloj!.ElapsedMilliseconds;
                var p = Math.Min(1.0, ms / (double)MS_ENTRADA);
                var e = 1 - Math.Pow(1 - p, 3);
                // Adonde diga la pila AHORA: si mientras entra se cierra otro, sube con ella.
                var sitio = EnPila(ventana);
                if (sitio is not null && p < 1)
                    SetWindowPos(ventana, IntPtr.Zero, sitio.X + (int)Math.Round((1 - e) * DESLIZ), sitio.Y, 0, 0, 0x1 /* NOSIZE */ | 0x4 /* NOZORDER */ | 0x10 /* NOACTIVATE */);
                else if (sitio is not null && sitio.Animando)
                {
                    // Llegó: desde ahora lo mueve Recolocar.
                    lock (pila) sitio.Animando = false;
                    SetWindowPos(ventana, IntPtr.Zero, sitio.X, sitio.Y, 0, 0, 0x1 /* NOSIZE */ | 0x4 /* NOZORDER */ | 0x10 /* NOACTIVATE */);
                }
                SetLayeredWindowAttributes(ventana, 0, (byte)Math.Round(240 * e), LWA_ALPHA);
                var salto = ms < MS_BOTES ? (int)Math.Round(5 * Math.Abs(Math.Sin(ms / 1000.0 * Math.PI * 5))) : 0;
                if (salto != saltoFigura) { saltoFigura = salto; InvalidateRect(ventana, IntPtr.Zero, false); }
                if (ms >= MS_BOTES && p >= 1) KillTimer(ventana, new IntPtr(2));
                return IntPtr.Zero;
            }
            case WM_TIMER:
            case WM_CLOSE:
                if (ventanaZoom != IntPtr.Zero) DestroyWindow(ventanaZoom);
                DestroyWindow(ventana);
                return IntPtr.Zero;

            case WM_DESTROY:
                // Fuera de la pila, y los de debajo suben a su hueco.
                lock (pila) pila.RemoveAll(a => a.Ventana == ventana);
                Recolocar();
                foreach (var m in minis!) if (m != IntPtr.Zero) GdipDisposeImage(m);
                minis.Clear();
                if (avatarImg != IntPtr.Zero) { GdipDisposeImage(avatarImg); avatarImg = IntPtr.Zero; }
                PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return DefWindowProc(ventana, mensaje, wParam, lParam);
    }

    /// <summary>Dónde está la X, en la ventana: arriba a la derecha; con el bocadillo, dentro de él (su borde acaba a 12 del filo).</summary>
    private static RECT RectX() => avatarFichero is not null
        ? new() { Left = ANCHO - LADO_X - 18, Top = 12, Right = ANCHO - 18, Bottom = 12 + LADO_X }
        : new() { Left = ANCHO - LADO_X - 8, Top = 8, Right = ANCHO - 8, Bottom = 8 + LADO_X };

    /// <summary>Dónde está la miniatura i, en la ventana.</summary>
    /// <summary>
    /// EL BOCADILLO: un globo redondeado detrás del título y del texto, con el
    /// borde del color del entrenador y un pico que apunta a su cara.
    /// </summary>
    private static void PintarBocadillo(IntPtr hdc, bool conPico = true)
    {
        ArrancarGdiplus();
        if (GdipCreateFromHDC(hdc, out var g) != 0 || g == IntPtr.Zero) return;
        GdipSetSmoothingMode(g, 4);
        int x = izq - 10, y = 8, w = ANCHO - 12 - x, h = ALTO_TEXTO - 8 - y, r = 12, d = r * 2;
        int picoY = 30, picoX = x - 12;
        GdipCreatePath(0, out var c);
        GdipAddPathArcI(c, x, y, d, d, 180, 90);
        GdipAddPathArcI(c, x + w - d, y, d, d, 270, 90);
        GdipAddPathArcI(c, x + w - d, y + h - d, d, d, 0, 90);
        GdipAddPathArcI(c, x, y + h - d, d, d, 90, 90);
        // El pico, en el lado izquierdo, hacia la figura (un bocadillo de seguida no lo lleva).
        if (conPico)
        {
            GdipAddPathLineI(c, x, y + h - r, x, picoY + 14);
            GdipAddPathLineI(c, x, picoY + 14, picoX, picoY + 4);
            GdipAddPathLineI(c, picoX, picoY + 4, x, picoY);
        }
        GdipClosePathFigure(c);
        var (cr, cg, cb) = colorEntrenadorRgb;
        GdipCreateSolidFill(0xFF141C2E, out var relleno);
        GdipFillPath(g, relleno, c);
        GdipCreatePen1((uint)((255 << 24) | (cr << 16) | (cg << 8) | cb), 1.6f, 2, out var pluma);
        GdipDrawPath(g, pluma, c);
        GdipDeleteBrush(relleno); GdipDeletePen(pluma); GdipDeletePath(c); GdipDeleteGraphics(g);
    }

    /// <summary>El alto de la parte de texto para este cuerpo, medido con la letra con que se pinta.</summary>
    private static int AltoDelCuerpo(string cuerpo)
    {
        var hdc = GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero) return ALTO_TEXTO_MIN;
        var fuente = CreateFont(17, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        var anterior = SelectObject(hdc, fuente);
        try
        {
            var r = new RECT { Left = 0, Top = 0, Right = ANCHO - izq - 26, Bottom = 0 };
            DrawText(hdc, cuerpo, -1, ref r, DT_LEFT | DT_WORDBREAK | DT_CALCRECT);
            return Math.Clamp(58 + (r.Bottom - r.Top) + 12, ALTO_TEXTO_MIN, 58 + 8 * 23 + 12);
        }
        finally { SelectObject(hdc, anterior); DeleteObject(fuente); ReleaseDC(IntPtr.Zero, hdc); }
    }

    private static RECT RectMini(int i) => new() { Left = izq + i * (ANCHO_MINI + AIRE_MINI), Top = ALTO_TEXTO - 4, Right = izq + i * (ANCHO_MINI + AIRE_MINI) + ANCHO_MINI, Bottom = ALTO_TEXTO - 4 + ALTO_MINI };

    /// <summary>Qué hay bajo un punto de la ventana: la X, una miniatura o nada. Sólo en las fijas: las otras no ven el ratón.</summary>
    private static int QueHayEn(IntPtr lParam)
    {
        if (!conCierre) return -1;
        var x = (short)(lParam.ToInt64() & 0xFFFF);
        var y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        bool Dentro(RECT r) => x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;
        if (Dentro(RectX())) return BAJO_X;
        for (var i = 0; i < ficherosMini.Length; i++) if (Dentro(RectMini(i))) return i;
        return -1;
    }

    /// <summary>
    /// La carta en grande de la miniatura i (o ninguna con -1): una ventana
    /// aparte a la IZQUIERDA de la alerta —que está pegada a la derecha de
    /// Arena, así que a la izquierda siempre hay sitio—, a la altura de la
    /// miniatura. Transparente al ratón y sin foco, como el aviso de siempre.
    /// </summary>
    private static void Zoom(IntPtr ventana, int i)
    {
        if (i == miniZoom) return;
        miniZoom = i;
        // Se destruye SIN soltar antes `ventanaZoom`: su WM_DESTROY tiene que caer
        // en la rama de la carta grande, no en la de la alerta (que cerraría todo).
        if (ventanaZoom != IntPtr.Zero) DestroyWindow(ventanaZoom);
        if (i < 0 || i >= minis.Count || minis[i] == IntPtr.Zero || !GetWindowRect(ventana, out var r)) return;
        var rm = RectMini(i);
        var zx = r.Left - 10 - ANCHO_ZOOM;
        var zy = Math.Max(r.Top, r.Top + rm.Top + (rm.Bottom - rm.Top) / 2 - ALTO_ZOOM / 2);
        var z = CreateWindowEx(WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE,
            "MtgCornerAviso", "MTG Corner", WS_POPUP, zx, zy, ANCHO_ZOOM, ALTO_ZOOM, IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
        if (z == IntPtr.Zero) return;
        ventanaZoom = z;
        SetWindowRgn(z, CreateRoundRectRgn(0, 0, ANCHO_ZOOM + 1, ALTO_ZOOM + 1, 16, 16), true);
        SetLayeredWindowAttributes(z, 0, 255, LWA_ALPHA);
        ShowWindow(z, SW_SHOWNOACTIVATE);
        UpdateWindow(z);
    }

    private static void PintarZoom(IntPtr ventana)
    {
        var hdc = BeginPaint(ventana, out var ps);
        try
        {
            var fondo = CreateSolidBrush(Rgb(9, 13, 24));
            var todo = new RECT { Left = 0, Top = 0, Right = ANCHO_ZOOM, Bottom = ALTO_ZOOM };
            FillRect(hdc, ref todo, fondo);
            DeleteObject(fondo);
            var i = miniZoom;
            if (i >= 0 && i < minis.Count && minis[i] != IntPtr.Zero && GdipCreateFromHDC(hdc, out var g) == 0 && g != IntPtr.Zero)
            {
                GdipSetInterpolationMode(g, 7);
                GdipDrawImageRectI(g, minis[i], 0, 0, ANCHO_ZOOM, ALTO_ZOOM);
                GdipDeleteGraphics(g);
            }
        }
        finally { EndPaint(ventana, ref ps); }
    }

    /// <summary>
    /// El recuadro: fondo oscuro, filo ámbar a la izquierda como las tarjetas de
    /// la web, la marca arriba y las dos líneas del mensaje. En un lienzo aparte
    /// y volcado de una vez, que el paso del ratón lo repinta.
    /// </summary>
    private static void Pintar(IntPtr ventana)
    {
        var hdcVentana = BeginPaint(ventana, out var ps);
        var hdc = CreateCompatibleDC(hdcVentana);
        var lienzo = CreateCompatibleBitmap(hdcVentana, ANCHO, ALTO);
        var lienzoAnterior = SelectObject(hdc, lienzo);
        var fondo = CreateSolidBrush(Rgb(9, 13, 24));
        var filo = CreateSolidBrush(Rgb(245, 158, 11));
        var marca = CreateFont(13, 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        var fuerte = CreateFont(20, 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        var normal = CreateFont(17, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        try
        {
            var todo = new RECT { Left = 0, Top = 0, Right = ANCHO, Bottom = ALTO };
            FillRect(hdc, ref todo, fondo);
            var banda = new RECT { Left = 0, Top = 0, Right = 4, Bottom = ALTO };
            if (DeSeguida) { /* sin banda: es sólo el globo */ }
            else if (avatarFichero is not null) { var pincelBanda = CreateSolidBrush(colorEntrenador); FillRect(hdc, ref banda, pincelBanda); DeleteObject(pincelBanda); }
            else FillRect(hdc, ref banda, filo);
            if (avatarFichero is not null) PintarBocadillo(hdc, conPico: !DeSeguida);

            SetBkMode(hdc, TRANSPARENT_BK);

            SelectObject(hdc, marca);
            SetTextColor(hdc, Rgb(252, 211, 77));
            var rMarca = new RECT { Left = izq, Top = 12, Right = ANCHO - 18, Bottom = 28 };
            // Con el entrenador, quién habla y en su color; sin él, la marca.
            if (avatarFichero is not null && nombreEntrenador is { Length: > 0 }) SetTextColor(hdc, colorEntrenador);
            // De seguida no repite quién habla: es el mismo entrenador de arriba.
            if (!DeSeguida)
                DrawText(hdc, avatarFichero is not null && nombreEntrenador is { Length: > 0 } ? Textos.T("aviso_dice", nombreEntrenador).ToUpperInvariant() : "MTG CORNER", -1, ref rMarca, DT_LEFT | DT_SINGLELINE);

            SelectObject(hdc, fuerte);
            SetTextColor(hdc, Rgb(255, 255, 255));
            var sube = DeSeguida ? SUBE_SEGUIDA : 0;
            var rTitulo = new RECT { Left = izq, Top = 30 - sube, Right = conCierre ? RectX().Left - 6 : ANCHO - 18, Bottom = 56 - sube };
            DrawText(hdc, textoTitulo, -1, ref rTitulo, DT_LEFT | DT_SINGLELINE | DT_END_ELLIPSIS);

            // LA X de las fijas y de los consejos, arriba a la derecha; se ilumina al pasar.
            if (conCierre)
            {
                var rx = RectX();
                if (bajoRaton == BAJO_X) { var resalte = CreateSolidBrush(Rgb(51, 65, 85)); FillRect(hdc, ref rx, resalte); DeleteObject(resalte); }
                var fx = CreateFont(15, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe MDL2 Assets");
                SelectObject(hdc, fx);
                SetTextColor(hdc, bajoRaton == BAJO_X ? Rgb(255, 255, 255) : Rgb(186, 196, 214));
                DrawText(hdc, "\uE711", -1, ref rx, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
                DeleteObject(fx);
            }

            SelectObject(hdc, normal);
            SetTextColor(hdc, Rgb(203, 213, 225));
            var rCuerpo = new RECT { Left = izq, Top = 58 - sube, Right = ANCHO - 26, Bottom = ALTO_TEXTO - 10 };
            DrawText(hdc, textoCuerpo, -1, ref rCuerpo, DT_LEFT | DT_WORDBREAK | DT_END_ELLIPSIS);

            // Las miniaturas, en fila bajo el texto. Se cargan la primera vez
            // que se pinta y se sueltan al cerrar.
            if (avatarFichero is { } fichAvatar && !DeSeguida)
            {
                ArrancarGdiplus();
                if (avatarImg == IntPtr.Zero && GdipLoadImageFromFile(fichAvatar, out var cargada) == 0) avatarImg = cargada;
                if (avatarImg != IntPtr.Zero && GdipCreateFromHDC(hdc, out var gA) == 0 && gA != IntPtr.Zero)
                {
                    GdipSetInterpolationMode(gA, 7);
                    GdipGetImageWidth(avatarImg, out var aw); GdipGetImageHeight(avatarImg, out var ah);
                    // Con su proporción, del ancho de su hueco y sin pasarse del alto del aviso.
                    var altoA = aw > 0 ? Math.Min(ALTO_TEXTO - 28, (int)(ANCHO_AVATAR * (double)ah / aw)) : ANCHO_AVATAR;
                    var anchoA = ah > 0 ? (int)(altoA * (double)aw / ah) : ANCHO_AVATAR;
                    GdipDrawImageRectI(gA, avatarImg, 18 + (ANCHO_AVATAR - anchoA) / 2, 14 - saltoFigura, anchoA, altoA);
                    GdipDeleteGraphics(gA);
                }
            }
            if (ficherosMini.Length > 0)
            {
                ArrancarGdiplus();
                if (minis.Count == 0)
                    foreach (var f in ficherosMini) minis.Add(GdipLoadImageFromFile(f, out var img) == 0 ? img : IntPtr.Zero);
                if (GdipCreateFromHDC(hdc, out var grafico) == 0 && grafico != IntPtr.Zero)
                {
                    GdipSetInterpolationMode(grafico, 7);
                    for (var i = 0; i < minis.Count; i++)
                    {
                        var rm = RectMini(i);
                        // La que está bajo el ratón, con su filo ámbar: es la que sale en grande.
                        if (i == bajoRaton)
                        {
                            var marco = new RECT { Left = rm.Left - 2, Top = rm.Top - 2, Right = rm.Right + 2, Bottom = rm.Bottom + 2 };
                            FillRect(hdc, ref marco, filo);
                        }
                        if (minis[i] != IntPtr.Zero) GdipDrawImageRectI(grafico, minis[i], rm.Left, rm.Top, ANCHO_MINI, ALTO_MINI);
                    }
                    GdipDeleteGraphics(grafico);
                }
            }
        }
        finally
        {
            BitBlt(hdcVentana, 0, 0, ANCHO, ALTO, hdc, 0, 0, SRCCOPY);
            SelectObject(hdc, lienzoAnterior); DeleteObject(lienzo); DeleteDC(hdc);
            EndPaint(ventana, ref ps);
            DeleteObject(fondo);
            DeleteObject(filo);
            DeleteObject(marca);
            DeleteObject(fuerte);
            DeleteObject(normal);
        }
    }

    /// <summary>
    /// Arriba a la derecha de la ventana de Arena, o de la pantalla si no lo
    /// hay. Arriba y no abajo: la parte de abajo de Arena es donde está la
    /// mano, que es lo último que conviene tapar aunque no se pueda pulsar.
    /// </summary>
    /// <summary>La esquina de la pila: el borde derecho y el de arriba, dentro del juego (o de la pantalla).</summary>
    private static (int Derecha, int Arriba) Esquina()
    {
        var arena = VentanaDeArena();
        if (arena != IntPtr.Zero && GetWindowRect(arena, out var r) && r.Right > r.Left)
        {
            return (r.Right - MARGEN, r.Top + MARGEN);
        }

        var area = new RECT();
        if (SystemParametersInfo(SPI_GETWORKAREA, 0, ref area, 0) && area.Right > area.Left)
        {
            return (area.Right - MARGEN, area.Top + MARGEN);
        }
        return (MARGEN + ANCHO_BASE, MARGEN);
    }
}
