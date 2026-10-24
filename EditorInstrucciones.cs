using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MtgCornerArenaBridge;

/// <summary>
/// EL EDITOR DE LAS INSTRUCCIONES DEL ENTRENADOR (el usuario, 2026-10-19:
/// «podría existir el prompt que se pueda modificar para los usuarios
/// logeados, modificar desde el exe»).
///
/// Una ventanita con un cuadro de texto (500 caracteres como mucho), Guardar y
/// Cancelar. Lo que se escribe se guarda en la web (/api/mtga-device/entrenador)
/// y la IA lo recibe con cada consejo: el de cada turno, el de mulligan y los
/// del draft. Ver lib/instruccionesEntrenador.ts en la web.
///
/// A DIFERENCIA DE LA COLUMNA Y LOS AVISOS, ÉSTA SÍ COGE EL FOCO: hay que
/// escribir en ella. Por eso el cuadro es un EDIT de Windows de los de siempre,
/// que trae gratis el cursor, la selección, copiar y pegar y el teclado de
/// japonés o chino, cosas que pintadas a mano no saldrían bien. Lo demás se
/// pinta con los colores de la columna: sin el manifiesto de los controles
/// modernos, un botón de Windows sale gris y en relieve, y encima del juego
/// parecía otro programa.
///
/// Su propio hilo y su bucle de mensajes, e IsDialogMessage para que el
/// tabulador pase de uno a otro. Escape y la X cancelan.
/// </summary>
internal static class EditorInstrucciones
{
    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }
    [StructLayout(LayoutKind.Sequential)]
    private struct DRAWITEMSTRUCT { public uint CtlType, CtlID, itemID, itemAction, itemState; public IntPtr hwndItem, hDC; public RECT rcItem; public IntPtr itemData; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize, style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName, lpszClassName; public IntPtr hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WNDCLASSEX c);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint exStyle, string clase, string titulo, uint estilo, int x, int y, int w, int h, IntPtr padre, IntPtr menu, IntPtr instancia, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetMessage(out MSG m, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool IsDialogMessage(IntPtr hDlg, ref MSG m);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DispatchMessage(ref MSG m);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetWindowText(IntPtr hWnd, string texto);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, char[] texto, int max);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr despuesDe, int x, int y, int w, int h, uint banderas);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadCursor(IntPtr instancia, IntPtr cursor);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadImage(IntPtr instancia, string nombre, uint tipo, int cx, int cy, uint banderas);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
    [DllImport("user32.dll")] private static extern int FillRect(IntPtr hdc, ref RECT r, IntPtr pincel);
    [DllImport("user32.dll")] private static extern bool DrawFocusRect(IntPtr hdc, ref RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int DrawText(IntPtr hdc, string texto, int largo, ref RECT r, uint formato);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? nombre);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateFont(int h, int w, int esc, int ori, int peso, uint it, uint sub, uint tach, uint cs, uint op, uint cp, uint cal, uint fam, string cara);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr o);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr hdc, uint color);
    [DllImport("gdi32.dll")] private static extern uint SetBkColor(IntPtr hdc, uint color);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr hdc, int modo);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hWnd, int atributo, ref int valor, int tam);

    private const uint WS_POPUP = 0x80000000, WS_CAPTION = 0x00C00000, WS_SYSMENU = 0x00080000, WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_TABSTOP = 0x00010000;
    private const uint WS_EX_TOPMOST = 0x8, WS_EX_DLGMODALFRAME = 0x1;
    private const uint ES_MULTILINE = 0x4, ES_AUTOVSCROLL = 0x40, ES_WANTRETURN = 0x1000, BS_OWNERDRAW = 0xB;
    private const uint WM_DESTROY = 0x2, WM_CLOSE = 0x10, WM_SETFONT = 0x30, WM_DRAWITEM = 0x2B, WM_COMMAND = 0x111;
    private const uint WM_CTLCOLOREDIT = 0x133, WM_CTLCOLORSTATIC = 0x138, EM_SETSEL = 0xB1, EM_LIMITTEXT = 0xC5, EM_SETMARGINS = 0xD3;
    private const uint EN_CHANGE = 0x300, ODS_SELECTED = 0x1, ODS_FOCUS = 0x10;
    private const uint DT_CENTER = 0x1, DT_VCENTER = 0x4, DT_WORDBREAK = 0x10, DT_SINGLELINE = 0x20, DT_CALCRECT = 0x400;
    private const uint SWP_SHOWWINDOW = 0x40;
    private const int ID_GUARDAR = 1, ID_CANCELAR = 2, ID_TEXTO = 3, ID_CUENTA = 4;
    /// <summary>Medidas a 96 ppp; se multiplican por la escala de la pantalla de Arena.</summary>
    private const int ANCHO = 540, MARGEN = 18, ALTO_TEXTO = 170, ANCHO_BOTON = 116, ALTO_BOTON = 34;

    // Los colores de la columna (Columna.cs): fondo, resalte, texto, tenue, ámbar.
    private static uint Rgb(int r, int g, int b) => (uint)(r | (g << 8) | (b << 16));
    private static readonly uint FONDO = Rgb(9, 13, 24), CAMPO = Rgb(26, 34, 54), PULSADO = Rgb(51, 65, 85),
        TEXTO = Rgb(226, 232, 240), TENUE = Rgb(148, 163, 184), AMBAR = Rgb(245, 158, 11), SOBRE_AMBAR = Rgb(15, 16, 34);

    // El procedimiento en un campo estático a propósito: Windows guarda su
    // puntero, y si el recolector se llevara el delegado el siguiente mensaje
    // saltaría a memoria liberada.
    private static WndProc? procedimiento;
    private static IntPtr editor, cuenta, fuente, pincelFondo, pincelCampo, pincelAmbar, pincelPulsado;
    private static string? resultado;
    /// <summary>Los textos de esta vez: los de las instrucciones, o los de quien la abra para otra cosa (la opinión del programa).</summary>
    private static string tituloVentana = "", rotuloGuardar = "";
    private static int tope, abierto;
    private static bool claseHecha;

    /// <summary>
    /// Abre el editor con el texto de ahora y espera: el texto nuevo al guardar,
    /// null al cancelar. Si ya hay uno abierto, null (no se abren dos).
    /// </summary>
    /// <param name="titulo">El de la barra; sin él, el de las instrucciones.</param>
    /// <param name="explicacion">Lo de encima del cuadro; sin ella, la de las instrucciones.</param>
    /// <param name="guardar">El botón de aceptar; sin él, «Guardar».</param>
    public static Task<string?> Editar(string actual, int maximo, string? titulo = null, string? explicacion = null, string? guardar = null)
    {
        if (Interlocked.Exchange(ref abierto, 1) == 1) return Task.FromResult<string?>(null);
        var listo = new TaskCompletionSource<string?>();
        var hilo = new Thread(() =>
        {
            tituloVentana = titulo ?? Textos.T("instr_titulo");
            rotuloGuardar = guardar ?? Textos.T("instr_guardar");
            try { listo.TrySetResult(Correr(actual, maximo, explicacion ?? Textos.T("instr_explicacion", maximo))); }
            catch { listo.TrySetResult(null); }
            finally { Interlocked.Exchange(ref abierto, 0); }
        }) { IsBackground = true, Name = "instrucciones" };
        hilo.SetApartmentState(ApartmentState.STA);
        hilo.Start();
        return listo.Task;
    }

    /// <summary>La ventana principal de Arena, o cero si no está abierto (como en Superposicion.cs).</summary>
    private static IntPtr VentanaDeArena()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("MTGA"))
                using (p) if (p.MainWindowHandle != IntPtr.Zero) return p.MainWindowHandle;
        }
        catch { /* sin permisos para listar procesos: se usa la pantalla */ }
        return IntPtr.Zero;
    }

    private static string? Correr(string actual, int maximo, string explicacion)
    {
        resultado = null;
        tope = maximo;
        var instancia = GetModuleHandle(null);
        if (pincelFondo == IntPtr.Zero) pincelFondo = CreateSolidBrush(FONDO);   // el de la clase: uno para siempre
        pincelCampo = CreateSolidBrush(CAMPO);
        pincelAmbar = CreateSolidBrush(AMBAR);
        pincelPulsado = CreateSolidBrush(PULSADO);
        if (!claseHecha)
        {
            procedimiento = Procedimiento;
            var clase = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(procedimiento),
                hInstance = instancia,
                hIcon = LoadImage(instancia, "#32512", 1, 0, 0, 0x40),   // el icono del .exe, IMAGE_ICON, LR_DEFAULTSIZE
                hCursor = LoadCursor(IntPtr.Zero, (IntPtr)32512),
                hbrBackground = pincelFondo,
                lpszClassName = "MtgCornerInstrucciones",
            };
            claseHecha = RegisterClassEx(ref clase) != 0;
        }

        // A la escala de la pantalla donde está Arena: el proceso se declara
        // consciente del DPI (ver Columna.cs), así que todo va en píxeles reales.
        var arena = VentanaDeArena();
        var ppp = 96u;
        try { ppp = arena != IntPtr.Zero ? GetDpiForWindow(arena) : GetDpiForSystem(); } catch { /* Windows viejos: 96 */ }
        if (ppp == 0) ppp = 96;
        int S(int v) => (int)Math.Round(v * ppp / 96.0);

        var ventana = CreateWindowEx(WS_EX_TOPMOST | WS_EX_DLGMODALFRAME, "MtgCornerInstrucciones", tituloVentana,
            WS_POPUP | WS_CAPTION | WS_SYSMENU, 0, 0, S(ANCHO), S(400), IntPtr.Zero, IntPtr.Zero, instancia, IntPtr.Zero);
        if (ventana == IntPtr.Zero) { Limpiar(); return null; }
        // La barra del título también oscura (Windows 10 2004 en adelante; antes, el 19).
        try
        {
            var si = 1;
            if (DwmSetWindowAttribute(ventana, 20, ref si, 4) != 0) DwmSetWindowAttribute(ventana, 19, ref si, 4);
            var colorBarra = (int)FONDO;
            DwmSetWindowAttribute(ventana, 35, ref colorBarra, 4);   // DWMWA_CAPTION_COLOR, sólo Windows 11
        }
        catch { /* sin DWM: la barra de siempre */ }

        fuente = CreateFont(-S(15), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        GetClientRect(ventana, out var cliente);
        var anchoUtil = cliente.Right - S(MARGEN) * 2;

        // Lo que mide la explicación, medido y no supuesto: en japonés o en
        // alemán no ocupa lo mismo que en español.
        var medida = new RECT { Right = anchoUtil };
        var hdc = GetDC(ventana);
        var antes = SelectObject(hdc, fuente);
        DrawText(hdc, explicacion, -1, ref medida, DT_WORDBREAK | DT_CALCRECT);
        SelectObject(hdc, antes);
        ReleaseDC(ventana, hdc);
        var altoExplicacion = medida.Bottom + S(4);

        IntPtr Control(string clase, string texto, uint estilo, int cx, int cy, int w, int h, int id)
        {
            var c = CreateWindowEx(0, clase, texto, WS_CHILD | WS_VISIBLE | estilo, cx, cy, w, h, ventana, (IntPtr)id, instancia, IntPtr.Zero);
            SendMessage(c, WM_SETFONT, fuente, (IntPtr)1);
            return c;
        }
        var y = S(MARGEN);
        Control("STATIC", explicacion, 0, S(MARGEN), y, anchoUtil, altoExplicacion, 0);
        y += altoExplicacion + S(10);
        editor = Control("EDIT", actual.Replace("\r\n", "\n").Replace("\n", "\r\n"),
            WS_TABSTOP | ES_MULTILINE | ES_AUTOVSCROLL | ES_WANTRETURN, S(MARGEN), y, anchoUtil, S(ALTO_TEXTO), ID_TEXTO);
        SendMessage(editor, EM_LIMITTEXT, (IntPtr)maximo, IntPtr.Zero);
        SendMessage(editor, EM_SETMARGINS, (IntPtr)3, (IntPtr)(S(8) | (S(8) << 16)));   // EC_LEFTMARGIN | EC_RIGHTMARGIN
        y += S(ALTO_TEXTO) + S(14);
        cuenta = Control("STATIC", "", 0, S(MARGEN), y + (S(ALTO_BOTON) - S(20)) / 2, S(120), S(20), ID_CUENTA);
        Control("BUTTON", Textos.T("instr_cancelar"), WS_TABSTOP | BS_OWNERDRAW, cliente.Right - S(MARGEN) - S(ANCHO_BOTON) * 2 - S(10), y, S(ANCHO_BOTON), S(ALTO_BOTON), ID_CANCELAR);
        Control("BUTTON", rotuloGuardar, WS_TABSTOP | BS_OWNERDRAW, cliente.Right - S(MARGEN) - S(ANCHO_BOTON), y, S(ANCHO_BOTON), S(ALTO_BOTON), ID_GUARDAR);
        Contar();

        // Del alto del área útil al de la ventana, con su barra y sus bordes; y en el centro de Arena.
        var altoCliente = y + S(ALTO_BOTON) + S(MARGEN);
        GetWindowRect(ventana, out var fuera);
        var w = fuera.Right - fuera.Left;
        var h = fuera.Bottom - fuera.Top - cliente.Bottom + altoCliente;
        RECT zona;
        if (arena == IntPtr.Zero || !GetWindowRect(arena, out zona)) zona = new RECT { Right = GetSystemMetrics(0), Bottom = GetSystemMetrics(1) };
        // Se enseña con SetWindowPos y NUNCA con ShowWindow: el primer ShowWindow
        // de un proceso obedece a cómo se lanzó, y lanzado oculto (el taller, o el
        // residente si lo arranca algo con la ventana escondida) la volvía a esconder.
        SetWindowPos(ventana, (IntPtr)(-1), zona.Left + (zona.Right - zona.Left - w) / 2, zona.Top + (zona.Bottom - zona.Top - h) / 2, w, h, SWP_SHOWWINDOW);   // HWND_TOPMOST
        SetForegroundWindow(ventana);
        SetFocus(editor);
        var fin = GetWindowTextLength(editor);
        SendMessage(editor, EM_SETSEL, (IntPtr)fin, (IntPtr)fin);

        while (GetMessage(out var m, IntPtr.Zero, 0, 0) > 0)
        {
            if (IsDialogMessage(ventana, ref m)) continue;
            TranslateMessage(ref m);
            DispatchMessage(ref m);
        }
        Limpiar();
        return resultado;
    }

    private static void Limpiar()
    {
        foreach (var o in new[] { fuente, pincelCampo, pincelAmbar, pincelPulsado }) if (o != IntPtr.Zero) DeleteObject(o);
        // El del fondo no: es el de la clase, que sigue registrada para la próxima vez.
        fuente = pincelCampo = pincelAmbar = pincelPulsado = IntPtr.Zero;
    }

    /// <summary>Lo que se lleva escrito, «123 / 500», debajo del cuadro.</summary>
    private static void Contar() => SetWindowText(cuenta, $"{GetWindowTextLength(editor)} / {tope}");

    private static IntPtr Procedimiento(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_COMMAND:
                var id = (int)(wParam.ToInt64() & 0xFFFF);
                var aviso = (uint)((wParam.ToInt64() >> 16) & 0xFFFF);
                if (id == ID_TEXTO && aviso == EN_CHANGE) { Contar(); return IntPtr.Zero; }
                if (id == ID_GUARDAR)
                {
                    var largo = GetWindowTextLength(editor);
                    var buf = new char[largo + 1];
                    var leidos = GetWindowText(editor, buf, buf.Length);
                    resultado = new string(buf, 0, Math.Clamp(leidos, 0, largo)).Replace("\r\n", "\n");
                    DestroyWindow(hWnd);
                    return IntPtr.Zero;
                }
                // Cancelar, y Escape: IsDialogMessage lo manda como IDCANCEL, que es el 2.
                if (id == ID_CANCELAR) { resultado = null; DestroyWindow(hWnd); return IntPtr.Zero; }
                break;
            case WM_CTLCOLOREDIT:
                SetTextColor(wParam, TEXTO);
                SetBkColor(wParam, CAMPO);
                return pincelCampo;
            case WM_CTLCOLORSTATIC:
                SetTextColor(wParam, TENUE);
                SetBkColor(wParam, FONDO);
                return pincelFondo;
            case WM_DRAWITEM:
                // Los dos botones: Guardar en ámbar, Cancelar como una fila de la columna.
                var d = Marshal.PtrToStructure<DRAWITEMSTRUCT>(lParam);
                var guardar = d.CtlID == ID_GUARDAR;
                var pulsado = (d.itemState & ODS_SELECTED) != 0;
                var r = d.rcItem;
                FillRect(d.hDC, ref r, guardar ? (pulsado ? pincelPulsado : pincelAmbar) : (pulsado ? pincelPulsado : pincelCampo));
                SetBkMode(d.hDC, 1);   // TRANSPARENT
                SetTextColor(d.hDC, guardar && !pulsado ? SOBRE_AMBAR : TEXTO);
                var antes = SelectObject(d.hDC, fuente);
                var rotulo = d.CtlID == ID_GUARDAR ? rotuloGuardar : Textos.T("instr_cancelar");
                DrawText(d.hDC, rotulo, -1, ref r, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
                SelectObject(d.hDC, antes);
                if ((d.itemState & ODS_FOCUS) != 0)
                {
                    var foco = new RECT { Left = r.Left + 3, Top = r.Top + 3, Right = r.Right - 3, Bottom = r.Bottom - 3 };
                    DrawFocusRect(d.hDC, ref foco);
                }
                return (IntPtr)1;
            case WM_CLOSE:
                resultado = null;
                DestroyWindow(hWnd);
                return IntPtr.Zero;
            case WM_DESTROY:
                PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }
}
