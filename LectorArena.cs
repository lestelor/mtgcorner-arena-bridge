using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

using HackF5.UnitySpy;
using HackF5.UnitySpy.Detail;
using HackF5.UnitySpy.Offsets;
using HackF5.UnitySpy.ProcessFacade;

namespace MtgCornerArenaBridge;

/// <summary>
/// TU COLECCIÓN, LEÍDA DE LA MEMORIA DE MTG ARENA. SÓLO LECTURA: se abre el
/// proceso para leer, nunca para escribir, y no se toca ningún fichero del juego.
///
/// ANTES ERA OTRO PROGRAMA. Hasta el 2026-09-15 esto lo hacía mtga-tracker-daemon
/// como proceso aparte: había que llevarlo al lado en un zip, arrancarlo, buscarle
/// un puerto libre y hablarle por HTTP. De ahí salieron casi todos los fallos
/// reales: el zip abierto sin extraer (el .exe se copia solo a una carpeta
/// temporal y el lector se queda dentro), un puerto ocupado, una consola anterior
/// sin cerrar. Desde el 2026-09-16 su código va DENTRO de este programa
/// (vendor/HackF5.UnitySpy, del mismo proyecto, GPLv3) y se lee aquí mismo: un
/// único ejecutable, sin procesos sueltos ni puertos.
///
/// CÓMO SE LEE. Arena es un juego de Unity con runtime Mono: UnitySpy localiza
/// en la memoria del proceso las estructuras de Mono a partir de unas posiciones
/// conocidas por versión de Unity, y desde ahí llega a los objetos del juego. La
/// colección cuelga de WrapperController.Instance → InventoryManager →
/// InventoryServiceWrapper → Cards, un diccionario de id de carta a copias.
///
/// SI ARENA CAMBIA ESA RUTA, <see cref="Diagnostico"/> la recorre paso a paso y
/// dice dónde se rompe y qué campos hay en ese punto, que es lo único que permite
/// arreglarla sin tener Arena delante.
/// </summary>
internal static class LectorArena
{
    /// <summary>El nombre del proceso de Arena en Windows.</summary>
    private const string NombreProceso = "MTGA";

    public static bool ArenaAbierto() => BuscarProceso() is not null;

    /// <summary>El proceso de Arena, para la sonda de posiciones (ProbarOffsets.cs).</summary>
    public static Process? BuscarProcesoPublico() => BuscarProceso();

    private static Process? BuscarProceso()
    {
        try { return Process.GetProcessesByName(NombreProceso).FirstOrDefault(); }
        catch { return null; }
    }

    /// <summary>Espera a que Arena esté abierto, hasta el plazo dado.</summary>
    public static async Task<bool> EsperarArena(TimeSpan plazo)
    {
        var limite = DateTime.UtcNow + plazo;
        while (DateTime.UtcNow < limite)
        {
            if (ArenaAbierto()) return true;
            await Task.Delay(1000);
        }
        return false;
    }

    private static IAssemblyImage AbrirImagen()
    {
        var proceso = BuscarProceso() ?? throw new InvalidOperationException("MTG Arena is not running.");
        var acceso = new ProcessFacadeWindows(proceso);
        var posiciones = MonoLibraryOffsets.GetOffsets(acceso.GetMainModuleFileName());
        // "Core" es el ensamblado del juego donde viven WrapperController y compañía.
        return AssemblyImageFactory.Create(new UnityProcessFacade(acceso, posiciones), "Core");
    }

    /// <summary>
    /// EL SERVICIO DE INVENTARIO, POR DONDE ESTÉ HOY.
    ///
    /// Durante años colgó de `WrapperController.Instance`. Con la actualización
    /// de Arena a Unity 6 (septiembre de 2026) ese campo estático se quedó NULO
    /// para siempre: la clase sigue ahí y otros estáticos suyos se leen bien
    /// —`wrapperSceneEverLoaded` devuelve `true`—, pero el juego ya no guarda
    /// ahí su instancia. Se comprobó a mano con Arena abierto y en partida.
    ///
    /// Donde sí está es en `PAPA`, el objeto raíz del cliente, que expone el
    /// mismo `InventoryManager`. Se prueban los dos por ese orden, y el viejo
    /// se deja porque no cuesta nada y cubre a quien no haya actualizado el
    /// juego.
    ///
    /// SI ALGÚN DÍA SE MUEVE OTRA VEZ, el camino para encontrarlo está en
    /// ProbarOffsets.cs: lista los estáticos vivos y lo que cuelga de ellos.
    /// </summary>
    private static dynamic ServicioInventario(IAssemblyImage imagen)
    {
        foreach (var raiz in new Func<dynamic?>[]
        {
            () => imagen["PAPA"]["_instance"],
            () => imagen["WrapperController"]["<Instance>k__BackingField"],
        })
        {
            dynamic? objeto;
            try { objeto = raiz(); }
            catch { continue; }
            if (objeto is null) continue;

            try
            {
                dynamic? servicio = objeto["<InventoryManager>k__BackingField"]["InventoryServiceWrapper"];
                if (servicio is not null) return servicio;
            }
            catch { /* esa raíz no lleva al inventario: se prueba la siguiente */ }
        }

        throw new InvalidOperationException(
            "couldn't reach the inventory: neither PAPA nor WrapperController has it "
            + "(run --probar-offsets with Arena open to find where it moved)");
    }

    /// <summary>
    /// Las cartas con al menos una copia. <c>null</c> si no se pudo leer, con el
    /// motivo en <paramref name="error"/> (el mensaje de la excepción de verdad).
    /// </summary>
    public static CartaColeccion[]? LeerColeccion(out string? error)
    {
        try
        {
            object[] entradas = ServicioInventario(AbrirImagen())["<Cards>k__BackingField"]["_entries"];
            var cartas = new List<CartaColeccion>();
            foreach (var entrada in entradas)
            {
                if (entrada is not ManagedStructInstance carta) continue;
                var copias = carta.GetValue<int>("value");
                if (copias <= 0) continue;
                cartas.Add(new CartaColeccion((int)carta.GetValue<uint>("key"), copias));
            }
            error = cartas.Count > 0 ? null : "the collection came back empty";
            return cartas.Count > 0 ? cartas.ToArray() : null;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            return null;
        }
    }

    // ── ARENA ABIERTO COMO ADMINISTRADOR (2026-11-22) ─────────────────────
    //
    // Si Arena corre con permisos de administrador y este programa no, Windows
    // no deja leer su memoria: «Win32Exception: Acceso denegado», tres intentos
    // y sin colección. Arena no los pide (su lanzador y el juego van como
    // «asInvoker»): los hereda de quien lo abre, como «Ejecutar como
    // administrador» o un instalador que lo relanza al acabar. Le pasó al usuario
    // el 2026-10-25 y el 2026-11-22. Esperar no lo arregla, así que se dice qué
    // pasa y se ofrece leerla con permiso: una copia de este programa, con el
    // aviso de Windows, que sólo lee, deja la colección en un fichero y se va.
    // Nada se queda funcionando como administrador.

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint acceso, bool heredar, int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr proceso, uint acceso, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr token, int clase, out int valor, int largo, out int devuelto);
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, TOKEN_QUERY = 0x0008;
    private const int TokenElevation = 20;

    /// <summary>¿Corre ese proceso como administrador? null si no se puede saber.</summary>
    private static bool? Elevado(int pid)
    {
        var proceso = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (proceso == IntPtr.Zero) return null;
        try
        {
            if (!OpenProcessToken(proceso, TOKEN_QUERY, out var token)) return null;
            try { return GetTokenInformation(token, TokenElevation, out var valor, sizeof(int), out _) ? valor != 0 : null; }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(proceso); }
    }

    /// <summary>
    /// Arena corre como administrador y este programa no: la colección no se
    /// puede leer hasta que se abra Arena normal o se lea con permiso.
    /// </summary>
    public static bool ArenaSinPermiso()
    {
        try
        {
            var arena = BuscarProceso();
            return arena is not null && Elevado(arena.Id) == true && Elevado(Environment.ProcessId) == false;
        }
        catch { return false; }
    }

    /// <summary>El fichero que deja la copia con permiso: siempre en la carpeta temporal y con este nombre.</summary>
    private static readonly Regex NombreFichero = new(@"^mtgcorner-coleccion-[0-9a-f]{32}\.json$");

    /// <summary>
    /// La colección leída por una copia de este programa con permiso de
    /// administrador. Sale el aviso de Windows; si se dice que no, si Arena se
    /// cierra o si tarda más de dos minutos, null.
    /// </summary>
    public static async Task<CartaColeccion[]?> LeerConPermiso()
    {
        var ruta = Path.Combine(Path.GetTempPath(), $"mtgcorner-coleccion-{Guid.NewGuid():N}.json");
        try
        {
            var inicio = new ProcessStartInfo(Environment.ProcessPath!, $"--leer-coleccion-a \"{ruta}\"")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var copia = Process.Start(inicio);
            if (copia is null) return null;
            using var plazo = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try { await copia.WaitForExitAsync(plazo.Token); }
            catch (OperationCanceledException) { try { copia.Kill(); } catch { } return null; }
            if (!File.Exists(ruta)) return null;
            var leidas = JsonSerializer.Deserialize<CartaColeccion[]>(await File.ReadAllTextAsync(ruta));
            return leidas is { Length: > 0 } ? leidas : null;
        }
        // 1223: se dijo que no en el aviso de Windows.
        catch { return null; }
        finally { try { File.Delete(ruta); } catch { } }
    }

    /// <summary>
    /// Lo que hace la copia con permiso (<c>--leer-coleccion-a</c>): leer la
    /// colección y dejarla en el fichero. Nada más: ni enlace, ni arranque, ni
    /// red. Sólo escribe un fichero con el nombre que pone <see cref="LeerConPermiso"/>.
    /// </summary>
    public static int EscribirColeccionEn(string ruta)
    {
        try
        {
            var destino = Path.GetFullPath(ruta);
            if (!NombreFichero.IsMatch(Path.GetFileName(destino))) return 2;
            var cartas = LeerColeccion(out _);
            if (cartas is null) return 1;
            File.WriteAllText(destino, JsonSerializer.Serialize(cartas));
            return 0;
        }
        catch { return 1; }
    }

    /// <summary>
    /// El recorrido de la ruta de la colección, paso a paso, para enviarlo cuando
    /// falla: en qué paso se rompe, qué campos hay en ese punto y qué tipos del
    /// juego se parecen a los que se buscan.
    /// </summary>
    public static List<string> Diagnostico()
    {
        var informe = new List<string>();
        try
        {
            var imagen = AbrirImagen();
            informe.Add("Core assembly loaded");

            try
            {
                var parecidos = imagen.TypeDefinitions
                    .Where(t => t?.Name is not null && (t.Name.Contains("WrapperController") || t.Name.Contains("Inventory")))
                    .Select(t => t.FullName).Distinct().Take(40).ToList();
                informe.Add("types like WrapperController/Inventory: " + string.Join(", ", parecidos));
            }
            catch (Exception ex)
            {
                informe.Add("listing types failed: " + ex.GetType().Name + ": " + ex.Message);
            }

            ITypeDefinition? tipo = null;
            try { tipo = imagen.GetTypeDefinition("WrapperController"); }
            catch (Exception ex) { informe.Add("WrapperController lookup failed: " + ex.GetType().Name + ": " + ex.Message); }

            if (tipo is null)
            {
                informe.Add("WrapperController type: NOT FOUND");
                return informe;
            }

            informe.Add("WrapperController -> " + Campos(tipo));
            object? actual = Paso(informe, "<Instance>k__BackingField", () => tipo.GetStaticValue<object>("<Instance>k__BackingField"));
            actual = Siguiente(informe, actual, "<InventoryManager>k__BackingField");
            actual = Siguiente(informe, actual, "InventoryServiceWrapper");
            actual = Siguiente(informe, actual, "<Cards>k__BackingField");
            actual = Siguiente(informe, actual, "_entries");
            if (actual is object[] entradas) informe.Add("_entries length: " + entradas.Length);
        }
        catch (Exception ex)
        {
            informe.Add("EXCEPTION: " + ex.GetType().Name + ": " + ex.Message);
        }
        return informe;
    }

    private static string Campos(ITypeDefinition? tipo)
    {
        if (tipo is null) return "<unknown type>";
        try
        {
            var nombres = tipo.Fields.Where(f => f is not null).Select(f => f.Name).Take(80);
            return tipo.FullName + " { " + string.Join(", ", nombres) + " }";
        }
        catch (Exception ex)
        {
            return tipo.FullName + " { fields unreadable: " + ex.GetType().Name + ": " + ex.Message + " }";
        }
    }

    private static object? Paso(List<string> informe, string nombre, Func<object?> leer)
    {
        try
        {
            var valor = leer();
            if (valor is null)
            {
                informe.Add(nombre + " = null");
                return null;
            }
            informe.Add(valor is IManagedObjectInstance objeto
                ? nombre + " -> " + Campos(objeto.TypeDefinition)
                : nombre + " -> " + valor.GetType().Name);
            return valor;
        }
        catch (Exception ex)
        {
            informe.Add(nombre + " FAILED: " + ex.GetType().Name + ": " + ex.Message);
            return null;
        }
    }

    private static object? Siguiente(List<string> informe, object? actual, string campo)
    {
        if (actual is null) return null;
        if (actual is not IManagedObjectInstance objeto)
        {
            informe.Add("cannot read " + campo + " from " + actual.GetType().Name);
            return null;
        }
        return Paso(informe, campo, () => objeto.GetValue<object>(campo));
    }
}
