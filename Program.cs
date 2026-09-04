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
    private const int PuertoDaemon = 9000;
    private static readonly Uri BaseDaemon = new($"http://127.0.0.1:{PuertoDaemon}");
    private static readonly JsonSerializerOptions JsonOpciones = new(JsonSerializerDefaults.Web);

    private static async Task<int> Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("MTG Corner — puente con MTG Arena");
        Console.WriteLine("==================================");
        Console.WriteLine();

        using var http = new HttpClient { BaseAddress = new Uri(Sitio), Timeout = TimeSpan.FromSeconds(10) };

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
            Console.WriteLine($"No se pudo contactar con MTG Corner: {ex.Message}");
            return Esperar(1);
        }

        var url = $"{Sitio}/vincular-dispositivo?codigo={codigo}";
        Console.WriteLine("Abriendo tu navegador para confirmar que eres tú…");
        Console.WriteLine($"Si no se abre solo, entra en: {url}");
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* el aviso de arriba con la URL ya basta si esto falla */ }

        Console.WriteLine("Esperando la confirmación (hasta 3 minutos)…");
        var confirmado = await EsperarConfirmacion(http, codigo, TimeSpan.FromMinutes(3));
        if (!confirmado)
        {
            Console.WriteLine();
            Console.WriteLine("No se confirmó a tiempo. No se ha leído ni guardado nada.");
            return Esperar(1);
        }
        Console.WriteLine("Confirmado.");
        Console.WriteLine();

        // ── 2. Sólo ahora se toca Arena ─────────────────────────────────────
        var rutaDaemon = Path.Combine(AppContext.BaseDirectory, "mtga-tracker-daemon.exe");
        if (!File.Exists(rutaDaemon))
        {
            Console.WriteLine($"No se encuentra {rutaDaemon}.");
            Console.WriteLine("Este programa espera venir junto a mtga-tracker-daemon.exe en la misma carpeta.");
            return Esperar(1);
        }

        Console.WriteLine("Arrancando el lector de Arena (mtga-tracker-daemon, de terceros, GPLv3)…");
        using var daemon = new Process
        {
            StartInfo = new ProcessStartInfo(rutaDaemon, $"-p {PuertoDaemon}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };

        try { daemon.Start(); }
        catch (Exception ex)
        {
            Console.WriteLine($"No se pudo arrancar el lector: {ex.Message}");
            return Esperar(1);
        }

        try
        {
            using var httpDaemon = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            Console.WriteLine("Esperando a que detecte MTG Arena abierto (hasta 60 s)…");
            Console.WriteLine("Si no lo tienes abierto todavía, ábrelo ahora.");
            if (!await EsperarArena(httpDaemon, TimeSpan.FromSeconds(60)))
            {
                Console.WriteLine();
                Console.WriteLine("No se detectó Arena abierto a tiempo. Ábrelo y vuelve a ejecutar este programa.");
                return Esperar(1);
            }

            Console.WriteLine("Arena detectado. Leyendo tu colección…");
            var coleccion = await LeerColeccion(httpDaemon);
            if (coleccion is null)
            {
                Console.WriteLine("No se pudo leer la colección — ¿acabas de abrir Arena? Espera a que cargue del todo y reinténtalo.");
                return Esperar(1);
            }

            // ── 3. Directo a tu cuenta, con el mismo código ya confirmado ──
            Console.WriteLine($"Guardando {coleccion.Length} cartas en tu colección de MTG Corner…");
            var cuerpo = new PeticionImportar(codigo, null, [], coleccion, []);
            var resp = await http.PostAsJsonAsync("/api/mtga-import", cuerpo, JsonOpciones);
            if (!resp.IsSuccessStatusCode)
            {
                Console.WriteLine($"MTG Corner no aceptó la importación (código {(int)resp.StatusCode}). No se ha guardado nada.");
                return Esperar(1);
            }

            var resumen = await resp.Content.ReadFromJsonAsync<ResumenGuardado>(JsonOpciones);
            Console.WriteLine();
            Console.WriteLine($"Listo: {resumen?.CartasGuardadas ?? 0} cartas guardadas en tu colección.");
            if (resumen?.SinTraducir > 0) Console.WriteLine($"({resumen.SinTraducir} no se reconocieron — puede que sean muy nuevas.)");
            return Esperar(0);
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

    private static async Task<bool> EsperarArena(HttpClient http, TimeSpan plazo)
    {
        var limite = DateTime.UtcNow + plazo;
        while (DateTime.UtcNow < limite)
        {
            try
            {
                var r = await http.GetFromJsonAsync<EstadoDaemon>(new Uri(BaseDaemon, "/status"), JsonOpciones);
                if (r?.IsRunning == true) return true;
            }
            catch { /* el daemon puede tardar un segundo en levantar su servidor */ }
            await Task.Delay(1000);
        }
        return false;
    }

    private static async Task<CartaColeccion[]?> LeerColeccion(HttpClient http)
    {
        try
        {
            var r = await http.GetFromJsonAsync<RespuestaCartas>(new Uri(BaseDaemon, "/cards"), JsonOpciones);
            return r?.Cards.Select(c => new CartaColeccion(c.GrpId, c.Owned)).ToArray();
        }
        catch { return null; }
    }

    private static int Esperar(int codigo)
    {
        Console.WriteLine();
        Console.WriteLine("Pulsa una tecla para cerrar…");
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
