using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
    /// <summary>
    /// La web. Se puede apuntar a otra con la variable de entorno
    /// `MTGCORNER_SITIO` para probar contra un servidor de desarrollo sin
    /// tocar el código; sin ella, la de siempre.
    /// </summary>
    private static readonly string Sitio =
        Environment.GetEnvironmentVariable("MTGCORNER_SITIO") is { Length: > 0 } otro ? otro.TrimEnd('/') : "https://mtgcorner.com";
    /// <summary>
    /// La LISTA de releases, no `/releases/latest`.
    ///
    /// `/releases/latest` devuelve el más reciente por fecha, y ése es siempre
    /// el de la etiqueta móvil `latest`, cuyo `tag_name` es literalmente
    /// "latest": no es una versión y no hay nada que comparar. Los releases
    /// versionados son los `vX.Y.Z`, así que se piden los últimos y se busca el
    /// número más alto entre ellos.
    /// </summary>
    private const string ReleasesApi = "https://api.github.com/repos/lestelor/mtgcorner-arena-bridge/releases?per_page=20";
    private static readonly string PaginaDescarga = Sitio + "/importar-arena";

    /// <summary>
    /// POR DÓNDE PREGUNTA EL PROGRAMA A LA WEB (nombre de una carta, cartas
    /// parecidas). Las mismas rutas existen en <c>/api/puente/…</c>, pero el
    /// cortafuegos de Vercel (modo Challenge) devuelve un 429 «Security
    /// Checkpoint» a todo lo que no es un navegador, y al programa sólo se le
    /// abrió paso por el prefijo <c>/api/mtga-device/…</c>, donde ya vincula el
    /// ordenador. Visto el 2026-09-24: con la 1.11.1 la columna nunca ofrecía
    /// «Cartas similares a…» porque el nombre no llegaba. Lo que se abre EN EL
    /// NAVEGADOR (<c>Abrir(...)</c>) sigue yendo por <c>/api/puente</c>: el
    /// navegador sí pasa el desafío.
    /// </summary>
    private const string PuertaDevice = "/api/mtga-device";
    private static readonly JsonSerializerOptions JsonOpciones = new(JsonSerializerDefaults.Web);

    /// <summary>La versión de este ejecutable (&lt;Version&gt; del .csproj).</summary>
    private static string VersionPropia =>
        Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    /// <summary>Una versión más nueva publicada en GitHub, con su fichero.</summary>
    /// <param name="Version">La etiqueta `vX.Y.Z` del release, ya en número.</param>
    /// <param name="Url">El .exe de ese release.</param>
    /// <param name="Sha256">La huella que publica GitHub, si la trae. Es lo que
    /// permite comprobar que lo descargado es exactamente lo publicado.</param>
    private sealed record Novedad(Version Version, string Url, string? Sha256)
    {
        public string Numero => $"{Version.Major}.{Version.Minor}.{Version.Build}";
    }

    /// <summary>
    /// ¿HAY UNA VERSIÓN MÁS NUEVA? Se pregunta una vez al arrancar.
    ///
    /// Es la razón de que ahora se publique con versión: antes el release era
    /// siempre "latest" con el mismo nombre de fichero, así que nadie podía
    /// saber si el .exe de ahí fuera ya lo tenías y se volvía a descargar por si
    /// acaso.
    ///
    /// NO ESTORBA: tres segundos de espera como mucho, cualquier fallo se traga
    /// —sin red, GitHub caído, un límite de peticiones— y si la versión coincide
    /// no escribe nada. Nunca impide usar el programa.
    /// </summary>
    private static async Task<Novedad?> BuscarVersionNueva()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            // GitHub rechaza las peticiones sin User-Agent.
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"MtgCornerArenaBridge/{VersionPropia}");
            var json = await http.GetStringAsync(ReleasesApi);
            if (JsonNode.Parse(json) is not JsonArray releases) return null;

            Version? ultima = null;
            JsonNode? mejor = null;
            foreach (var r in releases)
            {
                var etiqueta = r?["tag_name"]?.GetValue<string>();
                // Se salta "latest" y cualquier otra etiqueta que no sea un
                // número de versión.
                if (etiqueta is null || !Version.TryParse(etiqueta.TrimStart('v', 'V'), out var v)) continue;
                if (r?["draft"]?.GetValue<bool>() == true || r?["prerelease"]?.GetValue<bool>() == true) continue;
                if (ultima is null || v > ultima) { ultima = v; mejor = r; }
            }
            if (ultima is null || !Version.TryParse(VersionPropia, out var mia) || ultima <= mia) return null;

            // El .exe de ESE release, no el de la etiqueta móvil: así lo que se
            // descarga es exactamente la versión con la que se ha comparado.
            foreach (var a in (mejor?["assets"] as JsonArray) ?? new JsonArray())
            {
                var nombre = a?["name"]?.GetValue<string>() ?? "";
                if (!nombre.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                var url = a?["browser_download_url"]?.GetValue<string>();
                if (url is null) continue;
                // `digest` llega como "sha256:abc…" cuando GitHub lo publica.
                var digest = a?["digest"]?.GetValue<string>();
                var sha = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                    ? digest[7..] : null;
                return new Novedad(ultima, url, sha);
            }
            // Release sin fichero (raro): al menos se puede avisar.
            return new Novedad(ultima, PaginaDescarga, null);
        }
        catch { /* sin red o GitHub de morros: no es asunto de este programa */ }
        return null;
    }

    /// <summary>
    /// LA ACTUALIZACIÓN, PREGUNTANDO PRIMERO.
    ///
    /// Antes esto sólo avisaba, y avisar deja el trabajo hecho a medias: había
    /// que ir a la web, descargar, encontrar el fichero viejo y reemplazarlo a
    /// mano. Ahora se baja, se comprueba y se relanza solo.
    ///
    /// CÓMO SE REEMPLAZA UN .EXE EN MARCHA. Windows no deja escribir encima del
    /// ejecutable de un proceso vivo, pero sí deja RENOMBRARLO: el fichero
    /// abierto sigue siendo el mismo, sólo cambia su nombre. Así que el actual
    /// pasa a `.old`, el nuevo ocupa su sitio, se lanza y este proceso se va. El
    /// `.old` lo borra el siguiente arranque (<see cref="LimpiarViejo"/>), que es
    /// cuando ya no lo tiene nadie abierto. Sin programa auxiliar, que es justo
    /// lo que hace que un actualizador dé problemas.
    ///
    /// LO QUE SE COMPRUEBA ANTES DE EJECUTAR NADA: que venga de la API de GitHub
    /// por HTTPS y que su sha256 sea el que el propio release publica. Este
    /// programa no va firmado, así que la huella es la única garantía de que lo
    /// descargado es lo publicado, y por eso si no cuadra NO se instala.
    ///
    /// DE REGALO, UN AVISO MENOS: lo que baja un navegador queda marcado como
    /// «de internet» y Windows enseña el cartel de SmartScreen al abrirlo. Lo
    /// que baja este programa, no.
    ///
    /// Devuelve true si se ha lanzado la versión nueva: entonces aquí ya no hay
    /// nada más que hacer y este proceso se cierra.
    /// </summary>
    private static async Task<bool> Actualizar(Novedad novedad, bool silencioso)
    {
        // El .exe de este proceso. En un publicado de un solo fichero,
        // ProcessPath es el .exe de verdad (Assembly.Location viene vacío).
        var propio = Environment.ProcessPath;
        var puedeSolo = propio is not null
            && novedad.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && novedad.Sha256 is not null;

        if (!puedeSolo)
        {
            // Sin huella que comprobar o sin saber dónde está uno mismo, se
            // avisa y se deja la página, que es lo que se hacía hasta ahora.
            Textos.Linea("version_nueva", novedad.Numero, VersionPropia, PaginaDescarga);
            Console.WriteLine();
            return false;
        }

        if (!await Preguntar(novedad, silencioso)) return false;

        try
        {
            Textos.Linea("actualizando");
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"MtgCornerArenaBridge/{VersionPropia}");
            var datos = await http.GetByteArrayAsync(novedad.Url);

            var huella = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(datos)).ToLowerInvariant();
            if (!string.Equals(huella, novedad.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                Textos.Linea("actualizar_huella");
                Textos.Linea("version_nueva", novedad.Numero, VersionPropia, PaginaDescarga);
                Console.WriteLine();
                return false;
            }

            var viejo = propio + ".old";
            if (File.Exists(viejo)) File.Delete(viejo);
            File.Move(propio!, viejo);           // permitido aunque esté en marcha
            await File.WriteAllBytesAsync(propio!, datos);

            Process.Start(new ProcessStartInfo(propio!) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            // Lo más probable: el .exe está en una carpeta donde no se puede
            // escribir. Se deshace lo que se pueda y se sigue con la versión de
            // ahora, que funciona igual.
            try
            {
                var viejo = propio + ".old";
                if (File.Exists(viejo) && !File.Exists(propio!)) File.Move(viejo, propio!);
            }
            catch { /* ya se dice abajo que hay que hacerlo a mano */ }
            Textos.Linea("actualizar_fallo", ex.Message);
            Textos.Linea("version_nueva", novedad.Numero, VersionPropia, PaginaDescarga);
            Console.WriteLine();
            return false;
        }
    }

    /// <summary>
    /// ¿La instalo? Por consola cuando hay alguien mirándola, y con un cuadro de
    /// Windows cuando no lo hay.
    ///
    /// EL CUADRO ES PARA CUANDO NO HAY VENTANA QUE MIRAR: el modo de fondo
    /// (<c>--silencioso</c>) y cualquier arranque con la entrada redirigida, que
    /// es como queda si se deja programado. Una línea de consola ahí no la lee
    /// nadie.
    ///
    /// POR CONSOLA HAY MEDIO MINUTO Y SE SIGUE SIN ACTUALIZAR: quien lo ha
    /// lanzado puede haberse ido a por café, y la pregunta no puede dejar el
    /// programa colgado para siempre. Enter o cualquier tecla que no sea N
    /// acepta, porque la respuesta que casi siempre se quiere es sí y la letra
    /// del sí cambia con el idioma.
    /// </summary>
    private static async Task<bool> Preguntar(Novedad novedad, bool silencioso)
    {
        // Sin ventana que leer no vale preguntar por consola: ni con la bandera
        // del modo de fondo ni cuando la entrada viene de un script o de una
        // tarea programada, que es como se deja esto corriendo solo antes de
        // que exista el modo residente.
        if (silencioso || Console.IsInputRedirected) return Dialogo(novedad);

        try
        {
            Textos.Linea("actualizar_pregunta", novedad.Numero, VersionPropia);
            Textos.Linea("actualizar_teclas");
            for (var i = 0; i < 300; i++)
            {
                if (Console.KeyAvailable)
                {
                    var k = Console.ReadKey(true);
                    Console.WriteLine();
                    return k.Key != ConsoleKey.N && k.Key != ConsoleKey.Escape;
                }
                await Task.Delay(100);
            }
            Console.WriteLine();
            return false;
        }
        catch
        {
            // La consola dijo que sí la había y luego no dejó leer teclas. El
            // cuadro de Windows es lo que queda, y sigue siendo una pregunta.
            return Dialogo(novedad);
        }
    }

    /// <summary>
    /// La misma pregunta, en un cuadro de Windows: «Aceptar» instala.
    ///
    /// Es un MessageBox y no una notificación del sistema a propósito. Una
    /// notificación depende de que el usuario las tenga encendidas, se apila
    /// con las demás y se va sola; esto sale delante, espera, y tiene los dos
    /// botones que hacen falta.
    /// </summary>
    private static bool Dialogo(Novedad novedad)
    {
        const uint OkCancelar = 0x1, Info = 0x40, AlFrente = 0x10000, Encima = 0x40000;
        const int Aceptar = 1;
        return MessageBoxW(IntPtr.Zero,
            Textos.T("actualizar_pregunta", novedad.Numero, VersionPropia),
            Textos.T("titulo"), OkCancelar | Info | AlFrente | Encima) == Aceptar;
    }

    /// <summary>
    /// El ejecutable que la actualización anterior dejó renombrado. Se borra en
    /// el siguiente arranque, que es cuando ya no lo tiene abierto nadie. Si no
    /// se deja (un antivirus mirándolo), no pasa nada: se intentará otra vez.
    /// </summary>
    private static void LimpiarViejo()
    {
        try
        {
            var viejo = Environment.ProcessPath + ".old";
            if (File.Exists(viejo)) File.Delete(viejo);
        }
        catch { /* el fichero sobra, no molesta */ }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        // Primera suposición: el idioma de Windows, que es lo único que se sabe
        // antes de hablar con nadie. En cuanto se confirma en el navegador, la
        // web dice en qué idioma se está jugando y manda ése. Ver Textos.cs.
        Textos.EscogerElDeWindows(IdiomaDeWindows());

        // EL ENLACE mtgcorner:// apunta a ESTE ejecutable, esté hoy donde esté
        // (ver Protocolo.cs). Y si es el enlace quien nos abre, sólo «importar»
        // hace algo: es el flujo normal de abajo. Lo demás se ignora, que
        // cualquier web puede poner un enlace así.
        Protocolo.Registrar();
        var accion = Protocolo.Accion(args);
        if (accion is not null && accion != "importar") return 0;

        // Modos que no conectan con nada: comprobar el recorte del registro,
        // probar la lectura de la colección y volcar la licencia.
        if (args.Length > 0 && args[0] == "--probar-log") return ProbarLog(args.Skip(1).ToArray());
        if (args.Length > 0 && args[0] == "--probar-coleccion") return ProbarColeccion();
        if (args.Length > 0 && args[0] == "--probar-offsets") return ProbarOffsets.Ejecutar();
        if (args.Length > 0 && args[0] == "--licencia") return EscribirLicencia();
        // Poner o quitar el arranque con Windows. Son dos líneas en el registro
        // del usuario y no conectan con nada, como los de prueba de arriba.
        if (args.Length > 0 && args[0] == "--arrancar-solo") return Inicio(true);
        if (args.Length > 0 && args[0] == "--no-arrancar-solo") return Inicio(false);
        // Y el modo de fondo, que es donde vive todo lo demás de este programa
        // sin que nadie tenga que lanzarlo.
        if (args.Contains("--residente")) return await Residente();
        // Ver el aviso que se pinta encima de Arena sin tener que importar
        // nada. Con Arena abierto sale sobre su ventana; sin él, en la esquina
        // de la pantalla.
        if (args.Length > 0 && args[0] == "--probar-superposicion")
        {
            Superposicion.Mostrar(Textos.T("sup_titulo"), Textos.T("sup_guardado", 5343), 8);
            return 0;
        }
        // Ver la columna de iconos sobre Arena sin arrancar el residente. Los
        // iconos sólo dicen por consola cuál se ha pulsado.
        if (args.Length > 0 && args[0] == "--probar-columna")
        {
            Columna.SiempreVisible = true;
            Columna.ForzarAbierta = args.Contains("--abierta");
            Columna.Iniciar((a, d) => { Console.WriteLine($"pulsado: {a} {d}"); return Task.CompletedTask; }, ArrancaSolo);
            // --contexto: con las filas que pone el juego, para verlas sin jugar.
            if (args.Contains("--contexto"))
            {
                Columna.FilasDeContexto(
                    (Columna.Accion.Mejorar, "Mono-White Auras (2)", Textos.T("col_mejorar", "Mono-White Auras (2)")),
                    (Columna.Accion.Similares, "58437", Textos.T("col_similares", "Plains")),
                    (Columna.Accion.Combos, "58437", Textos.T("col_combos", "Plains")));
            }
            Console.WriteLine("Columna sobre Arena durante 40 s…");
            await Task.Delay(TimeSpan.FromSeconds(40));
            Columna.Cerrar();
            return 0;
        }
        // Preguntar a la web cómo se llama una carta EXACTAMENTE como lo hace el
        // residente (mismo cliente, mismas cabeceras, mismo token), y contar qué
        // vuelve: estado, tipo de contenido y el cuerpo. Para cuando la columna
        // no ofrece nada y hay que saber si es la web o el cortafuegos.
        if (args.Length > 0 && args[0] == "--probar-nombre")
        {
            var v = Vinculo.Leer();
            using var h = new HttpClient { BaseAddress = new Uri(Sitio), Timeout = TimeSpan.FromMinutes(1) };
            h.DefaultRequestHeaders.Add("X-Bridge-Version", VersionPropia);
            if (v is not null) h.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", v.Token);
            var grpPrueba = args.Length > 1 ? args[1] : "58437";
            try
            {
                var r = await h.GetAsync($"{PuertaDevice}/carta/{grpPrueba}");
                var cuerpo = await r.Content.ReadAsStringAsync();
                Console.WriteLine($"HTTP {(int)r.StatusCode} | {r.Content.Headers.ContentType} | vínculo={(v is null ? "no" : "sí")}");
                Console.WriteLine(cuerpo.Length > 300 ? cuerpo[..300] + "…" : cuerpo);
            }
            catch (Exception ex) { Console.WriteLine("EXCEPCIÓN: " + ex.GetType().Name + ": " + ex.Message); }
            return 0;
        }
        // Preguntar por las amenazas de una mesa dada por NOMBRES, como lo haría
        // el residente con los arena_id de la mesa del rival, y enseñar lo que
        // vuelve: sirve para comprobar la puerta y el formato sin una partida.
        if (args.Length > 0 && args[0] == "--probar-amenazas")
        {
            using var h = new HttpClient { BaseAddress = new Uri(Sitio), Timeout = TimeSpan.FromMinutes(1) };
            h.DefaultRequestHeaders.Add("X-Bridge-Version", VersionPropia);
            var nombresMesa = args.Length > 1 ? string.Join(",", args.Skip(1)) : "Thassa's Oracle,Demonic Consultation";
            try
            {
                var r = await h.GetFromJsonAsync<RespuestaAmenazas>($"{PuertaDevice}/amenazas?nombres={Uri.EscapeDataString(nombresMesa)}&idioma={Textos.Idioma}", JsonOpciones);
                foreach (var c in r?.Listos ?? []) Console.WriteLine($"LISTO  {c.Resultado} | {string.Join(" + ", (c.Piezas ?? []).Select(p => p.Nombre + (p.EnMesa ? " (en mesa)" : "")))} | {c.Url}");
                foreach (var c in (r?.Casi ?? []).Take(3)) Console.WriteLine($"CASI   {c.Resultado} | {string.Join(" + ", (c.Piezas ?? []).Select(p => p.Nombre + (p.EnMesa ? " (en mesa)" : "")))}");
                Console.WriteLine($"({(r?.Listos ?? []).Length} listos, {(r?.Casi ?? []).Length} casi)");
            }
            catch (Exception ex) { Console.WriteLine("EXCEPCIÓN: " + ex.Message); }
            return 0;
        }
        // Ver el panel de cartas encima de Arena sin jugar: con el arena_id que
        // se le pase, o el de una carta cualquiera de la partida de pruebas.
        if (args.Length > 0 && args[0] == "--probar-panel")
        {
            var vinculoPanel = Vinculo.Leer();
            using var httpPanel = new HttpClient { BaseAddress = new Uri(Sitio), Timeout = TimeSpan.FromMinutes(2) };
            if (vinculoPanel is not null) httpPanel.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", vinculoPanel.Token);
            PanelCartas.SiempreVisible = true;
            var cual = args.Length > 1 ? args[1] : "91550";
            Console.WriteLine($"Panel de similares de {cual}...");
            Console.WriteLine(await PanelSimilares(httpPanel, cual) ? "ensenado" : "no se pudo");
            await Task.Delay(TimeSpan.FromSeconds(40));
            PanelCartas.Cerrar();
            return 0;
        }
        // Ver qué saca el vigía del registro (Contexto.cs) con Arena en marcha:
        // mazo actual, carta bajo el ratón, si hay partida. Sesenta segundos.
        if (args.Length > 0 && args[0] == "--probar-contexto")
        {
            Contexto.Cambio += () => Console.WriteLine($"{DateTime.Now:HH:mm:ss}  mazo={Contexto.Mazo ?? "-"}  tu carta={Contexto.Carta?.ToString() ?? "-"}  rival mira={Contexto.RivalMira?.ToString() ?? "-"}  colores={(Contexto.MisColores.Length > 0 ? Contexto.MisColores : "-")}  mesa rival=[{string.Join(",", Contexto.MesaRival)}]  formato={Contexto.Formato ?? "-"}{(Contexto.Edicion is { } ed ? "/" + ed : "")}  partida={Contexto.EnPartida}");
            Contexto.PartidaAcabada += () =>
            {
                Console.WriteLine($"PARTIDA ACABADA: gané={Contexto.UltimaPartida?.Gane} razón={Contexto.UltimaPartida?.Razon} turnos={Contexto.UltimaPartida?.Turnos.Count}");
                foreach (var t in Contexto.UltimaPartida?.Turnos ?? [])
                    Console.WriteLine($"  T{t.N} {t.Activo}  vida {t.VidaYo?.ToString() ?? "?"}/{t.VidaRival?.ToString() ?? "?"}  yo=[{string.Join(",", t.JugadasYo)}] rival=[{string.Join(",", t.JugadasRival)}]  daño a mí {t.DanoAYo} al rival {t.DanoARival}");
            };
            // Con un segundo argumento se vigila otro fichero (un Player-prev.log
            // con partidas acabadas, para probar la cronología sin jugar una).
            Contexto.Iniciar(args.Length > 1 ? args[1] : Path.Combine(LogArena.Carpeta(), "Player.log"));
            Console.WriteLine("Vigilando el registro durante 60 s…");
            await Task.Delay(TimeSpan.FromSeconds(60));
            return 0;
        }

        // SIN NADIE MIRANDO LA CONSOLA. Es la bandera con la que arrancará el
        // modo de fondo, y de momento sólo cambia una cosa: lo que se preguntaría
        // por consola se pregunta con un cuadro de Windows, que es lo único que
        // se ve cuando no hay ventana.
        var silencioso = args.Contains("--silencioso");

        Textos.Linea("titulo");
        Console.WriteLine("==================================");
        Console.WriteLine();

        // Lo que dejó la actualización anterior, cuando ya no lo tiene abierto
        // nadie.
        LimpiarViejo();

        // ANTES DE NADA, LA VERSIÓN. Si hay una más nueva se ofrece instalarla
        // aquí mismo, y si se acepta este proceso se va: sigue el que se acaba
        // de lanzar, que ya es la versión nueva. Si no hay novedad, no se nota
        // que esto ha pasado.
        var novedad = await BuscarVersionNueva();
        if (novedad is not null && await Actualizar(novedad, silencioso)) return 0;

        // 10 s bastaba de sobra para iniciar/confirmar/consultar el vínculo,
        // pero la ÚLTIMA llamada —guardar— puede tardar de verdad: mtgcorner.com
        // traduce cada carta contra Scryfall en lotes, y una colección real son
        // miles. Con 10 s este cliente cortaba la petición antes de que el
        // servidor pudiera terminar.
        using var http = new HttpClient { BaseAddress = new Uri(Sitio), Timeout = TimeSpan.FromMinutes(4) };
        // QUÉ VERSIÓN ES ESTA, en cada petición. Con ella la web puede decir
        // «este ordenador tiene la 1.4.0 y hay la 1.5.0» sabiéndolo de verdad,
        // en vez de adivinarlo por lo que recuerde el navegador. Se guarda en
        // el dispositivo vinculado (ver /api/mtga-import).
        http.DefaultRequestHeaders.Add("X-Bridge-Version", VersionPropia);

        // ── 1. Quién eres ─────────────────────────────────────────────────
        //
        // La PRIMERA vez, confirmado en tu navegador y nunca aquí. Las demás,
        // con el token que quedó guardado de esa vez (ver Vinculo.cs): sin
        // pestaña, sin esperas y sin nada que teclear. Ese es justo el paso que
        // impedía que este programa pudiera quedarse de fondo.
        var vinculo = Vinculo.Leer();
        string? token = vinculo?.Token;
        string? codigo = null;
        if (token is null)
        {
            (token, codigo) = await Vincular(http);
            if (token is null && codigo is null) return Esperar(1);
        }
        else
        {
            // El idioma en el que se vinculó, que sin navegador ya no hay quien
            // lo diga. Si el fichero es de una versión anterior y no lo trae, se
            // queda el de Windows, que es lo que había antes de esto.
            Textos.Escoger(vinculo!.Idioma);
            Textos.Linea("ya_vinculado");
            Console.WriteLine();
        }
        if (token is not null)
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

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
        var (respuesta, estado, errorRed) = await SubirImportacion(http, codigo, coleccion, log.Fragmentos);
        if (errorRed is not null)
        {
            Textos.Linea("sin_conexion_guardar", errorRed);
            return Esperar(1);
        }
        if (estado != 200)
        {
            // UN TOKEN QUE YA NO VALE responde 401: revocado desde la web, o de
            // una cuenta que ya no existe. Se borra el vínculo aquí mismo, que
            // si no este programa repetiría el mismo rechazo para siempre; sin
            // fichero, la próxima ejecución vuelve a vincular sola.
            if (estado == 401 && token is not null)
            {
                Vinculo.Borrar();
                Console.WriteLine();
                Textos.Linea("vinculo_caducado");
                return Esperar(1);
            }
            Textos.Linea("rechazado", estado);
            return Esperar(1);
        }

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

            /**
             * LA OFERTA DEL MODO DE FONDO VA AQUÍ, ANTES DEL NAVEGADOR.
             *
             * Estaba después, y por eso no la veía nadie: abrir el navegador se
             * lleva el foco, y la pregunta se imprimía en una ventana que ya
             * estaba detrás. Se comprobó en una ejecución real: la consola pasó
             * de «nada se guarda hasta que confirmes» a la cuenta atrás sin que
             * la pregunta llegara a leerse.
             *
             * Aquí todavía está delante y acaba de leerse lo que hizo el
             * programa, que es el momento en que «¿lo dejo funcionando solo?»
             * significa algo.
             */
            await OfrecerArranqueSolo();
            DejarResidenteEnMarcha();

            try { Process.Start(new ProcessStartInfo(revisar) { UseShellExecute = true }); }
            catch { /* la URL de arriba basta si esto falla */ }
            // Y encima de Arena, para quien esté jugando y no mirando esta
            // ventana (ver Superposicion.cs).
            Superposicion.Mostrar(Textos.T("sup_titulo"), Textos.T("sup_pendiente", respuesta.Mazos ?? 0));
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
        Superposicion.Mostrar(Textos.T("sup_titulo"), Textos.T("sup_guardado", respuesta?.CartasGuardadas ?? 0));
        await OfrecerArranqueSolo();
        DejarResidenteEnMarcha();
        return Esperar(0);
    }

    /// <summary>
    /// LA SUBIDA, EN UN SOLO SITIO. La usan el modo normal y el residente, que
    /// hacen cosas muy distintas con el resultado (consola y navegador el uno,
    /// silencio y aviso sobre el juego el otro) pero mandan exactamente lo
    /// mismo.
    ///
    /// Siempre con <c>Revisar: true</c>: nada se guarda sin que la persona diga
    /// qué mazos quiere. Un log trae TODOS los de la cuenta de Arena, y los que
    /// ya existan en la web con el mismo nombre se reescribirían enteros.
    ///
    /// Devuelve el código de estado y, si hubo un fallo de red, su mensaje. No
    /// escribe nada por consola: quien llama decide cómo contarlo.
    /// </summary>
    private static async Task<(RespuestaImportar? Respuesta, int Estado, string? Error)> SubirImportacion(
        HttpClient http, string? codigo, CartaColeccion[]? coleccion, string? fragmentos)
    {
        var cuerpo = new PeticionImportar(codigo, null, [], coleccion, [], fragmentos, Revisar: true);
        try
        {
            var resp = await http.PostAsJsonAsync("/api/mtga-import", cuerpo, JsonOpciones);
            if (!resp.IsSuccessStatusCode) return (null, (int)resp.StatusCode, null);
            return (await resp.Content.ReadFromJsonAsync<RespuestaImportar>(JsonOpciones), 200, null);
        }
        catch (Exception ex)
        {
            return (null, 0, ex.Message);
        }
    }

    /// <summary>
    /// Una pregunta de sí o no por consola, con el mismo medio minuto de plazo
    /// que la de actualizar: quien lanzó esto puede haberse ido, y ninguna
    /// pregunta puede dejar el programa colgado. Sin consola interactiva
    /// contesta que no, que es lo que no cambia nada.
    /// </summary>
    private static async Task<bool> PreguntarSiNo(string clave)
    {
        try
        {
            if (Console.IsInputRedirected) return false;
            Console.WriteLine();
            Textos.Linea(clave);
            Textos.Linea("residente_teclas");
            for (var i = 0; i < 300; i++)
            {
                if (Console.KeyAvailable)
                {
                    var k = Console.ReadKey(true);
                    Console.WriteLine();
                    return k.Key != ConsoleKey.N && k.Key != ConsoleKey.Escape;
                }
                await Task.Delay(100);
            }
            Console.WriteLine();
            return false;
        }
        catch { return false; }
    }

    /// <summary>
    /// SE OFRECE AL FINAL, NO AL PRINCIPIO, y sólo si no está ya puesto.
    ///
    /// Al terminar una importación se acaba de ver para qué sirve el programa,
    /// que es el momento en que «¿lo dejo funcionando solo?» significa algo.
    /// Preguntarlo al arrancar, antes de haber hecho nada, es pedir permiso
    /// para instalarse.
    /// </summary>
    private static async Task OfrecerArranqueSolo()
    {
        if (ArrancaSolo()) return;
        // `lanzar: false`: el residente lo pone en marcha DejarResidenteEnMarcha,
        // que es quien sabe que la subida de esta ejecución ya está hecha.
        if (await PreguntarSiNo("residente_ofrecer")) Inicio(true, lanzar: false);
    }

    /// <summary>
    /// EL MODO RESIDENTE: se queda de fondo y sincroniza SOLO, cuando abres
    /// Arena. Es lo que esta herramienta prometía desde el principio y no podía
    /// cumplir mientras cada ejecución pidiera confirmar en el navegador; con el
    /// ordenador ya vinculado (ver Vinculo.cs) sube con su token y no pregunta
    /// nada.
    ///
    /// EL CICLO, Y POR QUÉ SON DOS SUBIDAS:
    ///   · Al ABRIR Arena, en cuanto la colección está cargada en memoria: es el
    ///     único momento en que se puede leer la colección entera.
    ///   · Al CERRARLO, del log: los mazos que hayas tocado durante la sesión se
    ///     escriben ahí, y a media partida todavía no están.
    ///
    /// NO ABRE EL NAVEGADOR NUNCA. Lo leído se queda pendiente en tu cuenta y la
    /// web avisa cuando entras. Una pestaña nueva cada vez que abres el juego es
    /// exactamente lo que hace que un programa así se desinstale.
    ///
    /// SIN VENTANA: se esconde la consola. Arrancado desde el registro de
    /// Windows saldría una ventana negra en cada inicio de sesión.
    ///
    /// NO TERMINA NUNCA por su cuenta, salvo si el vínculo deja de valer: ahí no
    /// hay nada que hacer sin una persona delante.
    /// </summary>
    private static async Task<int> Residente()
    {
        try { ShowWindow(GetConsoleWindow(), 0); } catch { /* SW_HIDE; sin consola, mejor */ }

        // UNO SOLO. Windows lo lanza al iniciar sesión, y desde la 1.10.0 también
        // se lanza al momento al aceptar el arranque automático: sin esto habría
        // dos procesos subiendo lo mismo y pintando dos columnas.
        using var unico = new Mutex(true, @"Local\MtgCornerArenaBridgeResidente", out var primero);
        if (!primero) return 0;

        var vinculo = Vinculo.Leer();
        if (vinculo is null) return 1;   // sin vincular no hay a quién subir
        Textos.Escoger(vinculo.Idioma);

        using var http = new HttpClient { BaseAddress = new Uri(Sitio), Timeout = TimeSpan.FromMinutes(4) };
        http.DefaultRequestHeaders.Add("X-Bridge-Version", VersionPropia);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", vinculo.Token);

        // Las subidas, de una en una: la del ciclo y la que pide el icono
        // «Importar ahora» pueden coincidir.
        var subiendo = new SemaphoreSlim(1, 1);
        async Task<bool> Subir(CartaColeccion[]? col, string? frag, bool avisar = true, Action<Subida>? alTerminar = null)
        {
            await subiendo.WaitAsync();
            try { return await SubirDeFondo(http, col, frag, avisar, alTerminar); }
            finally { subiendo.Release(); }
        }

        // LA COLUMNA DE ICONOS SOBRE EL JUEGO (ver Columna.cs): lo que hace cada uno.
        Columna.Iniciar(async (accion, dato) =>
        {
            switch (accion)
            {
                case Columna.Accion.Importar:
                    /**
                     * CON RESPUESTA EN PANTALLA, siempre. Antes sólo salía el aviso
                     * cuando había mazos que revisar: si todo estaba ya al día el
                     * icono parecía no hacer nada («no sale nada en pantalla», el
                     * usuario, 2026-09-24). Ahora dice que está subiendo y luego
                     * cómo ha ido: pendiente de tu OK, al día, o que falló.
                     */
                    Superposicion.MostrarSinEsperar(Textos.T("sup_titulo"), Textos.T("col_subiendo"), 30);
                    await Subir(LectorArena.LeerColeccion(out _), LogArena.Leer(LogArena.FicherosPorDefecto(LogArena.Carpeta())).Fragmentos,
                        alTerminar: fin =>
                        {
                            switch (fin)
                            {
                                case Subida.AlDia: Superposicion.MostrarSinEsperar(Textos.T("sup_titulo"), Textos.T("col_al_dia")); break;
                                case Subida.Nada: Superposicion.MostrarSinEsperar(Textos.T("sup_titulo"), Textos.T("col_sin_datos")); break;
                                case Subida.Fallo: Superposicion.MostrarSinEsperar(Textos.T("sup_titulo"), Textos.T("col_fallo")); break;
                                // Pendiente: SubirDeFondo ya ha puesto su aviso.
                            }
                        });
                    break;
                case Columna.Accion.Coleccion:
                    Abrir(Sitio + RutaWeb("coleccion"));
                    break;
                case Columna.Accion.Constructor:
                    Abrir(Sitio + RutaWeb("constructor"));
                    break;
                case Columna.Accion.Arranque:
                    // Sin lanzar otro residente: éste ya lo es.
                    Inicio(!ArrancaSolo(), lanzar: false);
                    Columna.Refrescar();
                    break;
                // Las filas que pone el juego (ver más abajo): abren la web en el
                // sitio exacto, que es quien sabe traducir nombre de mazo → mazo
                // tuyo y arena_id → carta (rutas /api/puente/*).
                case Columna.Accion.Mejorar:
                    if (dato is not null) Abrir($"{Sitio}/api/puente/mazo?nombre={Uri.EscapeDataString(dato)}&idioma={Textos.Idioma}");
                    break;
                case Columna.Accion.Similares:
                    // EN EL JUEGO, no en el navegador: el panel con las cartas
                    // grandes encima de Arena (ver PanelCartas.cs). Si algo
                    // falla -sin red, sin parecidas- se abre la web, que es lo
                    // que hacia antes y sigue valiendo.
                    if (dato is not null)
                    {
                        // La fila pulsada enseña que está en ello (ver Columna.EnCurso);
                        // el panel sólo sale cuando están las ocho cartas, para no
                        // tapar la partida mientras bajan.
                        Columna.EnCurso(dato);
                        bool hecho;
                        try { hecho = await PanelSimilares(http, dato); }
                        finally { Columna.EnCurso(null); }
                        if (!hecho)
                        {
                            var (cara, otraCara) = Caras(dato);
                            Abrir($"{Sitio}/api/puente/carta/{cara}?abrir=similares&idioma={Textos.Idioma}{otraCara}");
                        }
                    }
                    break;
                case Columna.Accion.Combos:
                    if (dato is not null)
                    {
                        Columna.EnCurso(dato);
                        bool hecho;
                        try { hecho = await PanelCombos(http, dato); }
                        finally { Columna.EnCurso(null); }
                        if (!hecho)
                        {
                            var (cara, otraCara) = Caras(dato);
                            Abrir($"{Sitio}/api/puente/carta/{cara}?abrir=combos&idioma={Textos.Idioma}{otraCara}");
                        }
                    }
                    break;
                case Columna.Accion.Sinergias:
                    if (dato is not null)
                    {
                        Columna.EnCurso(dato);
                        bool hechoSin;
                        try { hechoSin = await PanelSinergias(http, dato); }
                        finally { Columna.EnCurso(null); }
                        if (!hechoSin)
                        {
                            var (cara, otraCara) = Caras(dato);
                            Abrir($"{Sitio}/api/puente/carta/{cara}?abrir=sinergias&idioma={Textos.Idioma}{otraCara}");
                        }
                    }
                    break;
                case Columna.Accion.Resumen:
                    Columna.EnCurso("resumen");
                    try { await PanelResumen(http); }
                    finally { Columna.EnCurso(null); }
                    break;
                case Columna.Accion.Amenaza:
                    if (dato is not null && int.TryParse(dato.Replace("amenaza:", ""), out var indiceAmenaza))
                    {
                        Columna.EnCurso(dato);
                        try { await PanelAmenaza(indiceAmenaza); }
                        finally { Columna.EnCurso(null); }
                    }
                    break;
                case Columna.Accion.Salir:
                    Columna.Cerrar();
                    Environment.Exit(0);
                    break;
            }
        }, ArrancaSolo);

        /**
         * LO QUE PASA EN EL JUEGO, EN LA COLUMNA (ver Contexto.cs): el mazo que
         * acabas de guardar o de llevar a la cola, y la carta que señalas en la
         * mesa. El programa sólo conoce el arena_id de la carta; el nombre se le
         * pide a la web una vez y se recuerda, y mientras llega la fila dice
         * «esta carta».
         */
        var nombres = new Dictionary<int, string>();
        var pidiendo = new HashSet<int>();
        // Cuándo se puede volver a preguntar por una carta cuya consulta FALLÓ
        // (red, cortafuegos, la web caída). Sin esto, un tropiezo la marcaba
        // «desconocida» para toda la sesión y la columna se quedaba muda.
        var reintentar = new Dictionary<int, DateTime>();
        void ActualizarColumna()
        {
            var filas = new List<(Columna.Accion, string, string)>();
            if (Contexto.Mazo is { } mazo) filas.Add((Columna.Accion.Mejorar, mazo, Textos.T("col_mejorar", mazo)));
            if (Contexto.Carta is { } grp)
            {
                /**
                 * SIN NOMBRE NO SE OFRECE NADA.
                 *
                 * Decía «Similares a esta carta» mientras llegaba el nombre —o
                 * para siempre, si la carta no se podía resolver—, y eso no
                 * sirve: «no me queda claro cuál es la carta seleccionada»
                 * (el usuario, 2026-09-24). Ahora la fila aparece cuando se
                 * sabe cómo se llama, un instante después, y si no hay manera
                 * de saberlo no aparece: mejor una fila menos que una fila que
                 * no se sabe a qué lleva.
                 */
                string? nombre;
                lock (nombres) nombres.TryGetValue(grp, out nombre);
                if (nombre is null) { _ = NombrarCarta(grp, Contexto.CartaOtraCara); }
                else if (nombre.Length == 0) { /* preguntada y de verdad desconocida (404) */ }
                // El dato lleva las DOS caras si la carta está transformada
                // («96052:96051»): sólo la frontal existe en Scryfall.
                else
                {
                    // El dato lleva las DOS caras si la carta está transformada
                    // («96052:96051»): sólo la frontal existe en Scryfall.
                    var cual = Contexto.CartaOtraCara is { } otra ? $"{grp}:{otra}" : grp.ToString();
                    filas.Add((Columna.Accion.Similares, cual, Textos.T("col_similares", nombre)));
                    filas.Add((Columna.Accion.Combos, cual, Textos.T("col_combos", nombre)));
                    filas.Add((Columna.Accion.Sinergias, cual, Textos.T("col_sinergias", nombre)));
                    // Y POR ADELANTADO: similares, combos y sinergias de esa carta,
                    // con sus imágenes, antes de que nadie pulse nada (ver Precalentar).
                    _ = Precalentar(http, cual);
                }
            }
            // Las amenazas del rival, primero: si tiene un combo montado es lo
            // más urgente que puede decir la columna.
            lock (Amenazas)
            {
                for (var i = 0; i < Amenazas.Count; i++)
                {
                    var (combo, lista) = Amenazas[i];
                    filas.Insert(0, (Columna.Accion.Amenaza, $"amenaza:{i}",
                        Textos.T(lista ? "col_amenaza" : "col_amenaza_casi", combo.Resultado ?? "")));
                }
            }
            // Lo que mira el rival, dicho como tal: es lo único que el registro
            // sabe del ratón (ver Contexto.cs). Pulsarlo abre sus parecidas.
            if (Contexto.RivalMira is { } grpRival && grpRival != Contexto.Carta)
            {
                string? nombreRival;
                lock (nombres) nombres.TryGetValue(grpRival, out nombreRival);
                if (nombreRival is null) { _ = NombrarCarta(grpRival, Contexto.RivalMiraOtraCara); }
                else if (nombreRival.Length > 0)
                {
                    var cual = Contexto.RivalMiraOtraCara is { } otra ? $"{grpRival}:{otra}" : grpRival.ToString();
                    filas.Add((Columna.Accion.Similares, cual, Textos.T("col_rival_mira", nombreRival)));
                }
            }
            // Al acabar una partida, el resumen por IA se queda ofrecido hasta que
            // empieza la siguiente.
            if (partidaPendiente is not null && !Contexto.EnPartida) filas.Add((Columna.Accion.Resumen, "resumen", Textos.T("col_resumen")));
            Columna.FilasDeContexto(filas.ToArray());
        }
        async Task NombrarCarta(int grp, int? otraCara)
        {
            lock (pidiendo)
            {
                if (pidiendo.Contains(grp)) return;
                // Falló hace poco: se espera antes de volver a molestar.
                if (reintentar.TryGetValue(grp, out var cuando) && cuando > DateTime.UtcNow) return;
                pidiendo.Add(grp);
            }
            try
            {
                // /api/mtga-device/…, no /api/puente/…: el cortafuegos de Vercel
                // sólo deja pasar al programa por ese prefijo (ver PuertaDevice).
                using var r = await http.GetAsync($"{PuertaDevice}/carta/{grp}{Caras(otraCara)}");
                if (r.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    /**
                     * SÓLO EL 404 ES «DESCONOCIDA»: la web miró y no existe (una
                     * ficha, un emblema, una carta demasiado nueva). Eso sí se
                     * recuerda para toda la sesión, que si no se preguntaría
                     * por ella cada segundo.
                     *
                     * CUALQUIER OTRO FALLO ES «AHORA NO SE PUEDE»: sin red, la web
                     * caída o —lo que pasó el 2026-09-24— el cortafuegos de
                     * Vercel devolviendo un 429 de desafío a todo lo que no es
                     * un navegador. Antes esto también marcaba la carta como
                     * desconocida y la columna se quedaba sin filas hasta
                     * reiniciar; ahora se vuelve a intentar al minuto.
                     */
                    lock (nombres) nombres[grp] = "";
                }
                else if (r.IsSuccessStatusCode)
                {
                    var c = await r.Content.ReadFromJsonAsync<CartaPuente>(JsonOpciones);
                    if (c?.Nombre is { Length: > 0 } n) { lock (nombres) nombres[grp] = n; }
                    else { lock (pidiendo) reintentar[grp] = DateTime.UtcNow.AddMinutes(1); }
                }
                else
                {
                    lock (pidiendo) reintentar[grp] = DateTime.UtcNow.AddMinutes(1);
                }
            }
            catch
            {
                lock (pidiendo) reintentar[grp] = DateTime.UtcNow.AddMinutes(1);
            }
            finally
            {
                lock (pidiendo) pidiendo.Remove(grp);
                if (Contexto.Carta == grp || Contexto.RivalMira == grp) ActualizarColumna();
            }
        }
        Contexto.Cambio += ActualizarColumna;
        Contexto.Cambio += () => VigilarAmenazas(http, ActualizarColumna);
        Contexto.Cambio += () => { if (Contexto.EnPartida) partidaPendiente = null; };
        Contexto.PartidaAcabada += () =>
        {
            partidaPendiente = Contexto.UltimaPartida;
            Superposicion.MostrarSinEsperar(Textos.T("sup_titulo"), Textos.T("sup_resumen"), 8);
            ActualizarColumna();
        };
        Contexto.Iniciar(Path.Combine(LogArena.Carpeta(), "Player.log"));

        // Lo acaba de lanzar una ejecución normal, que ya ha subido: la primera
        // vuelta no sube nada y se pone a vigilar directamente.
        var recienSubido = Environment.GetCommandLineArgs().Contains("--recien-subido");

        while (true)
        {
            while (!LectorArena.ArenaAbierto()) await Task.Delay(TimeSpan.FromSeconds(20));

            if (!recienSubido)
            {
                // Arena tarda en dejar la colección lista; se reintenta en vez de
                // adivinar un plazo. Si no sale, se sube igual lo del log.
                var coleccion = await EsperarColeccion();
                var primera = LogArena.Leer(LogArena.FicherosPorDefecto(LogArena.Carpeta()));
                if (!await Subir(coleccion, primera.Fragmentos)) return 1;
            }
            recienSubido = false;

            var log = LogArena.Leer(LogArena.FicherosPorDefecto(LogArena.Carpeta()));

            /**
             * DURANTE LA PARTIDA SE VIGILA EL REGISTRO. Cada minuto se relee; si
             * los mazos han cambiado (la huella de los fragmentos es otra: se
             * guardó o se editó un mazo, se llevó otro a la cola) se sube, como
             * mucho una vez cada diez minutos, y SIN aviso encima del juego: a
             * media partida nadie quiere un recuadro. El aviso se guarda para
             * cuando se cierra Arena. Antes sólo se subía al abrir y al cerrar,
             * y un mazo guardado a las nueve no llegaba a la web hasta que se
             * cerraba el juego a la una.
             */
            var huella = Huella(log.Fragmentos);
            var ultimaSubida = DateTime.UtcNow;
            var avisoPendiente = false;
            while (LectorArena.ArenaAbierto())
            {
                await Task.Delay(TimeSpan.FromSeconds(60));
                if (!LectorArena.ArenaAbierto()) break;
                var ahora = LogArena.Leer(LogArena.FicherosPorDefecto(LogArena.Carpeta()));
                var h = Huella(ahora.Fragmentos);
                if (h == huella || ahora.Fragmentos is null || DateTime.UtcNow - ultimaSubida < TimeSpan.FromMinutes(10)) continue;
                if (!await Subir(null, ahora.Fragmentos, avisar: false)) return 1;
                huella = h;
                ultimaSubida = DateTime.UtcNow;
                avisoPendiente = true;
            }

            // Al cerrar: lo que quede por subir; y si ya se subió todo en
            // partida, sólo el aviso que se aguantó.
            var alCerrar = LogArena.Leer(LogArena.FicherosPorDefecto(LogArena.Carpeta()));
            if (alCerrar.Fragmentos is not null && Huella(alCerrar.Fragmentos) != huella)
            {
                if (!await Subir(null, alCerrar.Fragmentos)) return 1;
            }
            else if (avisoPendiente)
            {
                Superposicion.Mostrar(Textos.T("sup_titulo"), Textos.T("sup_actualizado"));
            }
        }
    }

    /// <summary>La huella de los fragmentos del registro: igual huella, nada nuevo que subir.</summary>
    private static string Huella(string? fragmentos) =>
        fragmentos is null ? "" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fragmentos)));

    /// <summary>
    /// «96052:96051» → la cara señalada y, como parámetro, la otra. Una carta
    /// transformada tiene un número por cara y sólo la frontal existe fuera de
    /// Arena, así que se mandan las dos y la web se queda con la que encuentre.
    /// </summary>
    private static (string Cara, string Otra) Caras(string dato)
    {
        var corte = dato.IndexOf(':');
        return corte < 0 ? (dato, "") : (dato[..corte], $"&otra={dato[(corte + 1)..]}");
    }

    /// <summary>La otra cara como parámetro suelto, para la consulta del nombre.</summary>
    private static string Caras(int? otra) => otra is { } o ? $"?otra={o}" : "";

    /// <summary>
    /// EL PANEL DE CARTAS PARECIDAS, ENCIMA DE ARENA.
    ///
    /// La web hace el trabajo —qué se parece a qué, con qué imagen y a dónde
    /// lleva cada carta— en `/api/puente/similares`, y aquí sólo se bajan las
    /// ilustraciones y se pintan. Las imágenes se guardan en %TEMP%: señalar
    /// dos veces la misma carta no las vuelve a bajar, y borrarlas no rompe
    /// nada.
    ///
    /// Mientras tanto se avisa, que bajar ocho cartas tarda un segundo largo y
    /// un icono que no contesta parece roto. Devuelve si se llegó a enseñar;
    /// quien llama decide qué hacer si no.
    /// </summary>
    /// <summary>
    /// LO QUE YA ESTÁ PEDIDO, para no pedirlo dos veces. Similares y combos por
    /// carta (su dato «grp» o «grp:otra»), y qué se está calentando ahora.
    /// </summary>
    private static readonly Dictionary<string, RespuestaSimilares> SimilaresListos = new();
    private static readonly Dictionary<string, RespuestaCombos> CombosListos = new();
    private static readonly HashSet<string> Calentando = new();

    /// <summary>
    /// POR ADELANTADO. Medido el 2026-09-24: la primera consulta de similares de
    /// una carta tarda 2–3 s (la web tiene que preguntar a Scryfall), la misma
    /// en caché 0,1 s, y bajar las ocho imágenes 0,2 s. Como la columna sabe
    /// cuál es tu carta en cuanto la juegas —segundos antes de que pulses
    /// nada—, se piden entonces similares y combos y se dejan las imágenes en
    /// disco: al pulsar, el panel sale al momento. Y de paso queda caliente la
    /// caché de la web para el siguiente que pregunte por esa carta. Sólo para
    /// TU carta: la que mira el rival cambia con cada movimiento de su ratón.
    /// </summary>
    private static async Task Precalentar(HttpClient http, string arena)
    {
        lock (Calentando) { if (!Calentando.Add(arena)) return; }
        try
        {
            await Task.WhenAll(
                Task.Run(async () =>
                {
                    var d = await Similares(http, arena);
                    if (d?.Cartas is { } c) await Task.WhenAll(c.Select(x => BajarImagen(x.Imagen)));
                }),
                Task.Run(async () =>
                {
                    var d = await Combos(http, arena);
                    if (d?.Combos is { } cs) await Task.WhenAll(cs.SelectMany(x => x.Piezas ?? []).Select(p => BajarImagen(p.Imagen)));
                }),
                Task.Run(async () =>
                {
                    var d = await Sinergias(http, arena);
                    if (d?.Grupos is { } gs) await Task.WhenAll(gs.SelectMany(x => x.Cartas ?? []).Select(c => BajarImagen(c.Imagen)));
                }));
        }
        catch { /* si no se pudo calentar, al pulsar se pide como siempre */ }
        finally { lock (Calentando) Calentando.Remove(arena); }
    }

    /// <summary>
    /// Los colores que juegas, como parámetro: las parecidas que se aconsejan
    /// —también las de la carta del rival— tienen que ser jugables con tus
    /// tierras. Va en la clave de la caché porque cambia según se ven cartas.
    /// </summary>
    private static string Colores() => Contexto.MisColores.Length > 0 ? $"&colores={Contexto.MisColores}" : "";

    /// <summary>
    /// LO QUE SE ESTÁ JUGANDO, como parámetros: el formato del mazo de la cola
    /// («standard», «alchemy», «brawl»…) y, en Limitado, la edición del evento.
    /// Con esto las parecidas, los combos y las amenazas son del juego que hay,
    /// no de todo Magic (pedido del usuario el 2026-09-26).
    /// </summary>
    private static string Juego()
    {
        var p = "";
        if (Contexto.Formato is { Length: > 0 } f) p += $"&formato={Uri.EscapeDataString(f)}";
        if (Contexto.Edicion is { Length: > 0 } e) p += $"&edicion={Uri.EscapeDataString(e)}";
        return p;
    }

    private static async Task<RespuestaSimilares?> Similares(HttpClient http, string arena)
    {
        var clave = arena + Colores() + Juego();
        lock (SimilaresListos) { if (SimilaresListos.TryGetValue(clave, out var ya)) return ya; }
        var (cara, otra) = Caras(arena);
        var d = await http.GetFromJsonAsync<RespuestaSimilares>(
            $"{PuertaDevice}/similares?arena={Uri.EscapeDataString(cara)}&idioma={Textos.Idioma}{otra}{Colores()}{Juego()}", JsonOpciones);
        if (d is not null) lock (SimilaresListos) SimilaresListos[clave] = d;
        return d;
    }

    private static async Task<RespuestaCombos?> Combos(HttpClient http, string arena)
    {
        var clave = arena + Juego();
        lock (CombosListos) { if (CombosListos.TryGetValue(clave, out var ya)) return ya; }
        var (cara, otra) = Caras(arena);
        var d = await http.GetFromJsonAsync<RespuestaCombos>(
            $"{PuertaDevice}/combos?arena={Uri.EscapeDataString(cara)}&idioma={Textos.Idioma}{otra}{Juego()}", JsonOpciones);
        if (d is not null) lock (CombosListos) CombosListos[clave] = d;
        return d;
    }

    private static readonly Dictionary<string, RespuestaSinergias> SinergiasListas = new();

    private static async Task<RespuestaSinergias?> Sinergias(HttpClient http, string arena)
    {
        lock (SinergiasListas) { if (SinergiasListas.TryGetValue(arena, out var ya)) return ya; }
        var (cara, otra) = Caras(arena);
        var d = await http.GetFromJsonAsync<RespuestaSinergias>(
            $"{PuertaDevice}/sinergias?arena={Uri.EscapeDataString(cara)}&idioma={Textos.Idioma}{otra}", JsonOpciones);
        if (d is not null) lock (SinergiasListas) SinergiasListas[arena] = d;
        return d;
    }

    /// <summary>
    /// EL PANEL DE SINERGIAS: una sección por regla («las que aprovechan la vida
    /// que da», «las que le dan auras»), con sus cartas. En Estándar de Arena es
    /// lo que más hay: pocos combos de libro, muchas cartas que se pagan entre sí.
    /// </summary>
    private static async Task<bool> PanelSinergias(HttpClient http, string arena)
    {
        try
        {
            var datos = await Sinergias(http, arena);
            var grupos = datos?.Grupos ?? [];
            if (grupos.Length == 0) { Superposicion.MostrarSinEsperar(Textos.T("sup_titulo"), Textos.T("panel_sin_sinergias")); return true; }
            var secciones = new List<PanelCartas.Seccion>();
            foreach (var g in grupos)
            {
                var bajadas = await Task.WhenAll((g.Cartas ?? []).Select(async c => (Carta: c, Fichero: await BajarImagen(c.Imagen))));
                var cartas = bajadas.Where(b => b.Fichero is not null)
                    .Select(b => new PanelCartas.Carta(b.Carta.Nombre ?? "", b.Fichero!, b.Carta.Ruta, b.Carta.PrecioUsd)).ToArray();
                if (cartas.Length == 0) continue;
                secciones.Add(new PanelCartas.Seccion(Textos.T(g.Sentido == "dan" ? "panel_sin_dan" : "panel_sin_pagan", g.Regla ?? ""), null, cartas));
            }
            if (secciones.Count == 0) return false;
            PanelCartas.AlAbrir = AbrirDelPanel;
            PanelCartas.Mostrar(Textos.T("col_sinergias", datos?.Fuente?.Nombre ?? Textos.T("col_esta_carta")), secciones);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// EL RESUMEN DE LA PARTIDA, POR IA. Al acabar una partida, el vigía deja su
    /// cronología en Contexto.UltimaPartida; se manda a la web con el token del
    /// programa y lo que vuelve —por qué se ganó o perdió, qué no había que
    /// hacer, qué hacer la próxima vez— se enseña como texto encima del juego.
    /// Pedido del usuario el 2026-09-26: «críticas constructivas para aprender».
    /// </summary>
    private static Contexto.Partida? partidaPendiente;

    private static async Task<bool> PanelResumen(HttpClient http)
    {
        var partida = partidaPendiente;
        if (partida is null) return false;
        try
        {
            var envio = new PartidaEnvio(partida.Formato, partida.Mazo, partida.Gane, partida.Razon,
                partida.Turnos.Select(t => new TurnoEnvio(t.N, t.Activo, t.VidaYo, t.VidaRival, [.. t.JugadasYo], [.. t.JugadasRival], t.DanoAYo, t.DanoARival)).ToArray());
            using var r = await http.PostAsJsonAsync($"{PuertaDevice}/resumen", new { idioma = Textos.Idioma, partida = envio }, JsonOpciones);
            if (!r.IsSuccessStatusCode) { Superposicion.MostrarSinEsperar(Textos.T("sup_titulo"), Textos.T("sup_resumen_fallo")); return true; }
            var d = await r.Content.ReadFromJsonAsync<RespuestaResumen>(JsonOpciones);
            if (d?.Texto is not { Length: > 0 } texto) { Superposicion.MostrarSinEsperar(Textos.T("sup_titulo"), Textos.T("sup_resumen_fallo")); return true; }
            PanelCartas.AlAbrir = AbrirDelPanel;
            PanelCartas.MostrarTexto(Textos.T("col_resumen"), texto);
            return true;
        }
        catch
        {
            Superposicion.MostrarSinEsperar(Textos.T("sup_titulo"), Textos.T("sup_resumen_fallo"));
            return true;
        }
    }

    /// <summary>
    /// LAS AMENAZAS DEL RIVAL. Cada vez que cambia su mesa (Contexto.MesaRival),
    /// tras dos segundos de calma se pregunta a la web qué combos tiene ya
    /// montados y a cuáles le falta una carta. Los nuevos se AVISAN una vez
    /// encima del juego y se quedan como filas en la columna, que abren un
    /// panel con las piezas. Pedido del usuario el 2026-09-25: «detectar
    /// combinaciones que hacen combos del contrincante y que avise».
    /// </summary>
    private static readonly List<(AmenazaPuente Combo, bool Lista)> Amenazas = new();
    private static readonly HashSet<string> Avisadas = new();
    private static CancellationTokenSource? esperaAmenazas;
    private static string mesaRevisada = "";

    private static void VigilarAmenazas(HttpClient http, Action alCambiar)
    {
        var mesa = string.Join(",", Contexto.MesaRival);
        if (mesa == mesaRevisada) return;
        if (!Contexto.EnPartida || Contexto.MesaRival.Length == 0)
        {
            // Partida acabada o mesa vacía: se olvida todo para la siguiente.
            mesaRevisada = mesa;
            lock (Amenazas) { Amenazas.Clear(); Avisadas.Clear(); }
            alCambiar();
            return;
        }
        esperaAmenazas?.Cancel();
        var cts = esperaAmenazas = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(2000, cts.Token);
                mesaRevisada = mesa;
                var r = await http.GetFromJsonAsync<RespuestaAmenazas>(
                    $"{PuertaDevice}/amenazas?ids={Uri.EscapeDataString(mesa)}&idioma={Textos.Idioma}{Juego()}", JsonOpciones, cts.Token);
                var nuevas = new List<(AmenazaPuente, bool)>();
                foreach (var c in r?.Listos ?? []) nuevas.Add((c, true));
                foreach (var c in (r?.Casi ?? []).Take(2)) nuevas.Add((c, false));
                (AmenazaPuente Combo, bool Lista)? avisar = null;
                lock (Amenazas)
                {
                    Amenazas.Clear();
                    Amenazas.AddRange(nuevas.Take(3));
                    foreach (var n in nuevas)
                        if (n.Item1.Id is { } id && Avisadas.Add(id) && avisar is null) avisar = n;
                }
                if (avisar is { } a)
                {
                    var piezas = string.Join(" + ", (a.Combo.Piezas ?? []).Select(p => p.Nombre));
                    Superposicion.MostrarSinEsperar(Textos.T("sup_titulo"),
                        Textos.T(a.Lista ? "sup_amenaza" : "sup_amenaza_casi", piezas, a.Combo.Resultado ?? ""), 8);
                }
                alCambiar();
            }
            catch { /* cancelado o sin red: a la siguiente */ }
        });
    }

    /// <summary>El panel de una amenaza: sus piezas, y en el rótulo qué hay en la mesa y qué le falta.</summary>
    private static async Task<bool> PanelAmenaza(int indice)
    {
        (AmenazaPuente Combo, bool Lista) a;
        lock (Amenazas) { if (indice < 0 || indice >= Amenazas.Count) return false; a = Amenazas[indice]; }
        var piezas = a.Combo.Piezas ?? [];
        var bajadas = await Task.WhenAll(piezas.Select(async p => (Pieza: p, Fichero: await BajarImagen(p.Imagen))));
        var cartas = bajadas.Where(b => b.Fichero is not null)
            .Select(b => new PanelCartas.Carta(b.Pieza.Nombre ?? "", b.Fichero!, b.Pieza.Ruta, b.Pieza.PrecioUsd)).ToArray();
        if (cartas.Length == 0) return false;
        var enMesa = string.Join(", ", piezas.Where(p => p.EnMesa).Select(p => p.Nombre));
        var faltan = string.Join(", ", piezas.Where(p => !p.EnMesa).Select(p => p.Nombre));
        var rotulo = faltan.Length > 0
            ? Textos.T("panel_mesa_falta", a.Combo.Resultado ?? "", enMesa, faltan)
            : Textos.T("panel_mesa", a.Combo.Resultado ?? "", enMesa);
        PanelCartas.AlAbrir = AbrirDelPanel;
        PanelCartas.Mostrar(Textos.T(a.Lista ? "col_amenaza" : "col_amenaza_casi", a.Combo.Resultado ?? ""),
            [new PanelCartas.Seccion(rotulo, a.Combo.Url, cartas)]);
        return true;
    }

    /// <summary>Abre una ruta del panel: las de la web son relativas; las de Commander Spellbook, absolutas.</summary>
    private static void AbrirDelPanel(string ruta) =>
        Abrir(ruta.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? ruta : Sitio + ruta);

    /// <summary>
    /// EL PANEL DE CARTAS PARECIDAS, ENCIMA DE ARENA.
    ///
    /// La web hace el trabajo —qué se parece a qué, con qué imagen y a dónde
    /// lleva cada carta— en `/api/puente/similares`, y aquí sólo se bajan las
    /// ilustraciones y se pintan. Sin aviso emergente: quien avisa de que se está
    /// buscando es la fila de la columna (Columna.EnCurso), que no tapa nada. El
    /// panel se abre sólo con las cartas ya bajadas, de golpe. Devuelve si se
    /// llegó a enseñar; quien llama decide qué hacer si no.
    /// </summary>
    private static async Task<bool> PanelSimilares(HttpClient http, string arena)
    {
        try
        {
            var datos = await Similares(http, arena);
            var lista = datos?.Cartas ?? [];
            if (lista.Length == 0) { Superposicion.MostrarSinEsperar(Textos.T("sup_titulo"), Textos.T("panel_nada")); return true; }

            var bajadas = await Task.WhenAll(lista.Select(async c => (Carta: c, Fichero: await BajarImagen(c.Imagen))));
            var cartas = bajadas
                .Where(b => b.Fichero is not null)
                .Select(b => new PanelCartas.Carta(b.Carta.Nombre ?? "", b.Fichero!, b.Carta.Ruta, b.Carta.PrecioUsd))
                .ToArray();
            if (cartas.Length == 0) return false;

            PanelCartas.AlAbrir = AbrirDelPanel;
            PanelCartas.Mostrar(Textos.T("col_similares", datos?.Fuente?.Nombre ?? Textos.T("col_esta_carta")), cartas);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// EL PANEL DE COMBOS: una sección por combo, con lo que produce de rótulo
    /// (pulsable: abre el combo en Commander Spellbook) y sus otras piezas
    /// debajo, compactas para que tres combos quepan en una pantalla de 1080.
    /// </summary>
    private static async Task<bool> PanelCombos(HttpClient http, string arena)
    {
        try
        {
            var datos = await Combos(http, arena);
            var combos = datos?.Combos ?? [];
            if (combos.Length == 0) { Superposicion.MostrarSinEsperar(Textos.T("sup_titulo"), Textos.T("panel_sin_combos")); return true; }

            var secciones = new List<PanelCartas.Seccion>();
            foreach (var combo in combos)
            {
                var piezas = combo.Piezas ?? [];
                var bajadas = await Task.WhenAll(piezas.Select(async p => (Pieza: p, Fichero: await BajarImagen(p.Imagen))));
                var cartas = bajadas
                    .Where(b => b.Fichero is not null)
                    .Select(b => new PanelCartas.Carta(b.Pieza.Nombre ?? "", b.Fichero!, b.Pieza.Ruta, b.Pieza.PrecioUsd))
                    .ToArray();
                if (cartas.Length == 0) continue;
                secciones.Add(new PanelCartas.Seccion(combo.Resultado ?? "", combo.Url, cartas));
            }
            if (secciones.Count == 0) return false;

            PanelCartas.AlAbrir = AbrirDelPanel;
            PanelCartas.Mostrar(Textos.T("col_combos", datos?.Fuente?.Nombre ?? Textos.T("col_esta_carta")), secciones);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// La ilustración de una carta, en disco. Ya bajada, se reutiliza: el
    /// nombre del fichero es la huella de su dirección, así que dos cartas
    /// distintas no se pisan y la misma no se baja dos veces.
    /// </summary>
    private static async Task<string?> BajarImagen(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            var carpeta = Path.Combine(Path.GetTempPath(), "MtgCorner", "cartas");
            Directory.CreateDirectory(carpeta);
            var nombre = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..24] + ".jpg";
            var destino = Path.Combine(carpeta, nombre);
            if (File.Exists(destino) && new FileInfo(destino).Length > 0) return destino;

            // Un cliente aparte: las imágenes están en scryfall.io y no llevan
            // —ni deben llevar— el token de la cuenta.
            using var img = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            img.DefaultRequestHeaders.Add("User-Agent", "MtgCornerArenaBridge/" + VersionPropia);
            var bytes = await img.GetByteArrayAsync(url);
            if (bytes.Length == 0) return null;
            await File.WriteAllBytesAsync(destino, bytes);
            return destino;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Abre una dirección en el navegador de siempre; si falla, nada.</summary>
    private static void Abrir(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* sin navegador no hay a dónde ir */ }
    }

    /// <summary>
    /// Las páginas de la web a las que lleva la columna, en el idioma del
    /// vínculo. Las rutas van traducidas (i18n/pathnames.ts en la web), y el
    /// inglés sin prefijo.
    /// </summary>
    private static string RutaWeb(string pagina) => (pagina, Textos.Idioma) switch
    {
        ("coleccion", "es") => "/es/coleccion",
        ("coleccion", "de") => "/de/sammlung",
        ("coleccion", "fr") => "/fr/collection",
        ("coleccion", "pt") => "/pt/colecao",
        ("coleccion", "it") => "/it/collezione",
        ("coleccion", "ja") => "/ja/collection",
        ("coleccion", "zh") => "/zh/collection",
        ("coleccion", _) => "/collection",
        ("constructor", "es") => "/es/constructor-de-mazos",
        ("constructor", "de") => "/de/deckbuilder",
        ("constructor", "fr") => "/fr/constructeur-de-deck",
        ("constructor", "pt") => "/pt/construtor-de-baralhos",
        ("constructor", "it") => "/it/costruttore-di-mazzi",
        ("constructor", "ja") => "/ja/deck-builder",
        ("constructor", "zh") => "/zh/deck-builder",
        ("constructor", _) => "/deck-builder",
        _ => "/",
    };

    /// <summary>
    /// La colección, reintentando mientras Arena siga abierto (hasta cinco
    /// minutos). Recién arrancado el juego todavía no hay nada que leer, y
    /// quedarse con el «no se pudo» de los primeros segundos sería perder la
    /// única lectura buena de la sesión.
    /// </summary>
    private static async Task<CartaColeccion[]?> EsperarColeccion()
    {
        var limite = DateTime.UtcNow.AddMinutes(5);
        while (DateTime.UtcNow < limite && LectorArena.ArenaAbierto())
        {
            var cartas = LectorArena.LeerColeccion(out _);
            if (cartas is not null && cartas.Length > 0) return cartas;
            await Task.Delay(TimeSpan.FromSeconds(20));
        }
        return null;
    }

    /// <summary>
    /// Sube sin consola y avisa encima del juego. Devuelve false SÓLO cuando el
    /// vínculo ha dejado de valer, que es lo único que hace inútil seguir
    /// despierto: lo demás (sin red, un rechazo, nada que subir) se reintenta en
    /// la siguiente sesión de Arena.
    /// </summary>
    /// <summary>Cómo acabó una subida de fondo, para quien la pidió con un icono.</summary>
    private enum Subida { Nada, Pendiente, AlDia, Fallo }

    private static async Task<bool> SubirDeFondo(HttpClient http, CartaColeccion[]? coleccion, string? fragmentos, bool avisar = true, Action<Subida>? alTerminar = null)
    {
        if (coleccion is null && fragmentos is null) { alTerminar?.Invoke(Subida.Nada); return true; }

        var (respuesta, estado, _) = await SubirImportacion(http, null, coleccion, fragmentos);
        if (estado == 401) { Vinculo.Borrar(); alTerminar?.Invoke(Subida.Fallo); return false; }
        if (estado != 200) { alTerminar?.Invoke(Subida.Fallo); return true; }
        if (respuesta?.Pendiente is null) { alTerminar?.Invoke(Subida.AlDia); return true; }

        // `avisar` en falso: a media partida (ver Residente), el aviso se guarda para el cierre.
        if (avisar) Superposicion.MostrarSinEsperar(Textos.T("sup_titulo"), Textos.T("sup_pendiente", respuesta.Mazos ?? 0));
        alTerminar?.Invoke(Subida.Pendiente);
        return true;
    }

    /// <summary>La clave de Windows que arranca programas al iniciar sesión.</summary>
    private const string ClaveInicio = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string NombreInicio = "MtgCornerArenaBridge";

    /// <summary>¿Está puesto para arrancar solo?</summary>
    private static bool ArrancaSolo()
    {
        try
        {
            using var clave = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ClaveInicio);
            return clave?.GetValue(NombreInicio) is not null;
        }
        catch { return false; }
    }

    /// <summary>Lo que hay escrito en el arranque automático, tal cual, o null.</summary>
    private static string? MandatoDeArranque()
    {
        try
        {
            using var clave = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ClaveInicio);
            return clave?.GetValue(NombreInicio) as string;
        }
        catch { return null; }
    }

    /// <summary>
    /// EL ARRANQUE AUTOMÁTICO APUNTA A UN .EXE CONCRETO, Y LA GENTE DESCARGA OTRO.
    ///
    /// Aquí no hay instalador: el programa es un fichero suelto que se vuelve a
    /// descargar con cada versión, y el navegador lo guarda al lado del
    /// anterior con otro nombre —«MtgCornerArenaBridge (9).exe»—. El arranque
    /// automático se escribió una vez con la ruta de aquel día y ahí se quedó,
    /// porque la oferta sólo se hace cuando NO está puesto. Resultado: se usa
    /// la versión nueva a mano y en cada inicio de sesión sigue levantándose la
    /// vieja, que no tiene ni las correcciones ni la columna.
    ///
    /// Visto en una máquina real el 2026-09-24: la 1.10.0 recién descargada y
    /// el registro llamando a una 1.8.0 de hacía cuatro días.
    ///
    /// Así que cuando el arranque está puesto y apunta a OTRO fichero, se
    /// reescribe con el que se está ejecutando ahora y se cierra el residente
    /// viejo, que no sabe apartarse solo: es de antes del mutex.
    /// </summary>
    private static void RefrescarArranque()
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return;
        var mandato = MandatoDeArranque();
        if (mandato is null) return;                                            // no está puesto: no es asunto de aquí
        if (mandato.Contains(exe, StringComparison.OrdinalIgnoreCase)) return;  // ya es éste

        Inicio(true, lanzar: false);
        Textos.Linea("arranque_actualizado");
        CerrarResidentesViejos(exe);
    }

    /// <summary>
    /// Cierra los residentes que sean de OTRO ejecutable. Los de antes de la
    /// 1.10.0 no comparten el mutex, así que dos copias se pisarían: las dos
    /// subiendo lo mismo y, desde la 1.10.0, dos columnas sobre el juego.
    /// </summary>
    private static void CerrarResidentesViejos(string exe)
    {
        try
        {
            var yo = Environment.ProcessId;
            // POR PREFIJO, no por el nombre exacto: cada descarga se llama de
            // su manera —«MtgCornerArenaBridge (7)»— y buscar el nombre del
            // ejecutable de ahora no encontraría precisamente a los viejos,
            // que son los que hay que cerrar.
            foreach (var p in Process.GetProcesses()
                         .Where(p => p.ProcessName.StartsWith("MtgCornerArenaBridge", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    if (p.Id == yo) continue;
                    if (string.Equals(p.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase)) continue;
                    p.Kill();
                    // A que termine de verdad: mientras agoniza sigue teniendo
                    // cogido el mutex del residente, y quien pregunte por él se
                    // creerá que hay uno funcionando.
                    p.WaitForExit(3000);
                }
                catch { /* sin permiso o ya cerrado: el mutex se encarga del resto */ }
                finally { p.Dispose(); }
            }
        }
        catch { /* si no se puede enumerar, se sigue igual */ }
    }

    /// <summary>
    /// Poner o quitar el arranque automático. En la clave del USUARIO y no en la
    /// de la máquina: no hace falta ser administrador, no afecta a nadie más y
    /// se quita igual de fácil. Windows lo lanza al iniciar sesión y el programa
    /// se queda esperando a que abras Arena.
    /// </summary>
    private static int Inicio(bool poner, bool lanzar = true)
    {
        try
        {
            using var clave = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ClaveInicio, writable: true);
            if (clave is null) return 1;
            if (poner)
            {
                var exe = Environment.ProcessPath;
                if (exe is null) return 1;
                clave.SetValue(NombreInicio, "\"" + exe + "\" --residente");
                Textos.Linea("residente_puesto");
                // Y AHORA, no en el siguiente inicio de sesión: quien acaba de
                // decir que sí espera que empiece a funcionar ya. El residente
                // lleva un Mutex, así que si ya hubiera uno, éste se cierra solo.
                if (lanzar) LanzarResidente();
            }
            else
            {
                clave.DeleteValue(NombreInicio, throwOnMissingValue: false);
                Textos.Linea("residente_quitado");
            }
            return 0;
        }
        catch (Exception ex)
        {
            Textos.Linea("residente_fallo", ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// AL TERMINAR, DEJAR LA COLUMNA PUESTA.
    ///
    /// La columna de iconos sobre Arena (ver Columna.cs) vive en el modo
    /// residente, y hasta la 1.10.0 el residente sólo arrancaba al iniciar
    /// sesión en Windows o al aceptar el arranque automático. Así que quien
    /// abría el programa a mano con Arena delante —que es lo normal la primera
    /// vez— importaba sus mazos y no veía ninguna capa por ningún lado
    /// (comunicado por el usuario el 2026-09-24: «lo he ejecutado y voy a Arena
    /// y no veo nada superpuesto»).
    ///
    /// Ahora, al acabar una ejecución normal, el modo de fondo se queda puesto
    /// PARA ESTA SESIÓN DE WINDOWS. Es cosa distinta del arranque automático,
    /// que es el permiso para volver solo después de reiniciar y se sigue
    /// preguntando aparte: decir «no» a aquello no obliga a cerrar esto, y para
    /// quitarlo está «Cerrar MTG Corner» en la propia columna.
    ///
    /// No se lanza si ya hay uno —el residente se protege con este mismo
    /// mutex—, ni sin vínculo, porque sin token el residente se cierra solo.
    /// </summary>
    private static void DejarResidenteEnMarcha()
    {
        if (Vinculo.Leer() is null) return;
        // Antes de mirar si hay uno vivo: si el que arranca con Windows es otro
        // fichero (una versión anterior), se corrige y se cierra aquél. Si no,
        // el mutex de abajo vería vivo al viejo y se dejaría todo como estaba.
        RefrescarArranque();
        if (HayResidente()) return;
        // `--recien-subido`: sin esto el residente subiría otra vez, al segundo
        // de hacerlo esta ejecución, y en la web quedarían DOS revisiones
        // idénticas esperando.
        LanzarResidente("--recien-subido");
        Textos.Linea("columna_puesta");
    }

    /// <summary>
    /// ¿Hay ya un residente vivo? Lo dice su mutex.
    ///
    /// CON REINTENTOS, y no de adorno: justo antes de esto se ha podido cerrar
    /// un residente de una versión anterior, y `Kill()` no espera a que el
    /// proceso termine. Entre que muere y suelta el mutex pasan unos
    /// milisegundos en los que preguntar una sola vez contesta «sí lo hay»: se
    /// daba por hecho que quedaba uno funcionando, no se lanzaba ninguno y la
    /// columna desaparecía del juego (el usuario, 2026-09-24: «he vuelto a
    /// ejecutar el programa y me ha desaparecido la barra vertical»).
    /// </summary>
    private static bool HayResidente()
    {
        for (var intento = 0; ; intento++)
        {
            try
            {
                using var vivo = Mutex.OpenExisting(@"Local\MtgCornerArenaBridgeResidente");
            }
            catch (WaitHandleCannotBeOpenedException) { return false; }   // no hay ninguno
            catch { return true; }                                        // otro problema: no tocar nada
            if (intento >= 10) return true;
            Thread.Sleep(200);
        }
    }

    /// <summary>Arranca el modo de fondo en este momento, escondido.</summary>
    private static void LanzarResidente(string banderas = "")
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is null) return;
            Process.Start(new ProcessStartInfo(exe, ("--residente " + banderas).Trim()) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
            Textos.Linea("residente_lanzado");
        }
        catch { /* si no arranca ahora, arrancará con Windows */ }
    }

    /// <summary>
    /// LA VINCULACIÓN DE LA PRIMERA VEZ: un código de un solo uso, confirmado
    /// en el navegador, que se cambia aquí por un token de larga duración.
    ///
    /// Devuelve el token si el canje salió. Si no salió pero sí la confirmación
    /// —una web anterior a /api/mtga-device/token, o un corte de red en el peor
    /// momento— devuelve el CÓDIGO, que sigue sin gastar: esta ejecución sube
    /// igual, como se hacía antes, y la siguiente volverá a intentarlo. Los dos
    /// nulos significan que no hay nada que hacer, y por qué ya está dicho por
    /// consola.
    /// </summary>
    private static async Task<(string? Token, string? Codigo)> Vincular(HttpClient http)
    {
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
            return (null, null);
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
            return (null, null);
        }
        // Tras confirmar, el visitante está mirando el navegador y aquí sigue
        // habiendo trabajo: esta ventana se trae al frente y, si Windows no deja,
        // al menos parpadea en la barra de tareas.
        TraerAlFrente();
        Textos.Linea("confirmado");

        // EL CANJE. El nombre del equipo viaja para que en la web se puedan
        // distinguir dos ordenadores y revocar el que toque.
        try
        {
            var r = await http.PostAsJsonAsync("/api/mtga-device/token",
                new PeticionToken(codigo, Environment.MachineName), JsonOpciones);
            if (r.IsSuccessStatusCode)
            {
                var d = await r.Content.ReadFromJsonAsync<RespuestaToken>(JsonOpciones);
                if (!string.IsNullOrWhiteSpace(d?.Token))
                {
                    Vinculo.Guardar(d!.Token!, Textos.Idioma);
                    Textos.Linea("vinculo_guardado");
                    Console.WriteLine();
                    return (d.Token, null);
                }
            }
        }
        catch { /* sin token se sigue con el código, que es lo de siempre */ }

        Console.WriteLine();
        return (null, codigo);
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
                /**
                 * LA FOTO DE TU RANGO, también reducida aquí.
                 *
                 * La respuesta de `RankGetCombinedRankInfo` viene de dos formas:
                 * unas veces es tu posición —temporada, nivel, derrotas de ese
                 * nivel— y otras arrastra además la ESCALA entera de la
                 * clasificación (todas las clases y sus escalones, unos 4 KB que
                 * son iguales para todo el mundo). Se envían sólo tus cinco
                 * números: lo otro engorda el envío y no dice nada de nadie.
                 *
                 * No lleva nombres ni identificadores: es en qué punto de la
                 * temporada estás, y es lo que permite dibujar cuándo subiste.
                 */
                if (linea.Contains("<== RankGetCombinedRankInfo") && i + 1 < lineas.Length)
                {
                    hayDetalle = true;
                    var llaveRango = lineas[i + 1].IndexOf('{');
                    if (llaveRango < 0) continue;
                    var rango = RecortarRango(lineas[i + 1][llaveRango..]);
                    if (rango is null) continue;
                    sb.Append("<== RangoMtgCorner\n").Append(rango).Append('\n');
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

    /**
     * TU POSICIÓN EN LA CLASIFICACIÓN, en cinco números.
     *
     * LA CLASE PUEDE NO VENIR, y no es un fallo: Arena omite el valor por
     * defecto al serializar, así que "sin clase" significa la primera —bronce—.
     * Se manda tal cual viene, con su hueco, y es el servidor quien decide qué
     * hacer con la ausencia: aquí no se inventa un dato que el juego no dio.
     *
     * `null` si la línea no es la de tu posición (la misma llamada devuelve a
     * veces la escala entera de la clasificación, igual para todo el mundo).
     */
    private static string? RecortarRango(string json)
    {
        try
        {
            var raiz = JsonNode.Parse(json)?.AsObject();
            if (raiz is null || raiz["constructedSeasonOrdinal"] is null) return null;
            var foto = new JsonObject();
            foreach (var clave in new[] { "constructedSeasonOrdinal", "constructedClass", "constructedLevel", "constructedStep", "constructedMatchesLost" })
            {
                if (raiz[clave] is JsonNode v) foto[clave] = v.DeepClone();
            }
            return foto.Count > 1 ? foto.ToJsonString() : null;
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

/// <summary>Lo que se manda a /api/mtga-device/token para canjear el código ya
/// confirmado por el token de este ordenador. <c>Nombre</c> es el del equipo,
/// para poder distinguirlos en la web.</summary>
internal sealed record PeticionToken(
    [property: JsonPropertyName("codigo")] string Codigo,
    [property: JsonPropertyName("nombre")] string? Nombre);

/// <summary>El token, que se enseña UNA sola vez: en el servidor sólo queda su
/// huella. Ver Vinculo.cs.</summary>
internal sealed record RespuestaToken([property: JsonPropertyName("token")] string? Token);
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
/// <summary>Lo que devuelve /api/puente/sinergias: grupos por regla con sus cartas.</summary>
internal sealed record RespuestaSinergias(
    [property: JsonPropertyName("fuente")] FuenteSimilares? Fuente,
    [property: JsonPropertyName("grupos")] GrupoSinergiaPuente[]? Grupos);

internal sealed record GrupoSinergiaPuente(
    [property: JsonPropertyName("regla")] string? Regla,
    [property: JsonPropertyName("sentido")] string? Sentido,
    [property: JsonPropertyName("cartas")] CartaSimilar[]? Cartas);

/// <summary>El resumen de la partida que devuelve /api/mtga-device/resumen.</summary>
internal sealed record RespuestaResumen([property: JsonPropertyName("texto")] string? Texto);

/// <summary>La partida que se manda para el resumen: lo mismo que Contexto.Partida, con nombres JSON.</summary>
internal sealed record PartidaEnvio(
    [property: JsonPropertyName("formato")] string? Formato,
    [property: JsonPropertyName("miMazo")] string? MiMazo,
    [property: JsonPropertyName("gane")] bool? Gane,
    [property: JsonPropertyName("razon")] string? Razon,
    [property: JsonPropertyName("turnos")] TurnoEnvio[] Turnos);

internal sealed record TurnoEnvio(
    [property: JsonPropertyName("n")] int N,
    [property: JsonPropertyName("activo")] string Activo,
    [property: JsonPropertyName("vidaYo")] int? VidaYo,
    [property: JsonPropertyName("vidaRival")] int? VidaRival,
    [property: JsonPropertyName("jugadasYo")] int[] JugadasYo,
    [property: JsonPropertyName("jugadasRival")] int[] JugadasRival,
    [property: JsonPropertyName("danoAYo")] int DanoAYo,
    [property: JsonPropertyName("danoARival")] int DanoARival);

/// <summary>Lo que devuelve /api/puente/amenazas: los combos del rival montados en la mesa y a una carta.</summary>
internal sealed record RespuestaAmenazas(
    [property: JsonPropertyName("listos")] AmenazaPuente[]? Listos,
    [property: JsonPropertyName("casi")] AmenazaPuente[]? Casi);

internal sealed record AmenazaPuente(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("resultado")] string? Resultado,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("piezas")] PiezaAmenaza[]? Piezas);

internal sealed record PiezaAmenaza(
    [property: JsonPropertyName("nombre")] string? Nombre,
    [property: JsonPropertyName("imagen")] string? Imagen,
    [property: JsonPropertyName("precioUsd")] double? PrecioUsd,
    [property: JsonPropertyName("ruta")] string? Ruta,
    [property: JsonPropertyName("enMesa")] bool EnMesa);

/// <summary>Lo que devuelve /api/puente/combos: de qué carta se parte y sus combos, cada uno con lo que produce y sus otras piezas.</summary>
internal sealed record RespuestaCombos(
    [property: JsonPropertyName("fuente")] FuenteSimilares? Fuente,
    [property: JsonPropertyName("combos")] ComboPuente[]? Combos);

internal sealed record ComboPuente(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("resultado")] string? Resultado,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("piezas")] CartaSimilar[]? Piezas);

/// <summary>Lo que devuelve /api/puente/similares: de qué carta se parte y las parecidas.</summary>
internal sealed record RespuestaSimilares(
    [property: JsonPropertyName("fuente")] FuenteSimilares? Fuente,
    [property: JsonPropertyName("cartas")] CartaSimilar[]? Cartas);

internal sealed record FuenteSimilares(
    [property: JsonPropertyName("nombre")] string? Nombre,
    [property: JsonPropertyName("ruta")] string? Ruta);

internal sealed record CartaSimilar(
    [property: JsonPropertyName("nombre")] string? Nombre,
    [property: JsonPropertyName("imagen")] string? Imagen,
    [property: JsonPropertyName("precioUsd")] double? PrecioUsd,
    [property: JsonPropertyName("ruta")] string? Ruta);

/// <summary>Lo que devuelve /api/puente/carta/&lt;arena_id&gt;: el nombre para la columna.</summary>
internal sealed record CartaPuente([property: JsonPropertyName("nombre")] string? Nombre);

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
    // Nulo en cuanto el ordenador está vinculado: entonces quien dice quién
    // eres es la cabecera Authorization: Bearer (ver Vinculo.cs).
    [property: JsonPropertyName("codigo")] string? Codigo,
    [property: JsonPropertyName("comodines")] object? Comodines,
    [property: JsonPropertyName("mazos")] object[] Mazos,
    [property: JsonPropertyName("coleccion")] CartaColeccion[]? Coleccion,
    [property: JsonPropertyName("avisos")] string[] Avisos,
    [property: JsonPropertyName("fragmentosLog")] string? FragmentosLog,
    /// <summary>Que el servidor lo deje pendiente para elegir en el navegador, sin guardar.</summary>
    [property: JsonPropertyName("revisar")] bool Revisar
);
