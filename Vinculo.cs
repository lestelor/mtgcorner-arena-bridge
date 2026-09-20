using System.Text.Json;

namespace MtgCornerArenaBridge;

/// <summary>
/// EL VÍNCULO CON TU CUENTA, GUARDADO EN ESTE ORDENADOR.
///
/// Hasta ahora cada ejecución abría el navegador para que confirmaras quién
/// eras. Vale para un programa que lanzas tú a propósito; no vale para uno que
/// se queda de fondo y sincroniza solo cada vez que abres Arena, que es a donde
/// va esto: nadie quiere una pestaña nueva cada partida.
///
/// Así que se vincula UNA vez. El código de un solo uso se cambia por un token
/// de larga duración (/api/mtga-device/token) y ese token se guarda aquí; a
/// partir de ahí el programa sube con él y no vuelve a preguntar.
///
/// DÓNDE, Y POR QUÉ AHÍ. En %APPDATA%\MtgCornerArenaBridge y no al lado del
/// .exe: el ejecutable es un fichero suelto que la gente descarga, mueve de
/// carpeta y vuelve a descargar, y el vínculo tiene que sobrevivir a eso. Y no
/// en el registro, porque una carpeta se puede abrir y borrar a mano.
///
/// EL TOKEN VA EN CLARO, a sabiendas. Cifrarlo con DPAPI no protegería de nada
/// real: cualquier programa que corra como tú podría descifrarlo igual, y ese
/// mismo programa ya puede leer la memoria de Arena. Sólo escondería el fichero
/// de quien se lo llevara a otro ordenador. A cambio, lo que sí hay es un botón
/// para revocarlo en la web, que deja el token muerto en el acto (la fila se
/// borra: ver sql/2026-09-20-mtga-dispositivos.sql en el repositorio de la web).
///
/// NADA DE ESTO PUEDE TUMBAR UNA IMPORTACIÓN. Si el disco está lleno, la carpeta
/// es de sólo lectura o el fichero está corrupto, se sigue como el primer día:
/// abriendo el navegador. Por eso todo aquí se traga sus errores.
/// </summary>
internal static class Vinculo
{
    /// <summary>Lo guardado: el token, y el idioma en el que se vinculó.</summary>
    /// <remarks>
    /// El IDIOMA se guarda porque, sin navegador, ya no hay quien lo diga. La
    /// web lo manda al confirmar (ver Textos.cs) y ésa era la única vez que
    /// este programa sabía que hablas español aunque tu Windows esté en inglés.
    /// Guardándolo, la segunda ejecución sigue hablándote igual que la primera.
    /// </remarks>
    internal sealed record Dispositivo(string Token, string? Idioma, DateTime Creado);

    private static readonly JsonSerializerOptions Opciones = new(JsonSerializerDefaults.Web);

    private static string Carpeta => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MtgCornerArenaBridge");

    /// <summary>Dónde vive el vínculo. Público para poder decirlo por consola.</summary>
    public static string Fichero => Path.Combine(Carpeta, "dispositivo.json");

    /// <summary>El vínculo de otras veces, o <c>null</c> si no hay o no se pudo leer.</summary>
    public static Dispositivo? Leer()
    {
        try
        {
            if (!File.Exists(Fichero)) return null;
            var d = JsonSerializer.Deserialize<Dispositivo>(File.ReadAllText(Fichero), Opciones);
            return string.IsNullOrWhiteSpace(d?.Token) ? null : d;
        }
        catch
        {
            // Un fichero a medias de una ejecución cortada: se hace como si no
            // hubiera vínculo y se vuelve a vincular, que cuesta diez segundos.
            return null;
        }
    }

    public static void Guardar(string token, string? idioma)
    {
        try
        {
            Directory.CreateDirectory(Carpeta);
            var d = new Dispositivo(token, idioma, DateTime.UtcNow);
            File.WriteAllText(Fichero, JsonSerializer.Serialize(d, Opciones));
        }
        catch
        {
            // Sin poder guardarlo esta ejecución funciona igual; la siguiente
            // volverá a pedir el navegador. Molesto, pero no roto.
        }
    }

    /// <summary>
    /// Se llama cuando el servidor dice que este token ya no vale (revocado
    /// desde la web, o de una cuenta que ya no existe). Dejarlo puesto sería
    /// repetir el mismo rechazo para siempre; borrándolo, la próxima ejecución
    /// vuelve a vincular sola.
    /// </summary>
    public static void Borrar()
    {
        try { if (File.Exists(Fichero)) File.Delete(Fichero); }
        catch { /* si no se deja borrar, el 401 se repetirá: no es peor que esto */ }
    }
}
