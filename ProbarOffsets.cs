using HackF5.UnitySpy;
using HackF5.UnitySpy.Offsets;
using HackF5.UnitySpy.ProcessFacade;

namespace MtgCornerArenaBridge;

/// <summary>
/// DE TALLER: ¿por dónde se llega hoy a la colección?
///
/// Arena se actualizó a Unity 6 y el camino de siempre
/// (WrapperController.Instance → InventoryManager → InventoryServiceWrapper)
/// devuelve nulo. YA SE SABE que no son las posiciones de Mono: otros campos
/// estáticos de esa misma clase se leen bien (`wrapperSceneEverLoaded = True`).
/// Lo que ha cambiado es DÓNDE guarda el juego su instancia.
///
/// Esto recorre los ensamblados y enseña todos los estáticos con valor cuyo
/// nombre huela a inventario, colección o singleton, que es por donde tiene que
/// salir el camino nuevo. Sólo en inglés, como los demás modos de taller.
/// </summary>
internal static class ProbarOffsets
{
    /// <summary>Los ensamblados donde puede vivir esto, por orden de sospecha.</summary>
    private static readonly string[] Ensamblados =
    [
        "Core", "SharedClientCore", "Assembly-CSharp", "MTGA", "Wizards.MTGA",
    ];

    public static int Ejecutar()
    {
        var proceso = LectorArena.BuscarProcesoPublico();
        if (proceso is null)
        {
            Console.WriteLine("MTG Arena is not running. Open it, get to the home screen and try again.");
            return 1;
        }

        var acceso = new ProcessFacadeWindows(proceso);
        var offsets = MonoLibraryOffsets.GetOffsets(acceso.GetMainModuleFileName());

        var imagen = AssemblyImageFactory.Create(new UnityProcessFacade(acceso, offsets), "Core");

        // El objeto raíz del juego. WrapperController.Instance ya no vale, pero
        // PAPA sigue vivo: se mira qué cuelga de él.
        dynamic? papa = null;
        try { papa = imagen["PAPA"]["_instance"]; }
        catch (Exception ex) { Console.WriteLine("PAPA: " + ex.GetType().Name); }
        if (papa is null) { Console.WriteLine("PAPA._instance = null"); return 1; }

        Console.WriteLine("=== fields of PAPA._instance ===");
        Hijos(papa, "  ");

        // Y un nivel más por los que suenen a inventario o a controlador.
        foreach (var rama in new[] { "_wrapperController", "<WrapperController>k__BackingField", "_inventoryManager", "<InventoryManager>k__BackingField" })
        {
            dynamic? hijo = null;
            try { hijo = papa[rama]; } catch { continue; }
            if (hijo is null) continue;
            Console.WriteLine();
            Console.WriteLine($"=== fields of PAPA.{rama} ===");
            Hijos(hijo, "  ");
        }

        return 0;
    }

    /// <summary>Los campos de un objeto, con el tipo de cada valor.</summary>
    private static void Hijos(dynamic objeto, string sangria)
    {
        if (objeto is not IManagedObjectInstance instancia) { Console.WriteLine(sangria + "(not an object)"); return; }
        foreach (var campo in instancia.TypeDefinition?.Fields ?? [])
        {
            if (campo?.Name is null || campo.TypeInfo?.IsStatic == true) continue;
            string valor;
            try
            {
                var v = instancia[campo.Name];
                valor = v is null ? "null" : v is IManagedObjectInstance o ? $"<{o.TypeDefinition?.Name}>" : Convert.ToString((object)v) ?? "?";
            }
            catch (Exception ex) { valor = "- (" + ex.GetType().Name + ")"; }
            if (valor == "null") continue;
            Console.WriteLine($"{sangria}{campo.Name} = {valor}");
        }
    }

    /// <summary>Los nombres por los que puede asomar la colección.</summary>
    private static bool Interesa(string nombre) =>
        nombre.Contains("Inventory") || nombre.Contains("Collection") || nombre.Contains("CardPool")
        || nombre.Contains("WrapperController") || nombre.Contains("ClientCore") || nombre.Contains("Session");

    /// <summary>El valor de un estático, o null si no hay nada que enseñar.</summary>
    private static string? Leer(ITypeDefinition tipo, string campo)
    {
        try
        {
            var valor = tipo.GetStaticValue<dynamic>(campo);
            if (valor is null) return null;
            if (valor is IManagedObjectInstance objeto) return $"<{objeto.TypeDefinition?.Name ?? "object"}>";
            var texto = Convert.ToString((object)valor);
            return string.IsNullOrEmpty(texto) || texto == "False" || texto == "0" ? null : texto;
        }
        catch
        {
            return null;
        }
    }
}
