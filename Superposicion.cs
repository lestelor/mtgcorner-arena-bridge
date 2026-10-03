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
    private const int ANCHO = 380, ALTO_TEXTO_MIN = 104, MARGEN = 28;
    /// <summary>
    /// LO QUE MIDE LA PARTE DE TEXTO: lo que pida el cuerpo, entre las dos
    /// líneas de siempre y unas ocho. Era fijo y el consejo de cada turno, que
    /// dice jugada, ataque y por qué, se cortaba a media frase (2026-10-03).
    /// </summary>
    private static int ALTO_TEXTO = ALTO_TEXTO_MIN;
    /// <summary>Las miniaturas: cartas pequeñas en fila bajo el texto (pedido del usuario el 2026-09-26: «no hace falta que sean muy grandes»).</summary>
    private const int ANCHO_MINI = 70, ALTO_MINI = 98, AIRE_MINI = 8, MAX_MINIS = 4;
    /// <summary>El alto del aviso que hay en pantalla: el del texto, y las miniaturas si las lleva.</summary>
    private static int ALTO = ALTO_TEXTO_MIN;
    private static string[] ficherosMini = [];
    private static readonly List<IntPtr> minis = new();
    private static IntPtr gdiplus;
    /// <summary>La alerta en pantalla es fija: se queda hasta su X y se puede pulsar.</summary>
    private static bool esFijo;
    /// <summary>Qué hay bajo el ratón: la X (-2), la miniatura i (0..n) o nada (-1).</summary>
    private static int bajoRaton = -1;
    private const int BAJO_X = -2, LADO_X = 28;
    /// <summary>La carta en grande al pasar por una miniatura: la mitad de la imagen «normal» de Scryfall (488×680), así sale nítida.</summary>
    private const int ANCHO_ZOOM = 244, ALTO_ZOOM = 340;
    private static IntPtr ventanaZoom;
    private static int miniZoom = -1;

    // El procedimiento de ventana se guarda en un campo estático A PROPÓSITO:
    // Windows se queda con su puntero, y si el recolector se llevara el
    // delegado, el primer mensaje que llegara saltaría a memoria liberada.
    private static WndProc? procedimiento;
    private static string textoTitulo = "", textoCuerpo = "";

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
    public static void Mostrar(string titulo, string texto, int segundos = 6, IReadOnlyList<string>? imagenes = null, bool fijo = false)
    {
        try
        {
            var hilo = MostrarSinEsperar(titulo, texto, segundos, imagenes, fijo);
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
    /// UNO SOLO A LA VEZ: el que hubiera se quita antes de poner el nuevo. Sin
    /// esto, un «subiendo» de veinte segundos y un «hecho» de seis se apilarían
    /// en el mismo sitio y el de encima taparía al otro.
    /// </summary>
    public static Thread? MostrarSinEsperar(string titulo, string texto, int segundos = 6, IReadOnlyList<string>? imagenes = null, bool fijo = false, bool importante = false)
    {
        /**
         * UN AVISO IMPORTANTE NO LO PISA OTRO CUALQUIERA mientras dura. Cada
         * aviso cierra el anterior, y el consejo de mulligan —que sólo sirve en
         * los segundos en que decides— lo tapaba la subida de fondo que hace el
         * programa al arrancar («no he visto que se me aconseje», el usuario,
         * 2026-10-03). Los importantes sí se sustituyen entre ellos.
         */
        if (!importante && DateTime.UtcNow < importanteHasta) return null;
        if (importante) importanteHasta = DateTime.UtcNow.AddSeconds(segundos);
        try
        {
            Ocultar();
            var ficheros = (imagenes ?? []).Where(f => !string.IsNullOrEmpty(f) && File.Exists(f)).Take(MAX_MINIS).ToArray();
            var hilo = new Thread(() => { try { Correr(titulo, texto, segundos, ficheros, fijo); } catch (Exception e) { Console.Error.WriteLine($"[aviso] {e.GetType().Name}: {e.Message}"); } });
            hilo.IsBackground = true;
            hilo.Start();
            return hilo;
        }
        catch { return null; }
    }

    /// <summary>Quita el aviso que haya, si hay alguno. Desde cualquier hilo.</summary>
    public static void Ocultar()
    {
        var v = ventanaActual;
        if (v != IntPtr.Zero) PostMessage(v, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>Hasta cuándo dura el último aviso importante (ver MostrarSinEsperar).</summary>
    private static DateTime importanteHasta;

    /// <summary>La ventana del aviso que está en pantalla, o cero.</summary>
    private static volatile IntPtr ventanaActual;

    private static void Correr(string titulo, string texto, int segundos, string[] ficheros, bool fijo)
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
        esFijo = fijo;
        bajoRaton = -1;
        miniZoom = -1;
        ALTO_TEXTO = AltoDelCuerpo(texto);
        ALTO = ALTO_TEXTO + (ficheros.Length > 0 ? ALTO_MINI + AIRE_MINI : 0);
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

        var (x, y) = Donde();
        var ventana = CreateWindowEx(
            // La fija no lleva WS_EX_TRANSPARENT: tiene que recibir el ratón (la X y el zoom).
            WS_EX_TOPMOST | (fijo ? 0 : WS_EX_TRANSPARENT) | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE,
            "MtgCornerAviso", "MTG Corner", WS_POPUP,
            x, y, ANCHO, ALTO, IntPtr.Zero, IntPtr.Zero, instancia, IntPtr.Zero);
        if (ventana == IntPtr.Zero) { Console.Error.WriteLine($"[aviso] CreateWindowEx falló: error {Marshal.GetLastWin32Error()}"); return; }

        // Esquinas redondeadas y un punto de transparencia, para que no parezca
        // un cuadro de diálogo de 1998.
        SetWindowRgn(ventana, CreateRoundRectRgn(0, 0, ANCHO + 1, ALTO + 1, 18, 18), true);
        SetLayeredWindowAttributes(ventana, 0, 240, LWA_ALPHA);

        if (!fijo) SetTimer(ventana, new IntPtr(1), (uint)Math.Max(1, segundos) * 1000, IntPtr.Zero);
        ventanaActual = ventana;
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
                if (esFijo && QueHayEn(lParam) == BAJO_X) PostMessage(ventana, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                return IntPtr.Zero;

            case WM_TIMER:
            case WM_CLOSE:
                if (ventanaActual == ventana) ventanaActual = IntPtr.Zero;
                if (ventanaZoom != IntPtr.Zero) DestroyWindow(ventanaZoom);
                DestroyWindow(ventana);
                return IntPtr.Zero;

            case WM_DESTROY:
                foreach (var m in minis) if (m != IntPtr.Zero) GdipDisposeImage(m);
                minis.Clear();
                PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return DefWindowProc(ventana, mensaje, wParam, lParam);
    }

    /// <summary>Dónde está la X de una alerta fija, en la ventana.</summary>
    private static RECT RectX() => new() { Left = ANCHO - LADO_X - 8, Top = 8, Right = ANCHO - 8, Bottom = 8 + LADO_X };

    /// <summary>Dónde está la miniatura i, en la ventana.</summary>
    /// <summary>El alto de la parte de texto para este cuerpo, medido con la letra con que se pinta.</summary>
    private static int AltoDelCuerpo(string cuerpo)
    {
        var hdc = GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero) return ALTO_TEXTO_MIN;
        var fuente = CreateFont(17, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        var anterior = SelectObject(hdc, fuente);
        try
        {
            var r = new RECT { Left = 0, Top = 0, Right = ANCHO - 36, Bottom = 0 };
            DrawText(hdc, cuerpo, -1, ref r, DT_LEFT | DT_WORDBREAK | DT_CALCRECT);
            return Math.Clamp(58 + (r.Bottom - r.Top) + 12, ALTO_TEXTO_MIN, 58 + 8 * 23 + 12);
        }
        finally { SelectObject(hdc, anterior); DeleteObject(fuente); ReleaseDC(IntPtr.Zero, hdc); }
    }

    private static RECT RectMini(int i) => new() { Left = 18 + i * (ANCHO_MINI + AIRE_MINI), Top = ALTO_TEXTO - 4, Right = 18 + i * (ANCHO_MINI + AIRE_MINI) + ANCHO_MINI, Bottom = ALTO_TEXTO - 4 + ALTO_MINI };

    /// <summary>Qué hay bajo un punto de la ventana: la X, una miniatura o nada. Sólo en las fijas: las otras no ven el ratón.</summary>
    private static int QueHayEn(IntPtr lParam)
    {
        if (!esFijo) return -1;
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
            FillRect(hdc, ref banda, filo);

            SetBkMode(hdc, TRANSPARENT_BK);

            SelectObject(hdc, marca);
            SetTextColor(hdc, Rgb(252, 211, 77));
            var rMarca = new RECT { Left = 18, Top = 12, Right = ANCHO - 18, Bottom = 28 };
            DrawText(hdc, "MTG CORNER", -1, ref rMarca, DT_LEFT | DT_SINGLELINE);

            SelectObject(hdc, fuerte);
            SetTextColor(hdc, Rgb(255, 255, 255));
            var rTitulo = new RECT { Left = 18, Top = 30, Right = ANCHO - (esFijo ? LADO_X + 16 : 18), Bottom = 56 };
            DrawText(hdc, textoTitulo, -1, ref rTitulo, DT_LEFT | DT_SINGLELINE | DT_END_ELLIPSIS);

            // LA X de las fijas, arriba a la derecha; se ilumina al pasar.
            if (esFijo)
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
            var rCuerpo = new RECT { Left = 18, Top = 58, Right = ANCHO - 18, Bottom = ALTO_TEXTO - 10 };
            DrawText(hdc, textoCuerpo, -1, ref rCuerpo, DT_LEFT | DT_WORDBREAK | DT_END_ELLIPSIS);

            // Las miniaturas, en fila bajo el texto. Se cargan la primera vez
            // que se pinta y se sueltan al cerrar.
            if (ficherosMini.Length > 0)
            {
                if (gdiplus == IntPtr.Zero)
                {
                    var entrada = new GdiplusStartupInput { GdiplusVersion = 1 };
                    GdiplusStartup(out gdiplus, ref entrada, IntPtr.Zero);
                }
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
    private static (int X, int Y) Donde()
    {
        var arena = VentanaDeArena();
        if (arena != IntPtr.Zero && GetWindowRect(arena, out var r) && r.Right > r.Left)
        {
            return (r.Right - ANCHO - MARGEN, r.Top + MARGEN);
        }

        var area = new RECT();
        if (SystemParametersInfo(SPI_GETWORKAREA, 0, ref area, 0) && area.Right > area.Left)
        {
            return (area.Right - ANCHO - MARGEN, area.Top + MARGEN);
        }
        return (MARGEN, MARGEN);
    }
}
