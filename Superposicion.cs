using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

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
/// NI ROBA EL FOCO. Con WS_EX_NOACTIVATE y ShowWithoutActivation aparece sin
/// quitarle el teclado al juego, que es lo que hace que una ventana emergente
/// tire a alguien de una partida.
///
/// EL LÍMITE, DICHO CLARO: en PANTALLA COMPLETA EXCLUSIVA no se ve. Ninguna
/// superposición se ve ahí, tampoco las de los demás; Windows le da la pantalla
/// entera al juego. Arena viene por defecto en ventana sin bordes, donde sí
/// funciona, pero quien la haya cambiado no verá nada y no es un fallo que se
/// pueda arreglar desde aquí.
///
/// SI ARENA NO ESTÁ ABIERTO se coloca en la esquina de la pantalla, que sigue
/// siendo un sitio razonable para un aviso.
/// </summary>
internal static class Superposicion
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    /// <summary>Esquinas redondeadas, para que no parezca un cuadro de diálogo de 1998.</summary>
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int izq, int arriba, int der, int abajo, int anchoElipse, int altoElipse);

    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    /// <summary>Ancho y alto del aviso, y el aire que deja contra el borde del juego.</summary>
    private const int ANCHO = 380, ALTO = 104, MARGEN = 28;

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
            // WinForms exige apartamento STA; sin esto la ventana ni se crea.
            hilo.SetApartmentState(ApartmentState.STA);
            hilo.IsBackground = true;
            hilo.Start();
            // Un poco más que la cuenta atrás, por si tarda en pintarse. Si algo
            // se queda colgado, el hilo es de fondo y no impide cerrar.
            hilo.Join(TimeSpan.FromSeconds(segundos + 3));
        }
        catch { /* ni con esas: el programa sigue igual */ }
    }

    private static void Correr(string titulo, string texto, int segundos)
    {
        // EN PÍXELES DE VERDAD. `GetWindowRect` devuelve píxeles físicos, y sin
        // esto un formulario de WinForms trabaja en píxeles lógicos: en una
        // pantalla al 150% el aviso saldría desplazado respecto a la ventana de
        // Arena, que es justo lo único que tiene que hacer bien.
        try { Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); }
        catch { /* ya estaba fijado: da igual, sólo se puede una vez */ }

        using var forma = new VentanaFlotante
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            TopMost = true,
            StartPosition = FormStartPosition.Manual,
            Size = new Size(ANCHO, ALTO),
            BackColor = Color.FromArgb(9, 13, 24),
            Opacity = 0.94,
        };
        forma.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, ANCHO + 1, ALTO + 1, 18, 18));
        forma.Location = Donde();

        // La marca arriba, en el ámbar del sitio: es lo que hace que se
        // reconozca de un vistazo sin tener que leer la frase.
        var marca = new Label
        {
            Text = "MTG CORNER",
            ForeColor = Color.FromArgb(252, 211, 77),
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            Location = new Point(18, 14),
            Size = new Size(ANCHO - 36, 14),
            BackColor = Color.Transparent,
        };
        var lineaTitulo = new Label
        {
            Text = titulo,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            Location = new Point(18, 32),
            Size = new Size(ANCHO - 36, 22),
            BackColor = Color.Transparent,
        };
        var lineaTexto = new Label
        {
            Text = texto,
            ForeColor = Color.FromArgb(203, 213, 225),
            Font = new Font("Segoe UI", 9f),
            Location = new Point(18, 56),
            Size = new Size(ANCHO - 36, 36),
            BackColor = Color.Transparent,
        };
        forma.Controls.Add(marca);
        forma.Controls.Add(lineaTitulo);
        forma.Controls.Add(lineaTexto);

        // Un filo ámbar a la izquierda, como las tarjetas de la web.
        forma.Paint += (_, e) =>
        {
            using var pincel = new SolidBrush(Color.FromArgb(245, 158, 11));
            e.Graphics.FillRectangle(pincel, 0, 0, 4, ALTO);
        };

        var reloj = new System.Windows.Forms.Timer { Interval = Math.Max(1, segundos) * 1000 };
        reloj.Tick += (_, _) => { reloj.Stop(); forma.Close(); };
        reloj.Start();

        Application.Run(forma);
        reloj.Dispose();
    }

    /// <summary>
    /// Arriba a la derecha de la ventana de Arena, o de la pantalla si no lo
    /// hay. Arriba y no abajo: la parte de abajo de Arena es donde está la
    /// mano, que es lo último que conviene tapar aunque no se pueda pulsar.
    /// </summary>
    private static Point Donde()
    {
        var arena = VentanaDeArena();
        if (arena != IntPtr.Zero && GetWindowRect(arena, out var r) && r.Right > r.Left)
        {
            return new Point(r.Right - ANCHO - MARGEN, r.Top + MARGEN);
        }
        var pantalla = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        return new Point(pantalla.Right - ANCHO - MARGEN, pantalla.Top + MARGEN);
    }

    /// <summary>
    /// La ventana en sí: transparente al ratón, fuera de la barra de tareas y
    /// sin robar el foco. Los tres estilos hacen falta a la vez, y ninguno se
    /// puede poner desde las propiedades normales del formulario.
    /// </summary>
    private sealed class VentanaFlotante : Form
    {
        protected override CreateParams CreateParams
        {
            get
            {
                var p = base.CreateParams;
                p.ExStyle |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return p;
            }
        }

        /// <summary>Aparecer sin activarse: el teclado se queda en el juego.</summary>
        protected override bool ShowWithoutActivation => true;
    }
}
