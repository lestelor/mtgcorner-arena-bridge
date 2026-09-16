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

        // Modos que no conectan con nada: comprobar el recorte del registro,
        // probar la lectura de la colección y volcar la licencia.
        if (args.Length > 0 && args[0] == "--probar-log") return ProbarLog(args.Skip(1).ToArray());
        if (args.Length > 0 && args[0] == "--probar-coleccion") return ProbarColeccion();
        if (args.Length > 0 && args[0] == "--licencia") return EscribirLicencia();

        Console.WriteLine("MTG Corner — bridge to MTG Arena");
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
            Console.WriteLine($"Couldn't reach MTG Corner: {ex.Message}");
            return Esperar(1);
        }

        var url = $"{Sitio}/vincular-dispositivo?codigo={codigo}";
        Console.WriteLine("Opening your browser to confirm it's you…");
        Console.WriteLine($"If it doesn't open on its own, go to: {url}");
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* el aviso de arriba con la URL ya basta si esto falla */ }

        Console.WriteLine("Waiting for confirmation (up to 3 minutes)…");
        var confirmado = await EsperarConfirmacion(http, codigo, TimeSpan.FromMinutes(3));
        if (!confirmado)
        {
            Console.WriteLine();
            Console.WriteLine("Not confirmed in time. Nothing was read or saved.");
            return Esperar(1);
        }
        // Tras confirmar, el visitante está mirando el navegador y aquí sigue
        // habiendo trabajo: esta ventana se trae al frente y, si Windows no deja,
        // al menos parpadea en la barra de tareas.
        TraerAlFrente();
        Console.WriteLine("Confirmed.");
        Console.WriteLine();

        // ── 2. La colección, de la memoria de Arena ────────────────────────
        var coleccion = await LeerColeccion();
        Console.WriteLine();

        // ── 3. Mazos y comodines, del Player.log ───────────────────────────
        var carpeta = LogArena.Carpeta();
        Console.WriteLine("Reading your decks and wildcards from Arena's log…");
        var log = LogArena.Leer(LogArena.FicherosPorDefecto(carpeta));
        if (!log.HayLog)
        {
            Console.WriteLine($"Couldn't find Arena's Player.log in {carpeta}.");
            Console.WriteLine("Decks and wildcards won't be updated this time.");
        }
        else if (!log.HayDetalle)
        {
            Console.WriteLine("Arena's log doesn't have the detailed data needed for your decks.");
            Console.WriteLine("In Arena: gear icon → Account → check \"Detailed Logs (Plugin Support)\",");
            Console.WriteLine("restart Arena, and run this program again to import your decks.");
        }
        else
        {
            Console.WriteLine($"Found {log.InicioSesion} login(s) and {log.Ediciones} deck change(s) in the log.");
        }

        if (coleccion is null && log.Fragmentos is null)
        {
            Console.WriteLine();
            Console.WriteLine("There's nothing to save — neither the collection nor the log could be read.");
            return Esperar(1);
        }

        // ── 4. A tu cuenta, con el mismo código ya confirmado ──────────────
        Console.WriteLine();
        Console.WriteLine(coleccion is null
            ? "Sending your decks and wildcards to MTG Corner…"
            : $"Sending {coleccion.Length} cards, your decks and wildcards to MTG Corner…");
        var cuerpo = new PeticionImportar(codigo, null, [], coleccion, [], log.Fragmentos, Revisar: true);
        HttpResponseMessage resp;
        try
        {
            resp = await http.PostAsJsonAsync("/api/mtga-import", cuerpo, JsonOpciones);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Couldn't reach MTG Corner to save: {ex.Message}. Nothing was saved.");
            return Esperar(1);
        }
        if (!resp.IsSuccessStatusCode)
        {
            Console.WriteLine($"MTG Corner didn't accept the import (code {(int)resp.StatusCode}). Nothing was saved.");
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
            var deColeccion = respuesta.Coleccion > 0 ? $" and {respuesta.Coleccion} distinct cards of your collection" : "";
            Console.WriteLine();
            Console.WriteLine($"Read {respuesta.Mazos ?? 0} deck(s){deColeccion}.");
            Console.WriteLine("Opening your browser so you can choose what to save…");
            Console.WriteLine($"If it doesn't open on its own, go to: {revisar}");
            Console.WriteLine("Nothing is saved until you confirm there. The link expires in an hour.");
            try { Process.Start(new ProcessStartInfo(revisar) { UseShellExecute = true }); }
            catch { /* la URL de arriba basta si esto falla */ }
            return Esperar(0);
        }

        // Un servidor anterior al paso de revisión guarda directamente y
        // devuelve el resumen de lo guardado.
        var mazos = (respuesta?.MazosGuardados ?? []).Where(n => n != "Arena").ToArray();
        Console.WriteLine();
        Console.WriteLine("Done.");
        if (coleccion is not null) Console.WriteLine("  Collection: saved as \"Arena\".");
        if (mazos.Length > 0) Console.WriteLine($"  Decks: {mazos.Length} saved ({string.Join(", ", mazos.Take(6))}{(mazos.Length > 6 ? ", …" : "")}).");
        if (respuesta?.ComodinesGuardados == true) Console.WriteLine("  Wildcards: updated.");
        if (respuesta?.SinTraducir > 0) Console.WriteLine($"  ({respuesta.SinTraducir} cards weren't recognized — they might be very new.)");
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
            Console.WriteLine("MTG Arena isn't open. Open it now and wait until it has fully loaded…");
            Console.WriteLine("(up to 3 minutes; your decks and wildcards will be read either way)");
            await LectorArena.EsperarArena(TimeSpan.FromMinutes(3));
        }

        if (!LectorArena.ArenaAbierto())
        {
            Console.WriteLine("Arena wasn't open in time, so your full collection can't be read this time.");
            Avisar();
            return null;
        }

        Console.WriteLine("Arena detected. Reading your collection…");
        for (var intento = 1; intento <= 3; intento++)
        {
            var cartas = LectorArena.LeerColeccion(out var error);
            if (cartas is not null)
            {
                Console.WriteLine($"Read {cartas.Length} cards from your collection.");
                return cartas;
            }
            // Arena recién abierto responde sin cartas: se reintenta antes de
            // darlo por perdido.
            Console.WriteLine($"Couldn't read the collection (attempt {intento} of 3): {error}");
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
            Console.WriteLine("!! Your full collection (\"Arena\") will NOT be updated this time.");
            Console.WriteLine("   Your decks and wildcards can still be saved now.");
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
        Console.WriteLine($"Detailed data: {log.HayDetalle} · logins: {log.InicioSesion} · deck changes: {log.Ediciones}");
        Console.WriteLine($"Would send: {(log.Fragmentos is null ? "nothing" : $"{Encoding.UTF8.GetByteCount(log.Fragmentos) / 1024} KB")}");
        if (salida is not null && log.Fragmentos is not null)
        {
            File.WriteAllText(salida, log.Fragmentos, new UTF8Encoding(false));
            Console.WriteLine($"Written to {salida}");
        }
        return log.Fragmentos is null ? 1 : 0;
    }

    private static async Task<bool> EsperarConfirmacion(HttpClient http, string codigo, TimeSpan plazo)
    {
        var limite = DateTime.UtcNow + plazo;
        while (DateTime.UtcNow < limite)
        {
            try
            {
                var r = await http.GetFromJsonAsync<RespuestaEstado>($"/api/mtga-device/estado?codigo={codigo}", JsonOpciones);
                if (r?.Confirmado == true) return true;
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
    /// La página de importar en el idioma de Windows de quien lo ejecuta. La
    /// ruta está traducida (i18n/routing.ts, '/importar-arena') y el inglés,
    /// idioma por defecto del sitio, va sin prefijo. Con InvariantGlobalization
    /// la cultura de .NET no dice nada, así que se pregunta a Windows.
    /// </summary>
    private static string RutaImportar()
    {
        int primario;
        try { primario = GetUserDefaultUILanguage() & 0x3FF; }
        catch { primario = 0; }
        return primario switch
        {
            0x0A => "/es/importar-arena",
            0x07 => "/de/arena-importieren",
            0x0C => "/fr/importer-arena",
            0x16 => "/pt/importar-arena",
            0x10 => "/it/importa-arena",
            0x11 => "/ja/import-arena",
            0x04 => "/zh/import-arena",
            _ => "/import-arena",
        };
    }

    [DllImport("kernel32.dll")]
    private static extern ushort GetUserDefaultUILanguage();

    private static int Esperar(int codigo)
    {
        Console.WriteLine();
        Console.WriteLine("Press a key to close…");
        try { Console.ReadKey(); }
        catch { /* sin consola interactiva (lanzado desde un script): no se espera */ }
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
        int inicios = 0, ediciones = 0;
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
                Console.WriteLine($"Couldn't read {fichero}: {ex.Message}");
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
                // Cualquier llamada con JSON dice que los registros detallados
                // están activados, aunque no haya inicio de sesión en este log.
                if (!hayDetalle && (linea.Contains("==> ") || linea.Contains("<== "))) hayDetalle = true;
            }
        }

        return new Resultado(sb.Length > 0 ? sb.ToString() : null, hayLog, hayDetalle, inicios, ediciones, leidos);
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
internal sealed record RespuestaEstado([property: JsonPropertyName("confirmado")] bool Confirmado);

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
