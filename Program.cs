using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MtgCornerArenaBridge;

/// <summary>
/// LO ÚNICO QUE HACE ESTE PROGRAMA: arrancar mtga-tracker-daemon (de terceros,
/// GPLv3, https://github.com/frcaton/mtga-tracker-daemon), pedirle tu
/// colección por su servidor local, y escribir un fichero que la web de
/// MTG Corner ya sabe leer.
///
/// NO LEE MEMORIA DE NADA POR SU CUENTA. Eso es justo lo que hace el daemon,
/// y reescribirlo aquí sería reinventar algo ya hecho, mantenido y probado
/// por otra gente — con el riesgo añadido de acertar mal sin tener Arena a
/// mano para comprobarlo. Este programa es sólo el pegamento: arranca el
/// proceso, habla con su API por HTTP, y traduce la respuesta al formato que
/// entiende /importar-arena.
///
/// LOS COMODINES Y LOS MAZOS NO SALEN DE AQUÍ — esos ya se leen bien del
/// propio Player.log, y la página web los saca de ahí. Este programa sólo
/// cubre lo que el log no trae: la colección completa.
/// </summary>
internal static class Program
{
    private const int Puerto = 9000;
    private static readonly Uri Base = new($"http://127.0.0.1:{Puerto}");

    private static async Task<int> Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("MTG Corner — puente con MTG Arena");
        Console.WriteLine("==================================");
        Console.WriteLine();

        var rutaDaemon = Path.Combine(AppContext.BaseDirectory, "mtga-tracker-daemon.exe");
        if (!File.Exists(rutaDaemon))
        {
            Console.WriteLine($"No se encuentra {rutaDaemon}.");
            Console.WriteLine("Este programa espera venir junto a mtga-tracker-daemon.exe en la misma carpeta —");
            Console.WriteLine("si lo separaste del zip original, vuelve a ponerlos juntos.");
            return Esperar(1);
        }

        Console.WriteLine("Arrancando el lector de Arena (mtga-tracker-daemon, de terceros, GPLv3)…");
        using var daemon = new Process
        {
            StartInfo = new ProcessStartInfo(rutaDaemon, $"-p {Puerto}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };

        try
        {
            daemon.Start();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"No se pudo arrancar el lector: {ex.Message}");
            return Esperar(1);
        }

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            Console.WriteLine("Esperando a que detecte MTG Arena abierto (hasta 60 s)…");
            Console.WriteLine("Si no lo tienes abierto todavía, ábrelo ahora.");
            var detectado = await EsperarArena(http, TimeSpan.FromSeconds(60));
            if (!detectado)
            {
                Console.WriteLine();
                Console.WriteLine("No se detectó Arena abierto a tiempo. Ábrelo y vuelve a ejecutar este programa.");
                return Esperar(1);
            }

            Console.WriteLine("Arena detectado. Leyendo tu colección…");
            var coleccion = await LeerColeccion(http);
            if (coleccion is null)
            {
                Console.WriteLine("No se pudo leer la colección — ¿acabas de abrir Arena? Espera a que cargue del todo y reinténtalo.");
                return Esperar(1);
            }

            var salida = new ResultadoAnalisis(
                Comodines: null, // Se leen del Player.log, no de aquí — ver /importar-arena.
                Mazos: [],
                Coleccion: coleccion,
                Avisos: []
            );

            var ruta = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                $"mtgcorner-arena-{DateTime.Now:yyyy-MM-dd-HHmm}.json");
            await File.WriteAllTextAsync(ruta, JsonSerializer.Serialize(salida, JsonOpciones));

            Console.WriteLine();
            Console.WriteLine($"Listo: {coleccion.Length} cartas distintas.");
            Console.WriteLine($"Guardado en: {ruta}");
            Console.WriteLine("Arrastra ese fichero a mtgcorner.com/importar-arena para guardarlo en tu colección.");
            return Esperar(0);
        }
        finally
        {
            // Nunca se deja corriendo de fondo: se cierra siempre, haya ido
            // bien o mal.
            try { if (!daemon.HasExited) daemon.Kill(entireProcessTree: true); } catch { /* ya se habrá cerrado solo */ }
        }
    }

    private static async Task<bool> EsperarArena(HttpClient http, TimeSpan plazo)
    {
        var limite = DateTime.UtcNow + plazo;
        while (DateTime.UtcNow < limite)
        {
            try
            {
                var r = await http.GetFromJsonAsync<EstadoDaemon>(new Uri(Base, "/status"), JsonOpciones);
                if (r?.IsRunning == true) return true;
            }
            catch
            {
                // El daemon puede tardar un segundo en levantar su servidor
                // HTTP nada más arrancar el proceso; un fallo de conexión
                // aquí es normal en el primer intento, no un error real.
            }
            await Task.Delay(1000);
        }
        return false;
    }

    private static async Task<CartaColeccion[]?> LeerColeccion(HttpClient http)
    {
        try
        {
            var r = await http.GetFromJsonAsync<RespuestaCartas>(new Uri(Base, "/cards"), JsonOpciones);
            return r?.Cards.Select(c => new CartaColeccion(c.GrpId, c.Owned)).ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static int Esperar(int codigo)
    {
        Console.WriteLine();
        Console.WriteLine("Pulsa una tecla para cerrar…");
        Console.ReadKey();
        return codigo;
    }

    private static readonly JsonSerializerOptions JsonOpciones = new(JsonSerializerDefaults.Web);
}

// ─── Lo que devuelve mtga-tracker-daemon ────────────────────────────────────

internal sealed record EstadoDaemon(
    [property: JsonPropertyName("isRunning")] bool IsRunning);

internal sealed record RespuestaCartas(
    [property: JsonPropertyName("cards")] CartaDaemon[] Cards);

internal sealed record CartaDaemon(
    [property: JsonPropertyName("grpId")] int GrpId,
    [property: JsonPropertyName("owned")] int Owned);

// ─── Lo que espera app/components/ImportarArena.tsx (lib/mtgaLog.ts) ───────

internal sealed record CartaColeccion(
    [property: JsonPropertyName("arenaId")] int ArenaId,
    [property: JsonPropertyName("cantidad")] int Cantidad);

internal sealed record ResultadoAnalisis(
    [property: JsonPropertyName("comodines")] object? Comodines,
    [property: JsonPropertyName("mazos")] object[] Mazos,
    [property: JsonPropertyName("coleccion")] CartaColeccion[] Coleccion,
    [property: JsonPropertyName("avisos")] string[] Avisos
);
