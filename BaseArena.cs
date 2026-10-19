using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MtgCornerArenaBridge;

/// <summary>
/// LA BASE DE DATOS DE CARTAS DE ARENA, la que trae su instalación
/// (<c>MTGA_Data/Downloads/Raw/Raw_CardDatabase_&lt;hash&gt;.mtga</c>, un SQLite).
///
/// Para los nombres de las cartas que nadie más conoce todavía: las de Alchemy
/// recién salidas (las «Y26» del draft de Reality Fracture del 2026-10-19) no
/// están ni en 17lands, ni en Scryfall con su número de Arena, ni en nuestro
/// índice, y la columna del draft las enseñaba como «sin datos». Arena sí las
/// tiene todas: <c>Cards(GrpId, TitleId, …)</c> y el nombre en
/// <c>Localizations_enUS(LocId, Loc)</c> por su TitleId.
///
/// Con el SQLITE QUE TRAE WINDOWS (<c>winsqlite3.dll</c>, en System32 desde
/// Windows 10): ninguna librería más en el ejecutable. Se abre SÓLO PARA LEER y
/// como inmutable (<c>immutable=1</c>), sin bloqueos: Arena puede estar usándola.
/// Si algo falla, no hay nombre y la fila sigue como antes.
/// </summary>
internal static class BaseArena
{
    private const string Sqlite = "winsqlite3.dll";
    [DllImport(Sqlite, CallingConvention = CallingConvention.StdCall)] private static extern int sqlite3_open_v2(byte[] nombre, out IntPtr db, int flags, IntPtr vfs);
    [DllImport(Sqlite, CallingConvention = CallingConvention.StdCall)] private static extern int sqlite3_close(IntPtr db);
    [DllImport(Sqlite, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)] private static extern int sqlite3_prepare16_v2(IntPtr db, string sql, int bytes, out IntPtr stmt, IntPtr cola);
    [DllImport(Sqlite, CallingConvention = CallingConvention.StdCall)] private static extern int sqlite3_step(IntPtr stmt);
    [DllImport(Sqlite, CallingConvention = CallingConvention.StdCall)] private static extern int sqlite3_column_int(IntPtr stmt, int col);
    [DllImport(Sqlite, CallingConvention = CallingConvention.StdCall)] private static extern IntPtr sqlite3_column_text16(IntPtr stmt, int col);
    [DllImport(Sqlite, CallingConvention = CallingConvention.StdCall)] private static extern int sqlite3_finalize(IntPtr stmt);

    private const int SQLITE_OPEN_READONLY = 0x1, SQLITE_OPEN_URI = 0x40, SQLITE_ROW = 100;

    /// <summary>Los nombres ya leídos, por GrpId: la base no cambia mientras dura la sesión.</summary>
    private static readonly Dictionary<int, string> nombres = new();

    /// <summary>
    /// Dónde está la base: la carpeta de datos de Arena sale de la primera línea
    /// de su registro («Mono path[0] = 'D:/Games/…/MTGA_Data/Managed'»), y si no,
    /// del proceso del juego. De los ficheros de la base, el más reciente.
    /// </summary>
    private static string? Fichero()
    {
        string? datos = null;
        try
        {
            var registro = LogArena.FicherosPorDefecto(LogArena.Carpeta()).LastOrDefault(File.Exists);
            if (registro is not null)
            {
                using var fs = new FileStream(registro, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var sr = new StreamReader(fs);
                var primera = sr.ReadLine() ?? "";
                var i = primera.IndexOf("'", StringComparison.Ordinal);
                var j = primera.IndexOf("/Managed'", StringComparison.Ordinal);
                if (i >= 0 && j > i) datos = primera[(i + 1)..j];
            }
        }
        catch { /* se prueba con el proceso */ }
        if (datos is null || !Directory.Exists(datos))
        {
            try
            {
                var exe = Process.GetProcessesByName("MTGA").FirstOrDefault()?.MainModule?.FileName;
                if (exe is not null) datos = Path.Combine(Path.GetDirectoryName(exe)!, "MTGA_Data");
            }
            catch { return null; }
        }
        if (datos is null) return null;
        var raw = Path.Combine(datos, "Downloads", "Raw");
        if (!Directory.Exists(raw)) return null;
        return new DirectoryInfo(raw).GetFiles("Raw_CardDatabase_*.mtga").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName;
    }

    /// <summary>El nombre en inglés de cada GrpId que la base conozca.</summary>
    internal static IReadOnlyDictionary<int, string> Nombres(IEnumerable<int> grps)
    {
        var pedidos = grps.Distinct().ToArray();
        var salida = new Dictionary<int, string>();
        int[] faltan;
        lock (nombres)
        {
            foreach (var g in pedidos) if (nombres.TryGetValue(g, out var n)) salida[g] = n;
            faltan = pedidos.Where(g => !nombres.ContainsKey(g)).ToArray();
        }
        if (faltan.Length == 0) return salida;
        try
        {
            var fichero = Fichero();
            if (fichero is null) return salida;
            var uri = new Uri(fichero).AbsoluteUri + "?mode=ro&immutable=1";
            if (sqlite3_open_v2(Encoding.UTF8.GetBytes(uri + "\0"), out var db, SQLITE_OPEN_READONLY | SQLITE_OPEN_URI, IntPtr.Zero) != 0) { sqlite3_close(db); return salida; }
            try
            {
                // Los números van pegados en la consulta: son enteros nuestros, no texto de nadie.
                var sql = "select c.GrpId, min(l.Loc) from Cards c join Localizations_enUS l on l.LocId = c.TitleId " +
                          $"where c.GrpId in ({string.Join(",", faltan)}) group by c.GrpId";
                if (sqlite3_prepare16_v2(db, sql, -1, out var stmt, IntPtr.Zero) != 0) return salida;
                try
                {
                    while (sqlite3_step(stmt) == SQLITE_ROW)
                    {
                        var g = sqlite3_column_int(stmt, 0);
                        var n = Marshal.PtrToStringUni(sqlite3_column_text16(stmt, 1));
                        if (n is { Length: > 0 }) salida[g] = n;
                    }
                }
                finally { sqlite3_finalize(stmt); }
            }
            finally { sqlite3_close(db); }
            lock (nombres) foreach (var g in faltan) if (salida.TryGetValue(g, out var n)) nombres[g] = n;
        }
        catch { /* sin SQLite de Windows o sin base: sin nombres */ }
        return salida;
    }
}
