using System.Text.RegularExpressions;

namespace MtgCornerArenaBridge;

/// <summary>
/// LO QUE ARENA ESTÁ HACIENDO AHORA, leído de su registro mientras se juega.
///
/// Arena no admite complementos ni deja leer su interfaz, pero escribe un
/// registro detallado (Player.log, con «Detailed Logs» puesto) y en él cuenta
/// tres cosas que bastan para acompañar al juego como hace Untapped:
///
///   · QUÉ MAZO acabas de guardar o de llevar a la cola: <c>==> DeckUpsertDeckV3</c>
///     y <c>==> EventSetDeckV3</c>, con su <c>Summary.Name</c>. Es «el mazo de ahora»
///     y lo que la columna ofrece mejorar.
///   · QUÉ CARTA SEÑALAS EN LA MESA durante la partida: <c>uiMessage.onHover
///     {objectId}</c>. Ese número es la INSTANCIA en la mesa, no la carta; la
///     carta (<c>grpId</c>, que es el <c>arena_id</c> de Scryfall) la da el estado
///     de la partida, que va escribiendo <c>"instanceId": N, "grpId": M</c> para
///     cada objeto. Se cruzan aquí.
///   · CUÁNDO EMPIEZA Y ACABA la partida: <c>MatchGameRoomStateType_Playing</c> y
///     <c>MatchGameRoomStateType_MatchCompleted</c>. Al acabar, la mesa se vacía.
///
/// Inventario de eventos medido sobre un registro real el 2026-09-24. Fuera de
/// la partida NO se sabe qué carta miras (colección, constructor, tienda): eso
/// sólo estaría en la memoria de Unity, que se rompe con cada actualización, y
/// no se construye nada sobre ello.
///
/// SE LEE POR DETRÁS, no entero cada vez: se recuerda hasta dónde se llegó y
/// se leen sólo los bytes nuevos, cada segundo. Un registro de una sesión larga
/// pasa de 25 MB, y releerlo cada vez sería lo que notaría el juego. Si el
/// fichero encoge es que Arena lo ha vuelto a empezar (lo rota al arrancar):
/// se empieza de cero y se olvida la mesa.
/// </summary>
internal static class Contexto
{
    /// <summary>El nombre del mazo guardado o llevado a la cola más reciente, o null.</summary>
    public static string? Mazo { get; private set; }

    /// <summary>La carta bajo el ratón en la mesa (su <c>grpId</c> = arena_id), o null.</summary>
    public static int? Carta { get; private set; }

    /// <summary>
    /// LA OTRA CARA de esa carta, si está transformada, o null.
    ///
    /// Arena numera por separado las dos caras —una Saga vuelta criatura tiene
    /// un número distinto del de la Saga— y sólo la frontal existe en
    /// Scryfall: señalando la cara vuelta, la web contestaba «desconocida»
    /// (visto el 2026-09-24 con el 96052). El propio objeto del registro dice
    /// cuál es la otra, así que se guarda y se manda con ella.
    /// </summary>
    public static int? CartaOtraCara { get; private set; }

    /// <summary>Si hay una partida en marcha.</summary>
    public static bool EnPartida { get; private set; }

    /// <summary>Algo de lo de arriba ha cambiado. Salta en el hilo del vigía.</summary>
    public static event Action? Cambio;

    // El nombre del mazo va dentro del `request`, que es JSON escapado dentro
    // de una cadena JSON: las comillas llegan como \" . Se acepta cualquier
    // escape dentro del nombre (\" o \), y se desescapa después.
    private static readonly Regex NombreMazo = new(@"\\\x22Name\\\x22:\\\x22((?:[^\x22\\]|\\.)*?)\\\x22", RegexOptions.Compiled);
    // Hasta `othersideGrpId` si lo hay, sin salirse del objeto: `[^{]` frena en
    // cuanto empieza el siguiente, que es lo que separa uno de otro. Y ACOTADO
    // a 400 caracteres: sin tope, en una línea de estado con cientos de objetos
    // la parte opcional hace retroceder al motor una y otra vez y la lectura
    // pasa de instantánea a insoportable. `othersideGrpId` va siempre cerca del
    // principio del objeto.
    private static readonly Regex Objeto = new(@"""instanceId"":\s*(\d+),\s*""grpId"":\s*(\d+)(?:[^{]{0,400}?""othersideGrpId"":\s*(\d+))?", RegexOptions.Compiled);
    private static readonly Regex BajoRaton = new(@"""onHover"":\s*\{\s*""objectId"":\s*(\d+)", RegexOptions.Compiled);
    private const string PrefijoPrecon = "?=?Loc/";

    /// <summary>Instancia en la mesa → la carta que es y, si está transformada, su otra cara.</summary>
    private static readonly Dictionary<int, (int Grp, int? Otra)> objetos = new();
    private static string? fichero;
    private static long posicion;
    private static bool estrenando = true;
    private static Thread? hilo;

    /// <summary>
    /// CUÁNTO SE MIRA HACIA ATRÁS AL EMPEZAR.
    ///
    /// El registro de una sesión larga pasa de los 35 MB, y leerlo entero al
    /// arrancar es medio minuto de trabajo, cientos de megas de memoria y la
    /// columna sin enterarse de nada mientras tanto (le pasó al usuario el
    /// 2026-09-24: el programa en 630 MB y sin ofrecer similares). No hace
    /// falta: lo único que se busca es lo de AHORA —el mazo de la cola, los
    /// objetos de la mesa, la carta bajo el ratón—, y eso cabe de sobra en el
    /// último medio mega, porque Arena reescribe el estado entero de la partida
    /// cada dos por tres.
    /// </summary>
    private const long COLA_AL_EMPEZAR = 4 * 1024 * 1024;

    /// <summary>Empieza a vigilar ese fichero (aunque todavía no exista). Una vez.</summary>
    public static void Iniciar(string rutaLog)
    {
        if (hilo is not null) return;
        fichero = rutaLog;
        hilo = new Thread(Vigilar) { IsBackground = true, Name = "contexto" };
        hilo.Start();
    }

    private static void Vigilar()
    {
        while (true)
        {
            try { Leer(); }
            catch { /* el fichero puede estar a medio escribir: a la siguiente */ }
            Thread.Sleep(1000);
        }
    }

    private static void Leer()
    {
        if (fichero is null || !File.Exists(fichero)) return;
        using var fs = new FileStream(fichero, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (fs.Length < posicion)
        {
            // Arena lo ha vuelto a empezar: sesión nueva, mesa nueva.
            posicion = 0;
            objetos.Clear();
            Cambiar(null, null, null, false);
        }
        if (estrenando)
        {
            // Sólo la cola: ver COLA_AL_EMPEZAR.
            estrenando = false;
            posicion = Math.Max(0, fs.Length - COLA_AL_EMPEZAR);
        }
        if (fs.Length == posicion) return;
        fs.Seek(posicion, SeekOrigin.Begin);
        using var sr = new StreamReader(fs, System.Text.Encoding.UTF8);
        var texto = sr.ReadToEnd();
        posicion = fs.Length;

        string? mazo = Mazo; int? carta = Carta; int? otraCara = CartaOtraCara; bool enPartida = EnPartida;
        foreach (var linea in texto.Split('\n'))
        {
            if (linea.Contains("DeckUpsertDeckV3", StringComparison.Ordinal) || linea.Contains("EventSetDeckV3", StringComparison.Ordinal))
            {
                var m = NombreMazo.Match(linea);
                if (m.Success)
                {
                    var nombre = Desescapar(m.Groups[1].Value);
                    if (nombre.Length > 0 && !nombre.StartsWith(PrefijoPrecon, StringComparison.Ordinal)) mazo = nombre;
                }
            }
            if (linea.Contains("\"grpId\"", StringComparison.Ordinal))
            {
                foreach (Match m in Objeto.Matches(linea))
                {
                    /**
                     * VACIARLO ENTERO ERA PEOR QUE NO TENER TOPE.
                     *
                     * Con 5.000 objetos se limpiaba de golpe, y como el número
                     * que llega al señalar una carta es el de la INSTANCIA en
                     * la mesa —creada a lo mejor diez turnos antes—, después de
                     * cada limpieza el juego tenía que volver a describirla
                     * para poder reconocerla. Mientras tanto, la columna no
                     * ofrecía nada. Cincuenta mil caben en un megabyte largo y
                     * el vaciado de verdad es el del final de la partida.
                     */
                    if (objetos.Count > 50000) objetos.Clear();
                    int? otra = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : null;
                    objetos[int.Parse(m.Groups[1].Value)] = (int.Parse(m.Groups[2].Value), otra);
                }
            }
            if (linea.Contains("onHover", StringComparison.Ordinal))
            {
                var m = BajoRaton.Match(linea);
                if (m.Success && objetos.TryGetValue(int.Parse(m.Groups[1].Value), out var obj))
                {
                    carta = obj.Grp;
                    otraCara = obj.Otra;
                }
            }
            if (linea.Contains("MatchGameRoomStateType_Playing", StringComparison.Ordinal)) enPartida = true;
            if (linea.Contains("MatchGameRoomStateType_MatchCompleted", StringComparison.Ordinal))
            {
                enPartida = false;
                carta = null;
                otraCara = null;
                objetos.Clear();
            }
        }
        Cambiar(mazo, carta, otraCara, enPartida);
    }

    private static void Cambiar(string? mazo, int? carta, int? otraCara, bool enPartida)
    {
        if (mazo == Mazo && carta == Carta && otraCara == CartaOtraCara && enPartida == EnPartida) return;
        Mazo = mazo; Carta = carta; CartaOtraCara = otraCara; EnPartida = enPartida;
        try { Cambio?.Invoke(); } catch { /* lo que haga quien escucha es cosa suya */ }
    }

    /// <summary>Quita los escapes de una cadena JSON que venía dentro de otra (\" → ", \ → \).</summary>
    private static string Desescapar(string s) => s.Replace("\\\"", "\"").Replace("\\\\", "\\");
}
