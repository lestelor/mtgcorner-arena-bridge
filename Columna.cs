using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MtgCornerArenaBridge;

/// <summary>
/// LA COLUMNA DE ICONOS SOBRE ARENA, al estilo de Untapped.
///
/// Una tira estrecha pegada al borde derecho de la ventana del juego, con un
/// icono por cosa que se puede hacer. Al pasar el ratón se abre hacia la
/// izquierda y enseña qué es cada icono; al pulsar uno, hace lo suyo; al quitar
/// el ratón se cierra. Lo pidió el usuario el 2026-09-24: «que el menú salga
/// estilo Untapped, como una columna expansible con icono y cada icono hace
/// una cosa».
///
/// A DIFERENCIA DEL AVISO (Superposicion.cs), ESTA SÍ SE PULSA. Por eso NO
/// lleva WS_EX_TRANSPARENT. Lo que sí conserva es WS_EX_NOACTIVATE, y además
/// contesta WM_MOUSEACTIVATE con «no me actives»: pulsar un icono no le quita
/// el foco al juego ni un instante. Es lo que separa un accesorio de una
/// ventana que se pone en medio.
///
/// SIGUE A ARENA: cada medio segundo mira dónde está su ventana y se recoloca;
/// se esconde si Arena está minimizado, no existe, o no es la ventana activa
/// (para no flotar encima del navegador o del escritorio). En PANTALLA COMPLETA
/// EXCLUSIVA no se ve, como ninguna superposición; Arena viene por defecto en
/// ventana sin bordes, donde sí.
///
/// LOS ICONOS SON GLIFOS de la fuente «Segoe MDL2 Assets», que trae Windows 10
/// y 11: no hay imágenes que embeber ni escalar, y se pintan con el mismo GDI
/// que el texto. Win32 a pelo por lo mismo que el aviso: WinForms doblaba el
/// ejecutable.
///
/// VIVE EN SU PROPIO HILO con su bucle de mensajes, durante toda la vida del
/// modo residente. Lo que hace cada icono lo decide quien la arranca, con la
/// función que le pasa: esta clase sólo pinta y avisa.
/// </summary>
internal static class Columna
{
    public enum Accion { Importar, Coleccion, Constructor, Arranque, Salir, Mejorar, Similares, Combos, Amenaza, Sinergias, Resumen, Version, SinergiaRival }

    /// <summary>Una fila. Las de contexto traen su etiqueta hecha y un dato (nombre del mazo, id de la carta).</summary>
    private sealed record Fila(Accion Accion, string Glifo, string Clave, string? Dato = null, string? Etiqueta = null);

    // ── Lo que hace falta de Windows ──────────────────────────────────────

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
        public uint cbSize, style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TRACKMOUSEEVENT { public uint cbSize, dwFlags; public IntPtr hwndTrack; public uint dwHoverTime; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WNDCLASSEX clase);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string clase, string titulo, uint estilo, int x, int y, int ancho, int alto, IntPtr padre, IntPtr menu, IntPtr instancia, IntPtr param);
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
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rc);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT rc);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hWnd, IntPtr region, bool repintar);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr despues, int x, int y, int ancho, int alto, uint banderas);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr hWnd, IntPtr rc, bool borrar);
    [DllImport("user32.dll")] private static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT e);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr LoadCursor(IntPtr instancia, int cursor);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr contexto);

    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] private static extern int IntersectClipRect(IntPtr hdc, int izq, int arriba, int der, int abajo);
    [DllImport("gdi32.dll")] private static extern int SelectClipRgn(IntPtr hdc, IntPtr region);
    [DllImport("gdi32.dll")] private static extern bool Ellipse(IntPtr hdc, int izq, int arriba, int der, int abajo);
    [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int objeto);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int izq, int arriba, int der, int abajo, int anchoElipse, int altoElipse);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFont(int alto, int ancho, int escape, int orientacion, int grosor, uint cursiva, uint subrayado, uint tachado, uint juego, uint precision, uint recorte, uint calidad, uint paso, string cara);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr objeto);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr objeto);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr hdc, int modo);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr hdc, uint color);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? nombre);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr instancia, string nombre, uint tipo, int cx, int cy, uint banderas);
    [DllImport("user32.dll")]
    private static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr icono, int cx, int cy, uint paso, IntPtr pincel, uint banderas);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string fichero, int indice, out IntPtr grandes, out IntPtr pequenos, uint cuantos);

    private const uint WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x8000000;
    private const uint WS_POPUP = 0x80000000;
    private const uint WM_DESTROY = 0x2, WM_PAINT = 0xF, WM_CLOSE = 0x10, WM_MOUSEACTIVATE = 0x21, WM_TIMER = 0x113;
    private const uint WM_MOUSEMOVE = 0x200, WM_LBUTTONUP = 0x202, WM_MOUSELEAVE = 0x2A3;
    private const int MA_NOACTIVATE = 3;
    private const int SW_HIDE = 0, SW_SHOWNOACTIVATE = 4;
    private const uint LWA_ALPHA = 0x2;
    private const int TRANSPARENT_BK = 1;
    private const uint DT_LEFT = 0x0, DT_CENTER = 0x1, DT_VCENTER = 0x4, DT_SINGLELINE = 0x20, DT_CALCRECT = 0x400, DT_WORDBREAK = 0x10, DT_END_ELLIPSIS = 0x8000;
    private const uint TME_LEAVE = 0x2;
    private const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40, SWP_HIDEWINDOW = 0x80;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const int IDC_ARROW = 32512;

    private static uint Rgb(int r, int g, int b) => (uint)(r | (g << 8) | (b << 16));

    // ── Medidas ───────────────────────────────────────────────────────────

    /// <summary>Cerrada, sólo iconos; abierta, con su nombre al lado.</summary>
    /// <summary>
    /// Cerrada, y abierta: lo que mide de ancho como mínimo y como máximo.
    ///
    /// Antes era un ancho fijo de 238 px y el nombre del mazo se cortaba —
    /// «Improve "Mono-White Aur…"» (visto por el usuario el 2026-09-24). Ahora
    /// se mide la etiqueta más larga con la fuente con la que se va a pintar y
    /// la columna se abre lo que haga falta, con un tope: lo que pase de ahí
    /// sí se corta con puntos suspensivos, porque una columna de media
    /// pantalla tapa el juego, que es lo que se ha venido a jugar.
    /// </summary>
    private const int ANCHO_CERRADA = 46, ANCHO_ABIERTA = 238, ANCHO_MAXIMO = 420;
    private const uint IMAGE_ICON = 1, LR_DEFAULTCOLOR = 0, DI_NORMAL = 3;
    private const int ALTO_CABECERA = 40, ALTO_FILA = 44, AIRE_ABAJO = 8;
    /// <summary>Separación con el borde derecho de Arena.</summary>
    private const int MARGEN = 10;

    /// <summary>
    /// Qué se puede hacer, en orden. Los glifos son de «Segoe MDL2 Assets»:
    /// descargar, biblioteca, editar, encendido, cerrar.
    /// </summary>
    private static readonly Fila[] Fijas =
    [
        new(Accion.Importar, "", "col_importar"),
        new(Accion.Coleccion, "", "col_coleccion"),
        new(Accion.Constructor, "", "col_constructor"),
        new(Accion.Arranque, "", "col_arranque"),
        new(Accion.Version, "\uE946", "col_version", "version"),
        new(Accion.Salir, "", "col_salir"),
    ];

    /// <summary>
    /// LAS FILAS QUE SE VEN: primero las de CONTEXTO, luego las fijas.
    ///
    /// Las de contexto las pone quien vigila el juego (Contexto.cs, desde el
    /// residente): «Mejorar “tal mazo”» cuando acabas de guardarlo o llevarlo a
    /// la cola, «Similares a X» y «Combos con X» cuando señalas una carta en la
    /// mesa. Van arriba porque son lo que cambia y lo que se busca en el momento;
    /// las cinco de siempre siguen debajo, en su orden.
    ///
    /// Se sustituye el array entero y de golpe (volatile): quien pinta y quien
    /// atiende el ratón toman su copia y trabajan con ella, y el hilo del vigía
    /// nunca escribe dentro de lo que otro está leyendo. La altura de la ventana
    /// sale de cuántas filas hay; el latido de medio segundo la recoloca.
    /// </summary>
    private static volatile Fila[] Filas = Fijas;
    private static volatile int cuantasDeContexto;
    /// <summary>Lo que mide abierta: lo que pida la etiqueta más larga, entre el mínimo y el tope.</summary>
    private static volatile int anchoAbierta = ANCHO_ABIERTA;
    /// <summary>Alguna etiqueta no cabe ni en el ancho máximo: el visor tiene trabajo.</summary>
    private static volatile bool hayDesbordadas;
    private static int ticksVisor;

    /// <summary>
    /// LA FILA CUYA ACCIÓN ESTÁ EN CURSO (bajando las cartas parecidas), por su
    /// dato, o null. Mientras dura, esa fila cambia el glifo por un reloj y a
    /// su nombre se le van sumando puntos con el latido de la columna.
    ///
    /// Es el indicador de carga, y está AQUÍ y no en un panel a propósito: el
    /// panel grande tapa el juego, y quien está en mitad de una partida no
    /// quiere que le tapen la mesa mientras se bajan ocho imágenes. La columna
    /// ya está en pantalla y ocupa lo que ocupa (el usuario, 2026-09-24: «la
    /// gente puede estar jugando y no queremos tapar mientras está recuperando
    /// las cartas»).
    /// </summary>
    private static volatile string? enCurso;
    private static int faseCurso;

    /// <summary>
    /// LA FILA QUE PIDE ATENCIÓN: su glifo y su nombre en ámbar y un punto en la
    /// esquina, hasta que se pulsa. Es cómo se ve, con la columna cerrada, que
    /// hay un resumen de la partida esperando (pedido del usuario el
    /// 2026-09-26). El ámbar ya es el color de la marca en la cabecera, así que
    /// no se añade nada nuevo al diseño.
    /// </summary>
    private static readonly HashSet<string> destacadas = new();

    public static void Destacar(string dato)
    {
        lock (destacadas) destacadas.Add(dato);
        Refrescar();
    }

    public static void Olvidar(string dato)
    {
        lock (destacadas) destacadas.Remove(dato);
        Refrescar();
    }

    /// <summary>La versión del programa, para la fila que la enseña. La pone Program al arrancar.</summary>
    public static string Version { get; set; } = "";

    public static void EnCurso(string? dato)
    {
        enCurso = dato;
        faseCurso = 0;
        Refrescar();
    }

    /// <summary>
    /// Mide las etiquetas con la MISMA fuente con la que se pintan (Segoe UI
    /// 16) y ajusta el ancho abierto. `DT_CALCRECT` no dibuja: rellena el
    /// rectángulo con lo que ocuparía, que es justo lo que hace falta y lo
    /// único que acierta con nombres en japonés o en chino, donde contar
    /// caracteres no vale de nada.
    /// </summary>
    private static void MedirAncho()
    {
        var hdc = GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero) return;
        var fuente = CreateFont(16, 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        var anterior = SelectObject(hdc, fuente);
        try
        {
            var ancho = ANCHO_ABIERTA;
            foreach (var f in Filas)
            {
                var r = new RECT();
                DrawText(hdc, Etiqueta(f), -1, ref r, DT_CALCRECT | DT_SINGLELINE);
                // El hueco del icono, el texto, y el aire de la derecha.
                ancho = Math.Max(ancho, ANCHO_CERRADA + 2 + (r.Right - r.Left) + 16);
            }
            anchoAbierta = Math.Min(ancho, ANCHO_MAXIMO);
            hayDesbordadas = ancho > ANCHO_MAXIMO;
        }
        catch { /* sin medir se queda el de siempre */ }
        finally
        {
            SelectObject(hdc, anterior);
            DeleteObject(fuente);
            ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    public static void FilasDeContexto(params (Accion Accion, string Dato, string Etiqueta)[] contexto)
    {
        var nuevas = new Fila[contexto.Length + Fijas.Length];
        for (var i = 0; i < contexto.Length; i++)
            nuevas[i] = new Fila(contexto[i].Accion, GlifoDe(contexto[i].Accion), "", contexto[i].Dato, contexto[i].Etiqueta);
        Array.Copy(Fijas, 0, nuevas, contexto.Length, Fijas.Length);
        cuantasDeContexto = contexto.Length;
        Filas = nuevas;
        MedirAncho();   // el nombre del mazo o de la carta cambia lo que mide
        Refrescar();
    }

    /// <summary>Glifos de «Segoe MDL2 Assets» de las filas de contexto: estrella, copiar, enlace.</summary>
    private static string GlifoDe(Accion a) => a switch
    {
        Accion.Mejorar => "\uE735",
        Accion.Similares => "\uE8C8",
        Accion.Combos => "\uE71B",
        Accion.Amenaza => "\uE7BA",
        Accion.Sinergias => "\uE945",
        Accion.Resumen => "\uE7C3",
        Accion.SinergiaRival => "\uE7BA",
        _ => "",
    };

    /// <summary>El hueco entre lo del momento (arriba) y lo de siempre (abajo), con su raya en medio.</summary>
    private const int AIRE_GRUPO = 14;
    private static int Alto => ALTO_CABECERA + Filas.Length * ALTO_FILA + (cuantasDeContexto > 0 ? AIRE_GRUPO : 0) + AIRE_ABAJO;

    /// <summary>Dónde empieza la fila i: las de siempre bajan el hueco del grupo cuando hay filas del momento.</summary>
    private static int ArribaDe(int i) => ALTO_CABECERA + i * ALTO_FILA + (cuantasDeContexto > 0 && i >= cuantasDeContexto ? AIRE_GRUPO : 0);

    // El procedimiento en un campo estático a propósito: Windows guarda su
    // puntero, y si el recolector se llevara el delegado el siguiente mensaje
    // saltaría a memoria liberada.
    private static WndProc? procedimiento;
    private static IntPtr ventana;

    /// <summary>
    /// LA FLOR DE LA MARCA, que es el icono del propio ejecutable.
    ///
    /// Antes iba un cuadrado ámbar con las letras «MC»: se entendía, pero sobre
    /// el juego una marca se reconoce por el dibujo, no leyéndola (pedido por
    /// el usuario el 2026-09-24). No hace falta meter ninguna imagen nueva: el
    /// .exe ya lleva `mtgcorner.ico` con la flor en seis tamaños, y de ahí se
    /// saca.
    ///
    /// Primero por el recurso del módulo —el apphost de .NET guarda el icono de
    /// la aplicación con el identificador 32512—, que deja escoger el tamaño y
    /// coge el fotograma que mejor le va. Si eso fallara, se extrae del fichero
    /// del ejecutable, que devuelve el de 32×32. Y si tampoco, se pinta el
    /// cuadro de letras de siempre: la columna nunca se queda sin cabecera.
    /// </summary>
    private static IntPtr iconoMarca;
    private static bool iconoBuscado;

    private static IntPtr IconoMarca(int tam)
    {
        if (iconoBuscado) return iconoMarca;
        iconoBuscado = true;
        try
        {
            iconoMarca = LoadImage(GetModuleHandle(null), "#32512", IMAGE_ICON, tam, tam, LR_DEFAULTCOLOR);
            if (iconoMarca == IntPtr.Zero && Environment.ProcessPath is { } exe
                && ExtractIconEx(exe, 0, out var grande, out var pequeno, 1) > 0)
            {
                iconoMarca = grande != IntPtr.Zero ? grande : pequeno;
            }
        }
        catch { iconoMarca = IntPtr.Zero; }
        return iconoMarca;
    }
    private static Thread? hilo;
    private static bool abierta;
    private static bool siguiendoRaton;
    private static int filaBajoRaton = -1;
    private static bool visible;
    private static Func<Accion, string?, Task>? alPulsar;
    private static Func<bool>? arrancaSolo;
    /// <summary>Modo de prueba: se ve aunque Arena no sea la ventana activa (para
    /// mirarla desde otra ventana). En el residente, nunca.</summary>
    public static bool SiempreVisible;
    /// <summary>Modo de prueba: nace abierta y no se cierra al salir el ratón, para fotografiarla
    /// (Unity recoloca el cursor y una foto con ratón simulado sale siempre cerrada).</summary>
    public static bool ForzarAbierta;

    /// <summary>
    /// Arranca la columna en su hilo. <paramref name="pulsar"/> es lo que hace
    /// cada icono (corre fuera del hilo de la ventana, así que puede tardar);
    /// <paramref name="arranque"/> dice si el arranque automático está puesto,
    /// para el texto de ese icono.
    /// </summary>
    public static void Iniciar(Func<Accion, string?, Task> pulsar, Func<bool> arranque)
    {
        if (hilo is not null) return;
        alPulsar = pulsar;
        arrancaSolo = arranque;
        hilo = new Thread(Correr) { IsBackground = true, Name = "columna" };
        hilo.Start();
    }

    /// <summary>La quita. Se puede llamar desde cualquier hilo.</summary>
    public static void Cerrar()
    {
        if (ventana != IntPtr.Zero) PostMessage(ventana, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>Vuelve a pintar (por ejemplo, tras cambiar el arranque automático).</summary>
    public static void Refrescar()
    {
        if (ventana != IntPtr.Zero) InvalidateRect(ventana, IntPtr.Zero, true);
    }

    private static void Correr()
    {
        try
        {
            // En píxeles de verdad, como el aviso: sin esto, en una pantalla al
            // 150 % la columna saldría fuera del borde de Arena.
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { /* Windows viejos */ }

            procedimiento = Procedimiento;
            var instancia = GetModuleHandle(null);
            var clase = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(procedimiento),
                hInstance = instancia,
                // La flecha de siempre: sin cursor de clase, Windows deja el que
                // hubiera (a veces el reloj de arena del arranque).
                hCursor = LoadCursor(IntPtr.Zero, IDC_ARROW),
                lpszClassName = "MtgCornerColumna",
            };
            RegisterClassEx(ref clase);

            abierta = ForzarAbierta;
            MedirAncho();
            var anchoInicial = abierta ? anchoAbierta : ANCHO_CERRADA;
            var (x, y, ver) = Donde();
            ventana = CreateWindowEx(
                WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE,
                "MtgCornerColumna", "MTG Corner", WS_POPUP,
                x, y, anchoInicial, Alto, IntPtr.Zero, IntPtr.Zero, instancia, IntPtr.Zero);
            if (ventana == IntPtr.Zero) return;

            Recortar(anchoInicial);
            SetLayeredWindowAttributes(ventana, 0, 236, LWA_ALPHA);
            CrearAyuda(instancia);
            SetTimer(ventana, new IntPtr(1), 500, IntPtr.Zero);
            // El latido rápido del visor: sólo hace algo con la columna abierta
            // y alguna etiqueta que no cabe (ver Pintar).
            SetTimer(ventana, new IntPtr(2), 40, IntPtr.Zero);
            visible = ver;
            ShowWindow(ventana, ver ? SW_SHOWNOACTIVATE : SW_HIDE);

            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        catch { /* sin escritorio o sin permisos: el residente sigue sin columna */ }
        finally { if (ayuda != IntPtr.Zero) DestroyWindow(ayuda); ayuda = IntPtr.Zero; ventana = IntPtr.Zero; hilo = null; }
    }

    private static void Recortar(int ancho) =>
        SetWindowRgn(ventana, CreateRoundRectRgn(0, 0, ancho + 1, Alto + 1, 14, 14), true);

    /// <summary>La ventana principal de Arena, o cero. La usa también el panel de cartas.</summary>
    internal static IntPtr VentanaDeArena()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("MTGA"))
            {
                using (p) { if (p.MainWindowHandle != IntPtr.Zero) return p.MainWindowHandle; }
            }
        }
        catch { /* sin permisos para listar procesos */ }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Pegada al borde IZQUIERDO de Arena, en el TERCIO SUPERIOR.
    ///
    /// A la izquierda porque es donde la pone Untapped y es lo que la gente
    /// tiene aprendido (pedido por el usuario el 2026-09-24), y porque el lado
    /// derecho es justo donde Arena amplía la carta que tienes bajo el ratón
    /// en la partida: ahí un accesorio siempre acaba estorbando. En el tercio
    /// superior y no a media altura por lo mismo.
    ///
    /// La posición NO depende de lo ancha que esté: anclada a la izquierda, al
    /// abrirse crece hacia la derecha y los iconos no se mueven de su sitio.
    ///
    /// Y si se ve: sólo con Arena delante (o la propia columna, que al pulsarla
    /// es lo que hay debajo del ratón) y sin minimizar. No mira en qué pantalla
    /// del juego estás: sale igual en la home, en los mazos y en la partida.
    /// </summary>
    private static (int X, int Y, bool Ver) Donde()
    {
        var arena = VentanaDeArena();
        if (arena == IntPtr.Zero || IsIconic(arena) || !GetWindowRect(arena, out var r) || r.Right <= r.Left) return (0, 0, false);
        var delante = GetForegroundWindow();
        var ver = SiempreVisible || delante == arena || delante == ventana;
        return (r.Left + MARGEN, r.Top + (r.Bottom - r.Top) * 18 / 100, ver);
    }

    private static void Recolocar()
    {
        var ancho = abierta ? anchoAbierta : ANCHO_CERRADA;
        var (x, y, ver) = Donde();
        if (ver != visible)
        {
            visible = ver;
            if (!ver && !ForzarAbierta) { abierta = false; filaBajoRaton = -1; ancho = ANCHO_CERRADA; }
        }
        SetWindowPos(ventana, HWND_TOPMOST, x, y, ancho, Alto, SWP_NOACTIVATE | (ver ? SWP_SHOWWINDOW : SWP_HIDEWINDOW));
        Recortar(ancho);
    }

    /// <summary>La fila bajo un punto de la ventana, o -1.</summary>
    private static int FilaEn(IntPtr lParam)
    {
        var y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        var filas = Filas;
        for (var i = 0; i < filas.Length; i++)
        {
            var arriba = ArribaDe(i);
            if (y >= arriba && y < arriba + ALTO_FILA) return i;
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

            case WM_TIMER:
                if (wParam.ToInt64() == 2)
                {
                    // EL VISOR: las etiquetas que no caben se desplazan de lado
                    // (pedido del usuario el 2026-09-26: «a veces se recortan las
                    // letras»). Sólo se repinta cuando hay algo que mover.
                    if (abierta && hayDesbordadas) { ticksVisor++; InvalidateRect(hWnd, IntPtr.Zero, false); }
                    else ticksVisor = 0;
                    return IntPtr.Zero;
                }
                Recolocar();
                ActualizarAyuda();
                // Los puntos del indicador de carga andan con este latido.
                if (enCurso is not null) { faseCurso++; InvalidateRect(hWnd, IntPtr.Zero, false); }
                return IntPtr.Zero;

            // Pulsar la columna NO activa la columna: el teclado y el foco
            // siguen en el juego.
            case WM_MOUSEACTIVATE:
                return new IntPtr(MA_NOACTIVATE);

            case WM_MOUSEMOVE:
            {
                if (!siguiendoRaton)
                {
                    var seguir = new TRACKMOUSEEVENT { cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(), dwFlags = TME_LEAVE, hwndTrack = hWnd };
                    siguiendoRaton = TrackMouseEvent(ref seguir);
                }
                var fila = FilaEn(lParam);
                var cambia = !abierta || fila != filaBajoRaton;
                if (!abierta) { abierta = true; Recolocar(); }
                filaBajoRaton = fila;
                if (cambia) InvalidateRect(hWnd, IntPtr.Zero, true);
                return IntPtr.Zero;
            }

            case WM_MOUSELEAVE:
                siguiendoRaton = false;
                if (ForzarAbierta) return IntPtr.Zero;
                abierta = false;
                filaBajoRaton = -1;
                Recolocar();
                InvalidateRect(hWnd, IntPtr.Zero, true);
                return IntPtr.Zero;

            case WM_LBUTTONUP:
            {
                var fila = FilaEn(lParam);
                var filas = Filas;
                if (fila >= 0 && fila < filas.Length && alPulsar is { } pulsar)
                {
                    var accion = filas[fila].Accion;
                    var dato = filas[fila].Dato;
                    // Fuera del hilo de la ventana: lo que hace un icono puede
                    // tardar (subir la colección), y el bucle de mensajes no
                    // puede pararse a esperarlo.
                    _ = Task.Run(async () => { try { await pulsar(accion, dato); } catch { /* lo cuenta quien la lanzó */ } });
                }
                return IntPtr.Zero;
            }

            case WM_CLOSE:
                DestroyWindow(hWnd);
                return IntPtr.Zero;

            case WM_DESTROY:
                PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return DefWindowProc(hWnd, mensaje, wParam, lParam);
    }

    /// <summary>
    /// El dibujo: fondo oscuro como el aviso, la marca ámbar arriba, y una fila
    /// por icono. Abierta, el nombre a la derecha del icono y la fila bajo el
    /// ratón resaltada.
    /// </summary>
    private static void Pintar(IntPtr hWnd)
    {
        var hdc = BeginPaint(hWnd, out var ps);
        var ancho = abierta ? anchoAbierta : ANCHO_CERRADA;
        var fondo = CreateSolidBrush(Rgb(9, 13, 24));
        var resalte = CreateSolidBrush(Rgb(26, 34, 54));
        var separador = CreateSolidBrush(Rgb(148, 163, 184));
        var ambar = CreateSolidBrush(Rgb(245, 158, 11));
        var glifos = CreateFont(19, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe MDL2 Assets");
        var marca = CreateFont(12, 0, 0, 0, 800, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        var texto = CreateFont(16, 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        try
        {
            var todo = new RECT { Left = 0, Top = 0, Right = ancho, Bottom = Alto };
            FillRect(hdc, ref todo, fondo);
            SetBkMode(hdc, TRANSPARENT_BK);

            // La marca: la flor del programa, centrada en la parte estrecha. Si
            // el icono no se pudiera cargar, el cuadro ámbar con «MC» de antes.
            var cuadro = new RECT { Left = 11, Top = 9, Right = 35, Bottom = 33 };
            var flor = IconoMarca(cuadro.Right - cuadro.Left);
            if (flor != IntPtr.Zero)
            {
                DrawIconEx(hdc, cuadro.Left, cuadro.Top, flor,
                    cuadro.Right - cuadro.Left, cuadro.Bottom - cuadro.Top, 0, IntPtr.Zero, DI_NORMAL);
            }
            else
            {
                FillRect(hdc, ref cuadro, ambar);
                SelectObject(hdc, marca);
                SetTextColor(hdc, Rgb(15, 16, 34));
                DrawText(hdc, "MC", -1, ref cuadro, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
            }
            if (abierta)
            {
                SetTextColor(hdc, Rgb(252, 211, 77));
                var rMarca = new RECT { Left = ANCHO_CERRADA + 2, Top = 9, Right = ancho - 10, Bottom = 33 };
                DrawText(hdc, "MTG CORNER", -1, ref rMarca, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
            }

            var filas = Filas;
            var contexto = cuantasDeContexto;
            for (var i = 0; i < filas.Length; i++)
            {
                var arriba = ArribaDe(i);
                // La raya entre lo del momento y lo de siempre, en medio del
                // hueco: que se vea dónde acaba lo de la partida (pedido del
                // usuario el 2026-09-26: la fina de antes no se distinguía).
                if (contexto > 0 && i == contexto)
                {
                    var raya = new RECT { Left = 10, Top = arriba - AIRE_GRUPO / 2 - 1, Right = ancho - 10, Bottom = arriba - AIRE_GRUPO / 2 + 1 };
                    FillRect(hdc, ref raya, separador);
                }
                if (abierta && i == filaBajoRaton)
                {
                    var r = new RECT { Left = 4, Top = arriba + 2, Right = ancho - 4, Bottom = arriba + ALTO_FILA - 2 };
                    FillRect(hdc, ref r, resalte);
                }
                bool nueva;
                lock (destacadas) nueva = filas[i].Dato is { } dd && destacadas.Contains(dd);
                SelectObject(hdc, glifos);
                SetTextColor(hdc, i == filaBajoRaton ? Rgb(255, 255, 255) : nueva ? Rgb(252, 211, 77) : Rgb(186, 196, 214));
                var rGlifo = new RECT { Left = 0, Top = arriba, Right = ANCHO_CERRADA, Bottom = arriba + ALTO_FILA };
                var ocupada = enCurso is not null && filas[i].Dato == enCurso;
                DrawText(hdc, ocupada ? "" : filas[i].Glifo, -1, ref rGlifo, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
                if (nueva && !ocupada)
                {
                    // El punto: en la esquina del glifo, sin borde.
                    var pincelAnterior = SelectObject(hdc, ambar);
                    var plumaAnterior = SelectObject(hdc, GetStockObject(8 /* NULL_PEN */));
                    Ellipse(hdc, ANCHO_CERRADA - 15, arriba + 9, ANCHO_CERRADA - 6, arriba + 18);
                    SelectObject(hdc, plumaAnterior);
                    SelectObject(hdc, pincelAnterior);
                }

                if (abierta)
                {
                    SelectObject(hdc, texto);
                    SetTextColor(hdc, i == filaBajoRaton ? Rgb(255, 255, 255) : nueva ? Rgb(252, 211, 77) : Rgb(226, 232, 240));
                    var rTexto = new RECT { Left = ANCHO_CERRADA + 2, Top = arriba, Right = ancho - 10, Bottom = arriba + ALTO_FILA };
                    var rotulo = ocupada ? Etiqueta(filas[i]) + new string('.', 1 + faseCurso % 3) : Etiqueta(filas[i]);
                    // ¿Cabe? Si no, se pinta entera desplazada según el latido
                    // del visor, recortada a su hueco: espera al principio, corre
                    // hasta el final, espera, y vuelve.
                    var rMedida = new RECT();
                    DrawText(hdc, rotulo, -1, ref rMedida, DT_CALCRECT | DT_SINGLELINE);
                    var sobra = (rMedida.Right - rMedida.Left) - (rTexto.Right - rTexto.Left);
                    if (sobra <= 0)
                    {
                        DrawText(hdc, rotulo, -1, ref rTexto, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
                    }
                    else
                    {
                        const int ESPERA_INICIO = 30, ESPERA_FIN = 20, PASO = 2;
                        var ciclo = ESPERA_INICIO + sobra / PASO + ESPERA_FIN;
                        var fase = ticksVisor % ciclo;
                        var desplazado = Math.Clamp((fase - ESPERA_INICIO) * PASO, 0, sobra);
                        IntersectClipRect(hdc, rTexto.Left, rTexto.Top, rTexto.Right, rTexto.Bottom);
                        var rCorrido = new RECT { Left = rTexto.Left - desplazado, Top = rTexto.Top, Right = rTexto.Left - desplazado + (rMedida.Right - rMedida.Left) + 4, Bottom = rTexto.Bottom };
                        DrawText(hdc, rotulo, -1, ref rCorrido, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
                        SelectClipRgn(hdc, IntPtr.Zero);
                    }
                }
            }
        }
        finally
        {
            EndPaint(hWnd, ref ps);
            DeleteObject(fondo);
            DeleteObject(resalte);
            DeleteObject(separador);
            DeleteObject(ambar);
            DeleteObject(glifos);
            DeleteObject(marca);
            DeleteObject(texto);
        }
    }

    /// <summary>El nombre de cada icono; el del arranque dice si está puesto.</summary>
    private static string Etiqueta(Fila f) =>
        f.Etiqueta ?? (f.Accion == Accion.Arranque
            ? Textos.T(arrancaSolo?.Invoke() == true ? "col_arranque_si" : "col_arranque_no")
            : f.Accion == Accion.Version ? Textos.T("col_version", Version)
            : Textos.T(f.Clave));

    // ── La ayuda: qué hace cada fila, al pasar el ratón ──────────────────
    //
    // Una ventanita a la derecha de la columna abierta, con una frase sobre la
    // fila que hay bajo el ratón (pedido del usuario el 2026-09-26). Sale con
    // el tic de medio segundo de la columna, no al instante, para que mover el
    // ratón por la lista no vaya dejando cajas por el camino.
    private const int ANCHO_AYUDA = 300, MARGEN_AYUDA = 12;
    private static IntPtr ayuda;
    private static WndProc? procedimientoAyuda;
    private static int filaAyuda = -1, filaCandidata = -1, ticsCandidata;
    private static string textoAyuda = "";

    private static string ClaveAyuda(Accion a) => "ayuda_" + a.ToString().ToLowerInvariant();

    private static void CrearAyuda(IntPtr instancia)
    {
        procedimientoAyuda = ProcedimientoAyuda;
        var clase = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(procedimientoAyuda),
            hInstance = instancia,
            hCursor = LoadCursor(IntPtr.Zero, IDC_ARROW),
            lpszClassName = "MtgCornerAyuda",
        };
        RegisterClassEx(ref clase);
        ayuda = CreateWindowEx(
            WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE,
            "MtgCornerAyuda", "", WS_POPUP, 0, 0, ANCHO_AYUDA, 40, IntPtr.Zero, IntPtr.Zero, instancia, IntPtr.Zero);
        if (ayuda != IntPtr.Zero) SetLayeredWindowAttributes(ayuda, 0, 240, LWA_ALPHA);
    }

    /// <summary>Con la columna abierta y una fila bajo el ratón, la ayuda de esa fila; si no, nada.</summary>
    private static void ActualizarAyuda()
    {
        if (ayuda == IntPtr.Zero) return;
        var filas = Filas;
        var fila = abierta && visible && filaBajoRaton >= 0 && filaBajoRaton < filas.Length ? filaBajoRaton : -1;
        // Un segundo quieto sobre la fila (dos tics) antes de enseñarla: quien
        // baja el ratón por la lista no quiere una caja por cada fila.
        if (fila != filaCandidata) { filaCandidata = fila; ticsCandidata = 0; if (fila < 0 || fila != filaAyuda) { filaAyuda = -1; ShowWindow(ayuda, SW_HIDE); } if (fila < 0) return; }
        if (fila == filaAyuda) return;
        if (++ticsCandidata < 2) return;
        filaAyuda = fila;
        textoAyuda = Textos.T(ClaveAyuda(filas[fila].Accion));
        if (textoAyuda.Length == 0 || textoAyuda.StartsWith("ayuda_", StringComparison.Ordinal)) { ShowWindow(ayuda, SW_HIDE); return; }

        // El alto, medido con la fuente con la que se pinta.
        var hdc = GetDC(IntPtr.Zero);
        var fuente = CreateFont(15, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        var anterior = SelectObject(hdc, fuente);
        var r = new RECT { Left = 0, Top = 0, Right = ANCHO_AYUDA - MARGEN_AYUDA * 2, Bottom = 0 };
        DrawText(hdc, textoAyuda, -1, ref r, DT_LEFT | DT_WORDBREAK | DT_CALCRECT);
        SelectObject(hdc, anterior); DeleteObject(fuente); ReleaseDC(IntPtr.Zero, hdc);
        var alto = r.Bottom - r.Top + MARGEN_AYUDA * 2;

        GetWindowRect(ventana, out var rc);
        var x = rc.Right + 6;
        var y = rc.Top + ArribaDe(fila);
        SetWindowPos(ayuda, HWND_TOPMOST, x, y, ANCHO_AYUDA, alto, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        InvalidateRect(ayuda, IntPtr.Zero, true);
    }

    private static IntPtr ProcedimientoAyuda(IntPtr hWnd, uint mensaje, IntPtr wParam, IntPtr lParam)
    {
        switch (mensaje)
        {
            case WM_PAINT:
            {
                var hdc = BeginPaint(hWnd, out var ps);
                var fondo = CreateSolidBrush(Rgb(9, 13, 24));
                var marco = CreateSolidBrush(Rgb(56, 189, 248));
                var fuente = CreateFont(15, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
                try
                {
                    GetClientRect(hWnd, out var rc);
                    FillRect(hdc, ref rc, marco);
                    var dentro = new RECT { Left = 1, Top = 1, Right = rc.Right - 1, Bottom = rc.Bottom - 1 };
                    FillRect(hdc, ref dentro, fondo);
                    SetBkMode(hdc, TRANSPARENT_BK);
                    SelectObject(hdc, fuente);
                    SetTextColor(hdc, Rgb(226, 232, 240));
                    var rt = new RECT { Left = MARGEN_AYUDA, Top = MARGEN_AYUDA, Right = rc.Right - MARGEN_AYUDA, Bottom = rc.Bottom - MARGEN_AYUDA };
                    DrawText(hdc, textoAyuda, -1, ref rt, DT_LEFT | DT_WORDBREAK);
                }
                finally
                {
                    DeleteObject(fuente); DeleteObject(fondo); DeleteObject(marco);
                    EndPaint(hWnd, ref ps);
                }
                return IntPtr.Zero;
            }
            case WM_MOUSEACTIVATE:
                return new IntPtr(MA_NOACTIVATE);
        }
        return DefWindowProc(hWnd, mensaje, wParam, lParam);
    }
}
