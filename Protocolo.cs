using Microsoft.Win32;

namespace MtgCornerArenaBridge;

/// <summary>
/// EL ENLACE <c>mtgcorner://</c>: que un botón de la web abra este programa.
///
/// Un navegador no puede lanzar un programa del disco, y navegar a
/// <c>file://</c> desde <c>https://</c> está bloqueado de raíz. La única puerta
/// que Windows deja es un PROTOCOLO PROPIO, el mismo mecanismo con el que
/// <c>zoommtg://</c> abre Zoom o <c>spotify://</c> abre Spotify: el programa se
/// apunta en el registro como quien atiende ese esquema y, a partir de ahí, un
/// enlace <c>mtgcorner://importar</c> en la web lo abre con el permiso que pide
/// el navegador la primera vez.
///
/// SE REGISTRA EN CADA EJECUCIÓN, no sólo la primera: el ejecutable es un
/// fichero suelto que la gente mueve de carpeta y vuelve a descargar, y la
/// clave guarda su RUTA. Registrarlo siempre es la forma de que la ruta sea la
/// del .exe que está corriendo ahora. Son cuatro valores en la rama del
/// USUARIO (<c>HKCU\Software\Classes</c>): sin administrador y sin tocar a nadie
/// más.
///
/// SÓLO SE ATIENDE LA ACCIÓN «importar». Cualquier web puede poner un enlace
/// <c>mtgcorner://loquesea</c>, así que lo que llega por aquí no puede hacer
/// nada que la persona no habría hecho con un doble clic: importar es el flujo
/// normal con su consola, y lo demás se ignora.
/// </summary>
internal static class Protocolo
{
    private const string Esquema = "mtgcorner";

    /// <summary>La versión de la web que enseña el botón «Abrir el programa» (BotonPuenteArena): hace falta ≥ 1.10.0.</summary>
    public static void Registrar()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is null) return;
            using var raiz = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + Esquema);
            if (raiz is null) return;
            raiz.SetValue("", "URL:MTG Corner");
            raiz.SetValue("URL Protocol", "");
            using (var icono = raiz.CreateSubKey("DefaultIcon")) icono?.SetValue("", $"\"{exe}\",0");
            using (var abrir = raiz.CreateSubKey(@"shell\open\command")) abrir?.SetValue("", $"\"{exe}\" \"%1\"");
        }
        catch
        {
            // Sin registro no hay enlace, pero el programa sigue funcionando con
            // el doble clic de siempre.
        }
    }

    /// <summary>
    /// La acción del argumento <c>mtgcorner:…</c>, en minúsculas y sin barras
    /// («importar»), o <c>null</c> si el programa no se abrió por el enlace.
    /// </summary>
    public static string? Accion(string[] args)
    {
        var a = args.FirstOrDefault(x => x.StartsWith(Esquema + ":", StringComparison.OrdinalIgnoreCase));
        if (a is null) return null;
        var resto = a[(Esquema.Length + 1)..].Trim('/', ' ');
        var corte = resto.IndexOfAny(['?', '#']);
        if (corte >= 0) resto = resto[..corte];
        return resto.Trim('/').ToLowerInvariant();
    }
}
