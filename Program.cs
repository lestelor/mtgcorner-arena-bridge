using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MtgCornerArenaBridge;

/// <summary>
/// LO ÚNICO QUE HACE ESTE PROGRAMA: confirmar quién eres en mtgcorner.com,
/// arrancar mtga-tracker-daemon (de terceros, GPLv3,
/// https://github.com/frcaton/mtga-tracker-daemon) para leer tu colección de
/// Arena, y mandarla directamente a tu cuenta. No queda ningún fichero en tu
/// ordenador ni al acabar bien ni si algo falla.
///
/// NO LEE MEMORIA DE NADA POR SU CUENTA. Eso es justo lo que hace el daemon,
/// y reescribirlo aquí sería reinventar algo ya hecho, mantenido y probado
/// por otra gente. Este programa es sólo el pegamento.
///
/// EL ORDEN IMPORTA: primero se confirma la sesión, y SÓLO DESPUÉS se toca
/// Arena. Sin sesión confirmada, nunca se llega a leer nada — no hay ningún
/// motivo para sacar datos del juego que no se van a poder guardar.
/// </summary>
internal static class Program
{
    private const string Sitio = "https://mtgcorner.com";
    private static readonly JsonSerializerOptions JsonOpciones = new(JsonSerializerDefaults.Web);

    private static async Task<int> Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
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

        // ── 2. Sólo ahora se toca Arena ─────────────────────────────────────
        var rutaDaemon = Path.Combine(AppContext.BaseDirectory, "mtga-tracker-daemon.exe");
        if (!File.Exists(rutaDaemon))
        {
            Console.WriteLine($"Can't find {rutaDaemon}.");
            Console.WriteLine("This program expects to be next to mtga-tracker-daemon.exe in the same folder.");
            return Esperar(1);
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
        var salidaDaemon = new System.Text.StringBuilder();
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
        // veía hasta que ya era tarde. Se guarda también en `salidaDaemon`
        // para poder reenseñarlo entero si hace falta más abajo.
        void RecibirLinea(string? linea)
        {
            if (linea is null) return;
            lock (salidaDaemon) salidaDaemon.AppendLine(linea);
            Console.WriteLine($"[reader] {linea}");
        }
        daemon.OutputDataReceived += (_, e) => RecibirLinea(e.Data);
        daemon.ErrorDataReceived += (_, e) => RecibirLinea(e.Data);

        // Antes de enseñar "pulsa una tecla para cerrar" (que se queda
        // bloqueado esperando), no después: si el lector se mata DESPUÉS de
        // ese mensaje, se queda vivo y ocupando su puerto todo el rato que el
        // usuario tarde en pulsar algo — tiempo de sobra para que una prueba
        // manual en ese mismo puerto choque con él.
        int Salir(int codigo)
        {
            try { if (!daemon.HasExited) daemon.Kill(entireProcessTree: true); } catch { /* ya se habrá cerrado solo, o ni llegó a arrancar */ }
            return Esperar(codigo);
        }

        try
        {
            daemon.Start();
            daemon.BeginOutputReadLine();
            daemon.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Couldn't start the reader: {ex.Message}");
            return Salir(1);
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
            return Salir(1);
        }

        try
        {
            using var httpDaemon = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            Console.WriteLine("Waiting for it to detect MTG Arena open (up to 60 s)…");
            Console.WriteLine("If you don't have it open yet, open it now.");
            if (!await EsperarArena(httpDaemon, baseDaemon, TimeSpan.FromSeconds(60)))
            {
                Console.WriteLine();
                Console.WriteLine("Arena wasn't detected open in time. Open it and run this program again.");
                if (daemon.HasExited)
                {
                    Console.WriteLine("Also, the reader closed itself while waiting — see what it said above.");
                }
                return Salir(1);
            }

            Console.WriteLine("Arena detected. Reading your collection…");
            var coleccion = await LeerColeccion(httpDaemon, baseDaemon);
            if (coleccion is null)
            {
                Console.WriteLine("Couldn't read the collection — did you just open Arena? Wait for it to fully load and try again.");
                return Salir(1);
            }

            // ── 3. Directo a tu cuenta, con el mismo código ya confirmado ──
            Console.WriteLine($"Saving {coleccion.Length} cards to your \"Arena\" deck on MTG Corner…");
            Console.WriteLine("(a large collection can take more than a minute — keep waiting)");
            var cuerpo = new PeticionImportar(codigo, null, [], coleccion, []);
            var resp = await http.PostAsJsonAsync("/api/mtga-import", cuerpo, JsonOpciones);
            if (!resp.IsSuccessStatusCode)
            {
                Console.WriteLine($"MTG Corner didn't accept the import (code {(int)resp.StatusCode}). Nothing was saved.");
                return Salir(1);
            }

            var resumen = await resp.Content.ReadFromJsonAsync<ResumenGuardado>(JsonOpciones);
            Console.WriteLine();
            Console.WriteLine($"Done: {resumen?.CartasGuardadas ?? 0} cards saved to your \"Arena\" deck.");
            if (resumen?.SinTraducir > 0) Console.WriteLine($"({resumen.SinTraducir} weren't recognized — they might be very new.)");
            return Salir(0);
        }
        finally
        {
            try { if (!daemon.HasExited) daemon.Kill(entireProcessTree: true); } catch { /* ya se habrá cerrado solo */ }
        }
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
    ///
    /// Hay una ventana mínima entre que se libera aquí y el daemon lo coge —
    /// en teoría otra cosa podría colárselo en medio—, pero es el mismo
    /// método que usan herramientas de desarrollo con el mismo problema, y en
    /// la práctica es muchísimo más fiable que un número fijo cualquiera.
    /// </summary>
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

// ─── mtgcorner.com — el vínculo de dispositivo ──────────────────────────────

internal sealed record RespuestaIniciar([property: JsonPropertyName("codigo")] string Codigo);
internal sealed record RespuestaEstado([property: JsonPropertyName("confirmado")] bool Confirmado);
internal sealed record ResumenGuardado(
    [property: JsonPropertyName("cartasGuardadas")] int CartasGuardadas,
    [property: JsonPropertyName("sinTraducir")] int SinTraducir);

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

internal sealed record PeticionImportar(
    [property: JsonPropertyName("codigo")] string Codigo,
    [property: JsonPropertyName("comodines")] object? Comodines,
    [property: JsonPropertyName("mazos")] object[] Mazos,
    [property: JsonPropertyName("coleccion")] CartaColeccion[] Coleccion,
    [property: JsonPropertyName("avisos")] string[] Avisos
);
