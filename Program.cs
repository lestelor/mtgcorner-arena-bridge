using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MtgCornerArenaBridge;

/// <summary>
/// LO QUE HACE ESTE PROGRAMA: confirmar quién eres en mtgcorner.com, leer tu
/// colección de Arena con mtga-tracker-daemon (de terceros, GPLv3,
/// https://github.com/frcaton/mtga-tracker-daemon), sacar tus mazos y
/// comodines del `Player.log` de Arena, y mandarlo todo directamente a tu
/// cuenta. No queda ningún fichero en tu ordenador ni al acabar bien ni si algo
/// falla.
///
/// NO LEE MEMORIA DE NADA POR SU CUENTA. Eso es justo lo que hace el daemon,
/// y reescribirlo aquí sería reinventar algo ya hecho, mantenido y probado
/// por otra gente. Este programa es sólo el pegamento.
///
/// EL LOG NO SE ANALIZA AQUÍ. Se recortan los trozos que importan (ver
/// <see cref="LogArena"/>) y los analiza el servidor con el mismo código que
/// usa la página web al arrastrar el fichero (lib/mtgaLog.ts). Un analizador
/// duplicado en C# se habría quedado roto el día que Arena cambiara el formato
/// y sólo se arreglara el de la web — que es exactamente lo que pasó en
/// septiembre de 2026 con el de la web.
///
/// EL ORDEN IMPORTA: primero se confirma la sesión, y SÓLO DESPUÉS se toca
/// Arena. Sin sesión confirmada, nunca se llega a leer nada — no hay ningún
/// motivo para sacar datos del juego que no se van a poder guardar.
/// </summary>
internal static class Program
{
    private const string Sitio = "https://mtgcorner.com";
    private static readonly JsonSerializerOptions JsonOpciones = new(JsonSerializerDefaults.Web);

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        // Modo de prueba: lee y recorta el log SIN conectar con nada ni
        // arrancar el daemon. Sirve para comprobar el recorte contra un log
        // real y para soporte ("¿qué mandaría con mi log?").
        if (args.Length > 0 && args[0] == "--probar-log") return ProbarLog(args.Skip(1).ToArray());

        Console.WriteLine("MTG Corner — bridge to MTG Arena");
        Console.WriteLine("==================================");
        Console.WriteLine();

        // 10 s bastaba de sobra para iniciar/confirmar/consultar el vínculo,
        // pero la ÚLTIMA llamada —guardar la colección— puede tardar de
        // verdad: mtgcorner.com traduce cada arena_id contra Scryfall en
        // lotes de 75, a su ritmo, y una colección real son miles. Con sólo
        // 10 s este cliente cortaba esa petición él solo antes de que el
        // servidor pudiera siquiera terminar — daba error y no se guardaba
        // nada, aunque el servidor sí hubiera podido acabar con más tiempo.
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
        Console.WriteLine("Confirmed.");
        Console.WriteLine();

        // ── 2. Sólo ahora se toca Arena: la colección, de su memoria ────────
        // Si esto falla no se corta todo: los mazos y los comodines salen del
        // log, que existe aunque Arena esté cerrado o el lector no arranque.
        var coleccion = await LeerColeccionDeArena();
        Console.WriteLine();

        // ── 3. Mazos y comodines, del Player.log ────────────────────────────
        // DESPUÉS de la colección y no antes: si Arena acaba de abrirse, el
        // inicio de sesión (donde vienen los mazos) se escribe en el log
        // mientras se espera al lector.
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

        // ── 4. Directo a tu cuenta, con el mismo código ya confirmado ──────
        Console.WriteLine();
        Console.WriteLine(coleccion is null
            ? "Saving your decks and wildcards to MTG Corner…"
            : $"Saving {coleccion.Length} cards, your decks and wildcards to MTG Corner…");
        Console.WriteLine("(a large collection can take more than a minute — keep waiting)");
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

        // ── 5. Tú eliges en el navegador qué se guarda ──────────────────────
        // Un log trae TODOS los mazos de la cuenta de Arena, y uno que ya
        // exista en MTG Corner con el mismo nombre se reescribiría entero. Por
        // eso el servidor lo deja pendiente y nada se guarda hasta que marcas
        // qué mazos quieres en /importar-arena?revisar=<id>.
        if (respuesta?.Pendiente is string pendiente)
        {
            var revisar = $"{Sitio}{RutaImportar()}?revisar={Uri.EscapeDataString(pendiente)}";
            var deColeccion = respuesta.Coleccion > 0 ? $" and {respuesta.Coleccion} distinct cards of your collection" : "";
            Console.WriteLine();
            Console.WriteLine($"Read {respuesta.Mazos ?? 0} deck(s){deColeccion}.");
            Console.WriteLine("Opening your browser so you can choose which decks to save…");
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
        if (coleccion is not null) Console.WriteLine("  Collection: saved to your \"Arena\" deck.");
        if (mazos.Length > 0) Console.WriteLine($"  Decks: {mazos.Length} saved ({string.Join(", ", mazos.Take(6))}{(mazos.Length > 6 ? ", …" : "")}).");
        if (respuesta?.ComodinesGuardados == true) Console.WriteLine("  Wildcards: updated.");
        if (respuesta?.SinTraducir > 0) Console.WriteLine($"  ({respuesta.SinTraducir} cards weren't recognized — they might be very new.)");
        return Esperar(0);
    }

    /// <summary>
    /// Arranca mtga-tracker-daemon, espera a que vea Arena y le pide la
    /// colección. Devuelve <c>null</c> si algo falla, ya explicado por
    /// consola; el daemon se cierra siempre al salir de aquí.
    /// </summary>
    private static async Task<CartaColeccion[]?> LeerColeccionDeArena()
    {
        var rutaDaemon = Path.Combine(AppContext.BaseDirectory, "mtga-tracker-daemon.exe");
        if (!File.Exists(rutaDaemon))
        {
            Console.WriteLine($"Can't find {rutaDaemon}.");
            Console.WriteLine("This program expects to be next to mtga-tracker-daemon.exe in the same folder.");
            Console.WriteLine("Continuing with your decks and wildcards only.");
            return null;
        }

        // Puerto libre DE VERDAD, no uno fijo. El 9000 —el que llevaba éste
        // antes— chocó en la primera prueba real con otra cosa ya escuchando
        // ahí en el ordenador de un usuario; el daemon se caía al arrancar
        // sin que nada en esta consola lo dejara ver.
        // "localhost", NUNCA "127.0.0.1": el propio daemon sólo registra el
        // prefijo "http://localhost:<puerto>/" en HTTP.sys, y HTTP.sys
        // compara el host exacto salvo comodín — una petición a 127.0.0.1
        // contra ese prefijo no es "el mismo destino con otro nombre", es
        // sencillamente un prefijo distinto, y responde 400 Bad Request
        // (Invalid Hostname) sin que la petición llegue siquiera al código
        // del daemon. Esto hacía fallar SIEMPRE la detección de Arena, con
        // Arena abierto o no — visto de un `curl 127.0.0.1:<puerto>/status`
        // real reproduciendo el mismo 400.
        var puerto = PuertoLibre();
        var baseDaemon = new Uri($"http://localhost:{puerto}");

        Console.WriteLine($"Starting the Arena reader on port {puerto} (mtga-tracker-daemon, third-party, GPLv3)…");
        var salidaDaemon = new StringBuilder();
        using var daemon = new Process
        {
            StartInfo = new ProcessStartInfo(rutaDaemon, $"-p {puerto}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        // Se enseña en vivo, línea a línea, según va hablando — no sólo si se
        // cae. Es literalmente lo que faltó la primera vez que esto falló de
        // verdad: el error real (un puerto ocupado) estaba ahí, pero nadie lo
        // veía hasta que ya era tarde.
        void RecibirLinea(string? linea)
        {
            if (linea is null) return;
            lock (salidaDaemon) salidaDaemon.AppendLine(linea);
            Console.WriteLine($"[reader] {linea}");
        }
        daemon.OutputDataReceived += (_, e) => RecibirLinea(e.Data);
        daemon.ErrorDataReceived += (_, e) => RecibirLinea(e.Data);

        try
        {
            try
            {
                daemon.Start();
                daemon.BeginOutputReadLine();
                daemon.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Couldn't start the reader: {ex.Message}");
                return null;
            }

            // Un momento para que, si va a caerse al arrancar (como el conflicto
            // de puerto que motivó todo esto), se vea YA en vez de esperar el
            // minuto entero para nada.
            await Task.Delay(1500);
            if (daemon.HasExited)
            {
                Console.WriteLine();
                Console.WriteLine(salidaDaemon.Length > 0
                    ? "The Arena reader closed itself right after starting — see what it said above."
                    : $"The Arena reader closed itself right after starting, without saying anything (exit code {daemon.ExitCode}).");
                return null;
            }

            using var httpDaemon = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            Console.WriteLine("Waiting for it to detect MTG Arena open (up to 60 s)…");
            Console.WriteLine("If you don't have it open yet, open it now.");
            if (!await EsperarArena(httpDaemon, baseDaemon, TimeSpan.FromSeconds(60)))
            {
                Console.WriteLine();
                Console.WriteLine("Arena wasn't detected open in time, so the full collection can't be read.");
                if (daemon.HasExited) Console.WriteLine("Also, the reader closed itself while waiting — see what it said above.");
                return null;
            }

            Console.WriteLine("Arena detected. Reading your collection…");
            var coleccion = await LeerColeccion(httpDaemon, baseDaemon);
            if (coleccion is null)
            {
                Console.WriteLine("Couldn't read the collection — did you just open Arena? Wait for it to fully load and run this again.");
            }
            return coleccion;
        }
        finally
        {
            // Antes de enseñar "pulsa una tecla para cerrar" (que se queda
            // bloqueado esperando), no después: si el lector se mata DESPUÉS de
            // ese mensaje, se queda vivo y ocupando su puerto todo el rato que el
            // usuario tarde en pulsar algo.
            try { if (!daemon.HasExited) daemon.Kill(entireProcessTree: true); } catch { /* ya se habrá cerrado solo, o ni llegó a arrancar */ }
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

    private static async Task<bool> EsperarArena(HttpClient http, Uri baseDaemon, TimeSpan plazo)
    {
        var limite = DateTime.UtcNow + plazo;
        while (DateTime.UtcNow < limite)
        {
            try
            {
                var r = await http.GetFromJsonAsync<EstadoDaemon>(new Uri(baseDaemon, "/status"), JsonOpciones);
                if (r?.IsRunning == true) return true;
            }
            catch { /* el daemon puede tardar un segundo en levantar su servidor */ }
            await Task.Delay(1000);
        }
        return false;
    }

    private static async Task<CartaColeccion[]?> LeerColeccion(HttpClient http, Uri baseDaemon)
    {
        try
        {
            var r = await http.GetFromJsonAsync<RespuestaCartas>(new Uri(baseDaemon, "/cards"), JsonOpciones);
            return r?.Cards.Select(c => new CartaColeccion(c.GrpId, c.Owned)).ToArray();
        }
        catch { return null; }
    }

    /// <summary>
    /// Un puerto libre de verdad, pedido al sistema operativo en el momento
    /// — no uno fijo que puede chocar con lo que ya haya en el ordenador de
    /// quien lo ejecuta. Se abre un socket en el puerto 0 (que el SO resuelve
    /// a uno libre), se lee cuál le tocó, y se cierra enseguida para que el
    /// daemon lo use él.
    /// </summary>
    [DllImport("kernel32.dll")]
    private static extern ushort GetUserDefaultUILanguage();

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

    private static int PuertoLibre()
    {
        using var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        var puerto = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return puerto;
    }

    private static int Esperar(int codigo)
    {
        Console.WriteLine();
        Console.WriteLine("Press a key to close…");
        Console.ReadKey();
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

// ─── mtga-tracker-daemon ─────────────────────────────────────────────────────

internal sealed record EstadoDaemon([property: JsonPropertyName("isRunning")] bool IsRunning);
internal sealed record RespuestaCartas([property: JsonPropertyName("cards")] CartaDaemon[] Cards);
internal sealed record CartaDaemon(
    [property: JsonPropertyName("grpId")] int GrpId,
    [property: JsonPropertyName("owned")] int Owned);

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
