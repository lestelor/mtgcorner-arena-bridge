using System.Diagnostics;
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

    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint color);
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
    private const uint WM_DESTROY = 0x2, WM_PAINT = 0xF, WM_TIMER = 0x113;
    private const int SW_SHOWNOACTIVATE = 4;
    private const uint LWA_ALPHA = 0x2;
    private const int TRANSPARENT_BK = 1;
    private const uint DT_LEFT = 0x0, DT_SINGLELINE = 0x20, DT_WORDBREAK = 0x10, DT_END_ELLIPSIS = 0x8000;
    private const uint SPI_GETWORKAREA = 0x30;

    /// <summary>Un color de GDI: 0x00BBGGRR, al revés que en la web.</summary>
    private static uint Rgb(int r, int g, int b) => (uint)(r | (g << 8) | (b << 16));

    /// <summary>Ancho y alto del aviso, y el aire que deja contra el borde del juego.</summary>
    private const int ANCHO = 380, ALTO = 104, MARGEN = 28;

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
    public static void Mostrar(string titulo, string texto, int segundos = 6)
    {
        try
        {
            var hilo = new Thread(() => { try { Correr(titulo, texto, segundos); } catch { /* sin escritorio */ } });
            hilo.IsBackground = true;
            hilo.Start();
            hilo.Join(TimeSpan.FromSeconds(segundos + 3));
        }
        catch { /* ni con esas: el programa sigue igual */ }
    }

    private static void Correr(string titulo, string texto, int segundos)
    {
        // EN PÍXELES DE VERDAD. `GetWindowRect` devuelve píxeles físicos; sin
        // declararse consciente del DPI, Windows virtualiza las coordenadas y
        // en una pantalla al 150% el aviso saldría desplazado respecto a Arena,
        // que es justo lo único que tiene que hacer bien.
        try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } // PER_MONITOR_AWARE_V2
        catch { /* en Windows viejos no existe: se pierde precisión, nada más */ }

        textoTitulo = titulo;
        textoCuerpo = texto;
        procedimiento = Procedimiento;

        var instancia = GetModuleHandle(null);
        var clase = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(procedimiento),
            hInstance = instancia,
            lpszClassName = "MtgCornerAviso",
        };
        RegisterClassEx(ref clase);   // si ya estaba registrada, falla y da igual

        var (x, y) = Donde();
        var ventana = CreateWindowEx(
            WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE,
            "MtgCornerAviso", "MTG Corner", WS_POPUP,
            x, y, ANCHO, ALTO, IntPtr.Zero, IntPtr.Zero, instancia, IntPtr.Zero);
        if (ventana == IntPtr.Zero) return;

        // Esquinas redondeadas y un punto de transparencia, para que no parezca
        // un cuadro de diálogo de 1998.
        SetWindowRgn(ventana, CreateRoundRectRgn(0, 0, ANCHO + 1, ALTO + 1, 18, 18), true);
        SetLayeredWindowAttributes(ventana, 0, 240, LWA_ALPHA);

        SetTimer(ventana, new IntPtr(1), (uint)Math.Max(1, segundos) * 1000, IntPtr.Zero);
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
        switch (mensaje)
        {
            case WM_PAINT:
                Pintar(ventana);
                return IntPtr.Zero;

            case WM_TIMER:
                DestroyWindow(ventana);
                return IntPtr.Zero;

            case WM_DESTROY:
                PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return DefWindowProc(ventana, mensaje, wParam, lParam);
    }

    /// <summary>
    /// El recuadro: fondo oscuro, filo ámbar a la izquierda como las tarjetas de
    /// la web, la marca arriba y las dos líneas del mensaje.
    /// </summary>
    private static void Pintar(IntPtr ventana)
    {
        var hdc = BeginPaint(ventana, out var ps);
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
            var rTitulo = new RECT { Left = 18, Top = 30, Right = ANCHO - 18, Bottom = 56 };
            DrawText(hdc, textoTitulo, -1, ref rTitulo, DT_LEFT | DT_SINGLELINE | DT_END_ELLIPSIS);

            SelectObject(hdc, normal);
            SetTextColor(hdc, Rgb(203, 213, 225));
            var rCuerpo = new RECT { Left = 18, Top = 58, Right = ANCHO - 18, Bottom = ALTO - 10 };
            DrawText(hdc, textoCuerpo, -1, ref rCuerpo, DT_LEFT | DT_WORDBREAK | DT_END_ELLIPSIS);
        }
        finally
        {
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
