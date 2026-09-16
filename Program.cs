using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MtgCornerArenaBridge;

/// <summary>
/// LO QUE HACE ESTE PROGRAMA: confirmar quién eres en mtgcorner.com, leer de
/// MTG Arena tu colección, tus mazos y tus comodines, y mandarlo a tu cuenta
/// para que elijas en el navegador qué se guarda. No deja ningún fichero en tu
/// ordenador ni al acabar bien ni si algo falla, y sólo LEE.
///
/// UN ÚNICO EJECUTABLE. Hasta el 2026-09-15 esto venía en un zip con otro
/// programa al lado, mtga-tracker-daemon, que era quien leía la memoria: se
/// arrancaba, se le buscaba un puerto libre y se le hablaba por HTTP. Casi todos
/// los fallos reales salieron de ahí — el zip abierto sin extraer, una consola
/// anterior sin cerrar — así que su código vive ahora dentro (vendor/, GPLv3) y
/// se lee aquí mismo. Ver <see cref="LectorArena"/>.
///
/// DOS FUENTES DISTINTAS, a propósito:
///   · la colección sólo existe en la memoria de Arena, así que hay que leerla
///     con Arena abierto;
///   · los mazos y los comodines están en el Player.log, que se lee siempre,
///     incluso con Arena cerrado. Ver <see cref="LogArena"/>.
///
/// EL ORDEN IMPORTA: primero se confirma la sesión y SÓLO DESPUÉS se toca Arena.
/// Sin sesión confirmada no se lee nada — no hay motivo para sacar datos del
/// juego que no se van a poder guardar.
/// </summary>
internal static class Program
{
    private const string Sitio = "https://mtgcorner.com";
    private static readonly JsonSerializerOptions JsonOpciones = new(JsonSerializerDefaults.Web);

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        // Primera suposición: el idioma de Windows, que es lo único que se sabe
        // antes de hablar con nadie. En cuanto se confirma en el navegador, la
        // web dice en qué idioma se está jugando y manda ése. Ver Textos.cs.
        Textos.EscogerElDeWindows(IdiomaDeWindows());

        // Modos que no conectan con nada: comprobar el recorte del registro,
        // probar la lectura de la colección y volcar la licencia.
        if (args.Length > 0 && args[0] == "--probar-log") return ProbarLog(args.Skip(1).ToArray());
        if (args.Length > 0 && args[0] == "--probar-coleccion") return ProbarColeccion();
        if (args.Length > 0 && args[0] == "--licencia") return EscribirLicencia();

        Textos.Linea("titulo");
        Console.WriteLine("==================================");
        Console.WriteLine();

        // 10 s bastaba de sobra para iniciar/confirmar/consultar el vínculo,
        // pero la ÚLTIMA llamada —guardar— puede tardar de verdad: mtgcorner.com
        // traduce cada carta contra Scryfall en lotes, y una colección real son
        // miles. Con 10 s este cliente cortaba la petición antes de que el
        // servidor pudiera terminar.
        using var http = new HttpClient { BaseAddress = new Uri(Sitio), Timeout = TimeSpan.FromMinutes(4) };

        // ── 1. Quién eres, confirmado en tu navegador — nunca aquí ─────────
        string codigo;
        try
        {
            var r = await http.PostAsync("/api/mtga-device/iniciar", null);
            r.EnsureSuccessStatusCode();
            var d = await r.Content.ReadFromJsonAsync<RespuestaIniciar>(JsonOpciones);
            if (d is null) throw new Exception("respuesta vacía");
            codigo = d.Codigo;
        }
        catch (Exception ex)
        {
            Textos.Linea("sin_conexion", ex.Message);
            return Esperar(1);
        }

        var url = $"{Sitio}/vincular-dispositivo?codigo={codigo}";
        Textos.Linea("abriendo_navegador");
        Textos.Linea("si_no_abre", url);
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* el aviso de arriba con la URL ya basta si esto falla */ }

        Textos.Linea("esperando_confirmacion");
        var confirmado = await EsperarConfirmacion(http, codigo, TimeSpan.FromMinutes(3));
        if (!confirmado)
        {
            Console.WriteLine();
            Textos.Linea("no_confirmado");
            return Esperar(1);
        }
        // Tras confirmar, el visitante está mirando el navegador y aquí sigue
        // habiendo trabajo: esta ventana se trae al frente y, si Windows no deja,
        // al menos parpadea en la barra de tareas.
        TraerAlFrente();
        Textos.Linea("confirmado");
        Console.WriteLine();

        // ── 2. La colección, de la memoria de Arena ────────────────────────
        var coleccion = await LeerColeccion();
        Console.WriteLine();

        // ── 3. Mazos y comodines, del Player.log ───────────────────────────
        var carpeta = LogArena.Carpeta();
        Textos.Linea("leyendo_log");
        var log = LogArena.Leer(LogArena.FicherosPorDefecto(carpeta));
        if (!log.HayLog)
        {
            Textos.Linea("sin_log", carpeta);
            Textos.Linea("sin_log_2");
        }
        else if (!log.HayDetalle)
        {
            Textos.Linea("sin_detalle_1");
            Textos.Linea("sin_detalle_2");
            Textos.Linea("sin_detalle_3");
        }
        else
        {
            Textos.Linea("log_encontrado", log.InicioSesion, log.Ediciones);
        }

        if (coleccion is null && log.Fragmentos is null)
        {
            Console.WriteLine();
            Textos.Linea("nada_que_guardar");
            return Esperar(1);
        }

        // ── 4. A tu cuenta, con el mismo código ya confirmado ──────────────
        Console.WriteLine();
        Console.WriteLine(coleccion is null
            ? Textos.T("enviando")
            : Textos.T("enviando_con_cartas", coleccion.Length));
        var cuerpo = new PeticionImportar(codigo, null, [], coleccion, [], log.Fragmentos, Revisar: true);
        HttpResponseMessage resp;
        try
        {
            resp = await http.PostAsJsonAsync("/api/mtga-import", cuerpo, JsonOpciones);
        }
        catch (Exception ex)
        {
            Textos.Linea("sin_conexion_guardar", ex.Message);
            return Esperar(1);
        }
        if (!resp.IsSuccessStatusCode)
        {
            Textos.Linea("rechazado", (int)resp.StatusCode);
            return Esperar(1);
        }

        var respuesta = await resp.Content.ReadFromJsonAsync<RespuestaImportar>(JsonOpciones);

        // ── 5. Tú eliges en el navegador qué se guarda ─────────────────────
        // Un log trae TODOS los mazos de la cuenta de Arena, y uno que ya exista
        // en MTG Corner con el mismo nombre se reescribiría entero. Por eso el
        // servidor lo deja pendiente y nada se guarda hasta que marcas qué
        // quieres en /importar-arena?revisar=<id>.
        if (respuesta?.Pendiente is string pendiente)
        {
            var revisar = $"{Sitio}{RutaImportar()}?revisar={Uri.EscapeDataString(pendiente)}";
            var deColeccion = respuesta.Coleccion > 0 ? Textos.T("y_coleccion", respuesta.Coleccion) : "";
            Console.WriteLine();
            Textos.Linea("leidos_mazos", respuesta.Mazos ?? 0, deColeccion);
            Textos.Linea("abriendo_revision");
            Textos.Linea("si_no_abre", revisar);
            Textos.Linea("nada_hasta_confirmar");
            try { Process.Start(new ProcessStartInfo(revisar) { UseShellExecute = true }); }
            catch { /* la URL de arriba basta si esto falla */ }
            return Esperar(0);
        }

        // Un servidor anterior al paso de revisión guarda directamente y
        // devuelve el resumen de lo guardado.
        var mazos = (respuesta?.MazosGuardados ?? []).Where(n => n != "Arena").ToArray();
        Console.WriteLine();
        Textos.Linea("hecho");
        if (coleccion is not null) Textos.Linea("hecho_coleccion");
        if (mazos.Length > 0) Textos.Linea("hecho_mazos", mazos.Length, string.Join(", ", mazos.Take(6)) + (mazos.Length > 6 ? ", …" : ""));
        if (respuesta?.ComodinesGuardados == true) Textos.Linea("hecho_comodines");
        if (respuesta?.SinTraducir > 0) Textos.Linea("hecho_sin_traducir", respuesta.SinTraducir);
        return Esperar(0);
    }

    /// <summary>
    /// La colección de la memoria de Arena, esperando a que el juego esté
    /// abierto. <c>null</c> si no se pudo leer, ya explicado por consola: esto
    /// no corta el resto, porque los mazos y los comodines salen del log y se
    /// pueden guardar igual.
    /// </summary>
    private static async Task<CartaColeccion[]?> LeerColeccion()
    {
        if (!LectorArena.ArenaAbierto())
        {
            Textos.Linea("arena_cerrado");
            Textos.Linea("arena_cerrado_2");
            await LectorArena.EsperarArena(TimeSpan.FromMinutes(3));
        }

        if (!LectorArena.ArenaAbierto())
        {
            Textos.Linea("arena_no_abrio");
            Avisar();
            return null;
        }

        Textos.Linea("arena_detectado");
        for (var intento = 1; intento <= 3; intento++)
        {
            var cartas = LectorArena.LeerColeccion(out var error);
            if (cartas is not null)
            {
                Textos.Linea("coleccion_leida", cartas.Length);
                return cartas;
            }
            // Arena recién abierto responde sin cartas: se reintenta antes de
            // darlo por perdido.
            Textos.Linea("fallo_coleccion", intento, error);
            if (intento < 3) await Task.Delay(TimeSpan.FromSeconds(10));
        }

        // El recorrido paso a paso de la ruta que lee, que es lo único que
        // permite arreglarla si Arena la ha cambiado.
        Console.WriteLine();
        Console.WriteLine("Diagnosis (please send these lines to MTG Corner):");
        foreach (var linea in LectorArena.Diagnostico()) Console.WriteLine($"[diagnosis] {linea}");
        Avisar();
        return null;

        static void Avisar()
        {
            Console.WriteLine();
            Textos.Linea("aviso_sin_coleccion");
            Textos.Linea("aviso_sin_coleccion_2");
        }
    }

    /// <summary>
    /// <c>--probar-coleccion</c>: lee la colección y dice cuántas cartas salen, o
    /// el diagnóstico si falla. No conecta con mtgcorner.com ni guarda nada.
    /// </summary>
    private static int ProbarColeccion()
    {
        Console.WriteLine("MTG Arena open: " + LectorArena.ArenaAbierto());
        var cartas = LectorArena.LeerColeccion(out var error);
        if (cartas is not null)
        {
            Console.WriteLine($"Collection read: {cartas.Length} cards with copies, {cartas.Sum(c => c.Cantidad)} copies in total.");
            return 0;
        }
        Console.WriteLine("Couldn't read the collection: " + error);
        foreach (var linea in LectorArena.Diagnostico()) Console.WriteLine("[diagnosis] " + linea);
        return 1;
    }

    /// <summary>
    /// <c>--licencia</c>: deja el texto de la GPLv3 al lado del ejecutable. Va
    /// dentro del propio programa porque se distribuye como un único fichero.
    /// </summary>
    private static int EscribirLicencia()
    {
        try
        {
            using var recurso = Assembly.GetExecutingAssembly().GetManifestResourceStream("LICENCIA-GPLv3.txt");
            if (recurso is null)
            {
                Console.WriteLine("The license text isn't embedded in this build. See https://www.gnu.org/licenses/gpl-3.0.txt");
                return 1;
            }
            var destino = Path.Combine(Environment.CurrentDirectory, "LICENCIA-GPLv3.txt");
            using var fichero = File.Create(destino);
            recurso.CopyTo(fichero);
            Console.WriteLine("Written to " + destino);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Couldn't write the license: " + ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// <c>--probar-log [fichero o carpeta …] [--salida fichero]</c>: recorta el
    /// log como en una importación real y dice qué saldría, sin conectar con
    /// nada. Sin rutas, usa la carpeta de Arena de este ordenador.
    /// </summary>
    private static int ProbarLog(string[] args)
    {
        string? salida = null;
        var rutas = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--salida" && i + 1 < args.Length) { salida = args[++i]; continue; }
            rutas.Add(args[i]);
        }
        var ficheros = rutas.Count == 0
            ? LogArena.FicherosPorDefecto(LogArena.Carpeta())
            : rutas.SelectMany(r => Directory.Exists(r) ? LogArena.FicherosPorDefecto(r) : [r]).ToList();

        Console.WriteLine($"Arena folder on this computer: {LogArena.Carpeta()}");
        Console.WriteLine($"Review page for this computer's language: {Sitio}{RutaImportar()}?revisar=<id>");
        var log = LogArena.Leer(ficheros);
        Console.WriteLine($"Files read: {(log.Ficheros.Count == 0 ? "none" : string.Join(" | ", log.Ficheros))}");
        Console.WriteLine($"Detailed data: {log.HayDetalle} · logins: {log.InicioSesion} · deck changes: {log.Ediciones} · matches: {log.Partidas}");
        Console.WriteLine($"Would send: {(log.Fragmentos is null ? "nothing" : $"{Encoding.UTF8.GetByteCount(log.Fragmentos) / 1024} KB")}");
        if (salida is not null && log.Fragmentos is not null)
        {
            File.WriteAllText(salida, log.Fragmentos, new UTF8Encoding(false));
            Console.WriteLine($"Written to {salida}");
        }
        return log.Fragmentos is null ? 1 : 0;
    }

    /// <summary>
    /// Espera a que confirmes en el navegador. Al confirmar, la web dice además
    /// EN QUÉ IDIOMA lo has hecho —el del sitio, no el de tu Windows— y a partir
    /// de ahí el programa habla en ése y abre la revisión en ése. Ver Textos.cs.
    /// </summary>
    private static async Task<bool> EsperarConfirmacion(HttpClient http, string codigo, TimeSpan plazo)
    {
        var limite = DateTime.UtcNow + plazo;
        while (DateTime.UtcNow < limite)
        {
            try
            {
                var r = await http.GetFromJsonAsync<RespuestaEstado>($"/api/mtga-device/estado?codigo={codigo}", JsonOpciones);
                if (r?.Confirmado == true) { Textos.Escoger(r.Idioma); return true; }
            }
            catch { /* un fallo de red suelto no corta la espera */ }
            await Task.Delay(2000);
        }
        return false;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr ventana);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr ventana, int comando);

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FLASHWINFO info);

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    /// <summary>
    /// Trae esta ventana al frente cuando vuelve a haber algo que mirar aquí.
    /// Windows no siempre deja robar el foco a otra aplicación, así que además
    /// se hace parpadear en la barra de tareas, que es lo que sí funciona
    /// siempre.
    /// </summary>
    private static void TraerAlFrente()
    {
        try
        {
            var ventana = GetConsoleWindow();
            if (ventana == IntPtr.Zero) return;
            ShowWindow(ventana, 9); // SW_RESTORE
            SetForegroundWindow(ventana);
            var info = new FLASHWINFO
            {
                cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
                hwnd = ventana,
                dwFlags = 3 | 12, // FLASHW_ALL | FLASHW_TIMERNOFG
                uCount = 5,
                dwTimeout = 0,
            };
            FlashWindowEx(ref info);
        }
        catch { /* si no se puede, la consola sigue ahí esperando */ }
    }

    /// <summary>
    /// La página de importar EN EL IDIOMA DEL PROCESO — el que dijo la web al
    /// confirmar, o el de Windows mientras no se sabe. La ruta está traducida
    /// (i18n/routing.ts, '/importar-arena') y el inglés, idioma por defecto del
    /// sitio, va sin prefijo.
    /// </summary>
    private static string RutaImportar() => Textos.Idioma switch
    {
        "es" => "/es/importar-arena",
        "de" => "/de/arena-importieren",
        "fr" => "/fr/importer-arena",
        "pt" => "/pt/importar-arena",
        "it" => "/it/importa-arena",
        "ja" => "/ja/import-arena",
        "zh" => "/zh/import-arena",
        _ => "/import-arena",
    };

    /// <summary>
    /// El identificador de idioma primario de Windows. Con InvariantGlobalization
    /// la cultura de .NET no dice nada, así que se pregunta al sistema.
    /// </summary>
    private static int IdiomaDeWindows()
    {
        try { return GetUserDefaultUILanguage() & 0x3FF; }
        catch { return 0; }
    }

    [DllImport("kernel32.dll")]
    private static extern ushort GetUserDefaultUILanguage();

    /// <summary>Segundos de cortesía antes de cerrarse solo cuando todo ha ido bien.</summary>
    private const int SegundosParaCerrar = 10;

    /// <summary>
    /// El final: se cierra SOLO cuando ha ido bien, y espera cuando no.
    ///
    /// Antes se quedaba siempre en «pulsa una tecla», y una ventana negra que no
    /// se va parece que aún hace falta algo, cuando el trabajo ya está hecho y
    /// la persona está en el navegador eligiendo qué guardar. Con éxito cuenta
    /// atrás y se cierra; cualquier tecla lo cierra ya.
    ///
    /// SI ALGO FALLÓ, SE QUEDA. El mensaje de error es lo único que explica qué
    /// pasó —y lo que se copia para contarlo—, así que esa ventana no se cierra
    /// sola pase lo que pase.
    /// </summary>
    private static int Esperar(int codigo)
    {
        Console.WriteLine();
        if (codigo != 0)
        {
            Textos.Linea("pulsa_tecla");
            try { Console.ReadKey(true); }
            catch { /* sin consola interactiva (lanzado desde un script): no se espera */ }
            return codigo;
        }

        try
        {
            for (var quedan = SegundosParaCerrar; quedan > 0; quedan--)
            {
                // `\r` y espacios al final: la cuenta atrás se reescribe sobre sí
                // misma en vez de dejar diez líneas seguidas.
                Console.Write("\r" + Textos.T("cerrando", quedan) + "   ");
                for (var i = 0; i < 10; i++)
                {
                    if (Console.KeyAvailable) { Console.ReadKey(true); Console.WriteLine(); return codigo; }
                    Thread.Sleep(100);
                }
            }
            Console.WriteLine();
        }
        catch { /* sin consola interactiva no hay cuenta atrás: se cierra y ya */ }
        return codigo;
    }
}

/// <summary>
/// EL <c>Player.log</c> DE ARENA, SIN PREGUNTAR DÓNDE ESTÁ.
///
/// Arena es un juego de Unity, y Unity escribe el log en una carpeta que sale
/// del nombre de la empresa y del producto, NO de dónde esté instalado el
/// juego: es la misma con el instalador de Wizards, con Steam y con Epic.
/// En Windows, <c>%USERPROFILE%\AppData\LocalLow\Wizards Of The Coast\MTGA</c>.
/// Al lado queda <c>Player-prev.log</c>, el de la sesión anterior: Arena lo
/// renombra al volver a abrirse.
///
/// Se leen LOS DOS, primero el anterior. Arena guarda los mazos en una caché
/// propia y sólo manda las listas cuando esa caché está desfasada: en un log
/// real de 2026-09-15 venían en el primer inicio de sesión y en los otros
/// cinco no. Con Arena recién reabierto, las listas pueden estar sólo en el
/// log anterior.
///
/// Y SÓLO LO QUE IMPORTA. De cada inicio de sesión (la respuesta de
/// <c>StartHook</c>) se quedan los cuatro comodines, los nombres y formatos
/// de TUS mazos y sus listas — fuera los precon de Arena, cosméticos, logros,
/// definiciones de fichas y el resto, que es casi todo. De la sesión, las
/// líneas en las que editas o juegas un mazo. Nunca el fichero entero: en él
/// hay también las partidas, con los nombres de los rivales.
/// </summary>
internal static partial class LogArena
{
    public sealed record Resultado(
        string? Fragmentos,
        bool HayLog,
        bool HayDetalle,
        int InicioSesion,
        int Ediciones,
        int Partidas,
        IReadOnlyList<string> Ficheros);

    private static readonly string[] Comodines = ["WildCardCommons", "WildCardUnCommons", "WildCardRares", "WildCardMythics"];
    private const string PrefijoPrecon = "?=?Loc/";

    // FOLDERID_LocalAppDataLow. Se pide al sistema en vez de componer la ruta
    // a mano: si el perfil está redirigido, la carpeta de verdad es otra.
    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

    /// <summary>La carpeta donde Arena deja su log en este ordenador.</summary>
    public static string Carpeta()
    {
        string? localLow = null;
        try
        {
            if (SHGetKnownFolderPath(new Guid("A520A1A4-1780-4FF6-BD18-167343C5AF16"), 0, IntPtr.Zero, out var p) == 0)
            {
                localLow = Marshal.PtrToStringUni(p);
                Marshal.FreeCoTaskMem(p);
            }
        }
        catch { /* sin shell32 o sin la carpeta conocida: se compone a mano */ }
        localLow ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow");
        return Path.Combine(localLow, "Wizards Of The Coast", "MTGA");
    }

    /// <summary>Los dos logs de una carpeta, el anterior primero.</summary>
    public static List<string> FicherosPorDefecto(string carpeta) =>
        [Path.Combine(carpeta, "Player-prev.log"), Path.Combine(carpeta, "Player.log")];

    [GeneratedRegex(@"(?:^|\])<==\s*StartHook\b")]
    private static partial Regex CabeceraStartHook();

    public static Resultado Leer(IEnumerable<string> ficheros)
    {
        var sb = new StringBuilder();
        int inicios = 0, ediciones = 0, partidas = 0;
        bool hayLog = false, hayDetalle = false;
        var leidos = new List<string>();

        foreach (var fichero in ficheros)
        {
            if (!File.Exists(fichero)) continue;
            hayLog = true;
            string[] lineas;
            try { lineas = LeerCompartido(fichero); }
            catch (Exception ex)
            {
                Textos.Linea("no_pude_leer", fichero, ex.Message);
                continue;
            }
            leidos.Add(fichero);

            for (var i = 0; i < lineas.Length; i++)
            {
                var linea = lineas[i];
                if (CabeceraStartHook().IsMatch(linea))
                {
                    hayDetalle = true;
                    var llave = linea.IndexOf('{');
                    var json = llave >= 0 ? linea[llave..] : (i + 1 < lineas.Length ? lineas[i + 1] : "");
                    var recortado = RecortarStartHook(json);
                    if (recortado is null) continue;
                    sb.Append("<== StartHook\n").Append(recortado).Append('\n');
                    inicios++;
                    continue;
                }
                if (linea.Contains("==> DeckUpsertDeckV3 ") || linea.Contains("==> EventSetDeckV3 "))
                {
                    hayDetalle = true;
                    sb.Append(linea).Append('\n');
                    ediciones++;
                    continue;
                }
                /**
                 * UNA PARTIDA TERMINADA, REDUCIDA AQUÍ Y NO EN EL SERVIDOR.
                 *
                 * El resto de líneas viajan tal cual —el servidor las analiza,
                 * y así un cambio de formato se arregla en un solo sitio—, pero
                 * ésta no puede: el aviso de fin de partida lleva el NOMBRE y el
                 * identificador de tu rival, y eso no tiene por qué salir de tu
                 * ordenador para contar quién ganó. Se queda el marcador: la
                 * partida, el evento, tu equipo, el equipo que ganó y cuándo.
                 *
                 * QUIÉN ERES TÚ sale de la línea de cabecera —Arena escribe
                 * "Match to <tu id>:"— y es la única forma de saber cuál de los
                 * dos equipos es el tuyo una vez quitados los nombres.
                 */
                if (linea.Contains("\"finalMatchResult\"") || (linea.Contains("Match to ") && i + 1 < lineas.Length && lineas[i + 1].Contains("\"finalMatchResult\"")))
                {
                    hayDetalle = true;
                    var conJson = linea.Contains("\"finalMatchResult\"") ? linea : lineas[i + 1];
                    var cabecera = linea.Contains("\"finalMatchResult\"") && i > 0 ? lineas[i - 1] : linea;
                    var llave = conJson.IndexOf('{');
                    if (llave < 0) continue;
                    var marcador = RecortarPartida(conJson[llave..], MiIdentificador(cabecera));
                    if (marcador is null) continue;
                    sb.Append("<== PartidaTerminada\n").Append(marcador).Append('\n');
                    partidas++;
                    continue;
                }
                // Cualquier llamada con JSON dice que los registros detallados
                // están activados, aunque no haya inicio de sesión en este log.
                if (!hayDetalle && (linea.Contains("==> ") || linea.Contains("<== "))) hayDetalle = true;
            }
        }

        return new Resultado(sb.Length > 0 ? sb.ToString() : null, hayLog, hayDetalle, inicios, ediciones, partidas, leidos);
    }

    /// <summary>
    /// Arena tiene el log abierto mientras juega: se abre compartido para
    /// lectura y escritura, o Windows no dejaría leerlo con el juego en marcha.
    /// </summary>
    private static string[] LeerCompartido(string fichero)
    {
        using var fs = new FileStream(fichero, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs, Encoding.UTF8);
        return sr.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
    }

    /// <summary>
    /// Tu identificador de Arena, sacado de la cabecera "Match to &lt;id&gt;:" que
    /// el juego escribe delante del aviso. Es lo único que distingue tu equipo
    /// del suyo cuando ya no hay nombres.
    /// </summary>
    private static string? MiIdentificador(string cabecera)
    {
        var i = cabecera.IndexOf("Match to ", StringComparison.Ordinal);
        if (i < 0) return null;
        var resto = cabecera[(i + 9)..];
        var fin = resto.IndexOf(':');
        var id = (fin > 0 ? resto[..fin] : resto).Trim();
        return id.Length > 0 ? id : null;
    }

    /// <summary>
    /// El marcador de una partida terminada: quién ganó, en qué evento y
    /// cuándo. SIN NOMBRES NI IDENTIFICADORES DE NADIE — ni del rival ni tuyo:
    /// del tuyo sólo se usa aquí dentro, para saber qué equipo era el tuyo, y
    /// lo que sale es un número de equipo.
    ///
    /// <c>null</c> si la línea viene cortada, si no trae resultado de partida
    /// (hay avisos intermedios con la misma forma) o si no se sabe cuál era tu
    /// equipo: media partida no se puede contar.
    /// </summary>
    private static string? RecortarPartida(string json, string? miId)
    {
        try
        {
            var raiz = JsonNode.Parse(json)?.AsObject();
            var sala = raiz?["matchGameRoomStateChangedEvent"]?["gameRoomInfo"];
            var config = sala?["gameRoomConfig"];
            var resultado = sala?["finalMatchResult"];
            if (config is null || resultado is null) return null;

            var matchId = Texto(resultado["matchId"]) ?? Texto(config["matchId"]);
            if (matchId is null) return null;

            int? miEquipo = null;
            string? evento = null;
            if (config["reservedPlayers"] is JsonArray jugadores)
            {
                foreach (var j in jugadores)
                {
                    var id = Texto(j?["userId"]);
                    evento ??= Texto(j?["eventId"]);
                    if (miId is not null && id == miId && j?["teamId"] is JsonValue t && t.TryGetValue<int>(out var equipo))
                    {
                        miEquipo = equipo;
                        evento = Texto(j?["eventId"]) ?? evento;
                    }
                }
            }
            if (miEquipo is null) return null;

            // El resultado de la PARTIDA, no el de cada juego suelto: un mejor
            // de tres trae una línea por juego y otra del conjunto.
            int? gana = null;
            string? motivo = null;
            if (resultado["resultList"] is JsonArray lista)
            {
                foreach (var r in lista)
                {
                    if (Texto(r?["scope"]) != "MatchScope_Match") continue;
                    if (r?["winningTeamId"] is JsonValue w && w.TryGetValue<int>(out var equipo)) gana = equipo;
                    motivo = Texto(r?["reason"]);
                }
            }
            if (gana is null) return null;

            var marcador = new JsonObject
            {
                ["id"] = matchId,
                ["evento"] = evento,
                ["mio"] = miEquipo,
                ["gana"] = gana,
                ["cuando"] = Texto(raiz?["timestamp"]),
                ["motivo"] = motivo,
            };
            return marcador.ToJsonString();
        }
        catch { return null; }
    }

    private static string? Texto(JsonNode? n) =>
        n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>
    /// La respuesta de StartHook reducida a lo que lee lib/mtgaLog.ts, con los
    /// mismos nombres de campo que trae (Decks o DecksInternal, DeckId o
    /// DeckIdInternal, DeckSummaries o DeckSummariesV2). <c>null</c> si la
    /// línea viene cortada y no es JSON.
    /// </summary>
    private static string? RecortarStartHook(string json)
    {
        JsonNode? nodo;
        try { nodo = JsonNode.Parse(json); }
        catch { return null; }
        if (nodo is not JsonObject hook) return null;

        var salida = new JsonObject();

        if (hook["InventoryInfo"] is JsonObject inventario)
        {
            var wc = new JsonObject();
            foreach (var k in Comodines)
                if (inventario[k] is JsonNode v) wc[k] = v.DeepClone();
            salida["InventoryInfo"] = wc;
        }

        var propios = new HashSet<string>();
        var claveResumenes = hook.ContainsKey("DeckSummariesV2") ? "DeckSummariesV2" : "DeckSummaries";
        if (hook[claveResumenes] is JsonArray resumenes)
        {
            var lista = new JsonArray();
            foreach (var r in resumenes)
            {
                if (r is not JsonObject resumen) continue;
                var nombre = Texto(resumen["Name"]);
                if (nombre is null || nombre.StartsWith(PrefijoPrecon, StringComparison.Ordinal)) continue;
                var copia = new JsonObject { ["Name"] = nombre };
                foreach (var clave in new[] { "DeckId", "DeckIdInternal" })
                {
                    var id = Texto(resumen[clave]);
                    if (id is null) continue;
                    copia[clave] = id;
                    propios.Add(id);
                }
                if (resumen["Attributes"] is JsonArray atributos)
                {
                    // Formato y fechas de última partida y última edición: con
                    // ellas la web ordena los mazos y marca de salida los recientes.
                    var utiles = new JsonArray();
                    foreach (var a in atributos.OfType<JsonObject>())
                    {
                        var nombreAtributo = Texto(a["name"])?.ToLowerInvariant();
                        if (nombreAtributo is "format" or "lastplayed" or "lastupdated") utiles.Add(a.DeepClone());
                    }
                    if (utiles.Count > 0) copia["Attributes"] = utiles;
                }
                lista.Add(copia);
            }
            salida[claveResumenes] = lista;
        }

        var claveListas = hook.ContainsKey("DecksInternal") ? "DecksInternal" : "Decks";
        if (hook[claveListas] is JsonObject listas)
        {
            var reducidas = new JsonObject();
            foreach (var (id, valor) in listas)
            {
                if (!propios.Contains(id) || valor is not JsonObject mazo) continue;
                var copia = new JsonObject();
                if (mazo["MainDeck"] is JsonNode principal) copia["MainDeck"] = principal.DeepClone();
                if (mazo["Sideboard"] is JsonNode banca) copia["Sideboard"] = banca.DeepClone();
                reducidas[id] = copia;
            }
            salida[claveListas] = reducidas;
        }

        return salida.ToJsonString();
    }
}

// ─── mtgcorner.com — el vínculo de dispositivo ──────────────────────────────

internal sealed record RespuestaIniciar([property: JsonPropertyName("codigo")] string Codigo);
/// <summary>
/// El estado del código de vinculación. <c>Idioma</c> es el del SITIO donde se
/// confirmó ("es", "ja"…), no el de Windows: con él, el resto del proceso —esta
/// consola y la página de revisión— va en una sola lengua. Llega nulo si el
/// servidor todavía no lo guarda (sql/2026-09-16-mtga-device-idioma.sql).
/// </summary>
internal sealed record RespuestaEstado(
    [property: JsonPropertyName("confirmado")] bool Confirmado,
    [property: JsonPropertyName("idioma")] string? Idioma = null);

/// <summary>La respuesta de /api/mtga-import: con el paso de revisión, <c>pendiente</c>
/// y los recuentos; un servidor anterior, el resumen de lo guardado.</summary>
internal sealed record RespuestaImportar(
    [property: JsonPropertyName("pendiente")] string? Pendiente,
    [property: JsonPropertyName("mazos")] int? Mazos,
    [property: JsonPropertyName("coleccion")] int? Coleccion,
    [property: JsonPropertyName("cartasGuardadas")] int? CartasGuardadas,
    [property: JsonPropertyName("sinTraducir")] int? SinTraducir,
    [property: JsonPropertyName("comodinesGuardados")] bool? ComodinesGuardados,
    [property: JsonPropertyName("mazosGuardados")] string[]? MazosGuardados);

// ─── lo que espera /api/mtga-import (lib/mtgaLog.ts) ────────────────────────

internal sealed record CartaColeccion(
    [property: JsonPropertyName("arenaId")] int ArenaId,
    [property: JsonPropertyName("cantidad")] int Cantidad);

/// <summary>El cuerpo de /api/mtga-import. <c>FragmentosLog</c> son los trozos
/// del Player.log que analiza el servidor (ver <see cref="LogArena"/>).</summary>
internal sealed record PeticionImportar(
    [property: JsonPropertyName("codigo")] string Codigo,
    [property: JsonPropertyName("comodines")] object? Comodines,
    [property: JsonPropertyName("mazos")] object[] Mazos,
    [property: JsonPropertyName("coleccion")] CartaColeccion[]? Coleccion,
    [property: JsonPropertyName("avisos")] string[] Avisos,
    [property: JsonPropertyName("fragmentosLog")] string? FragmentosLog,
    /// <summary>Que el servidor lo deje pendiente para elegir en el navegador, sin guardar.</summary>
    [property: JsonPropertyName("revisar")] bool Revisar
);
