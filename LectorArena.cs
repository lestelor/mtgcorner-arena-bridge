using System.Diagnostics;

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

    private static dynamic ServicioInventario(IAssemblyImage imagen) =>
        imagen["WrapperController"]["<Instance>k__BackingField"]["<InventoryManager>k__BackingField"]["InventoryServiceWrapper"];

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
