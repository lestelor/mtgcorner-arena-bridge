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
///   · QUÉ CARTA ACABAS DE JUGAR: las anotaciones <c>ZoneTransfer</c> de cada
///     cambio de estado dicen qué objeto pasa a qué zona; si la zona es el
///     campo de batalla o la pila y el objeto es tuyo, esa es «tu carta de
///     ahora». Los números son INSTANCIAS en la mesa, no cartas: la carta
///     (<c>grpId</c>, el <c>arena_id</c> de Scryfall), la zona y el dueño de
///     cada instancia los da el propio estado (<c>"instanceId": N, "grpId": M,
///     … "zoneId": Z, … "ownerSeatId": S</c>), y las zonas se numeran distinto
///     en cada partida (<c>"zoneId": 28, "type": "ZoneType_Battlefield"</c>).
///     Se cruza todo aquí. Tu asiento lo dice <c>systemSeatIds</c>.
///   · QUÉ CARTA MIRA EL RIVAL: <c>uiMessage.onHover {objectId}</c>. OJO: ese
///     aviso lo manda el servidor al OTRO jugador para enseñarle «tu oponente
///     está mirando…», así que en tu registro SÓLO están los del rival; lo que
///     señalas tú no sale de tu cliente y no queda escrito en ningún sitio.
///     Se descubrió el 2026-09-24 con 95 avisos seguidos, todos del asiento
///     contrario: la columna ofrecía «similares a Fabled Passage», una tierra
///     del rival. Se enseña, pero diciendo de quién es.
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

    /// <summary>TU última carta jugada —entró en el campo o en la pila— (su <c>grpId</c> = arena_id), o null.</summary>
    public static int? Carta { get; private set; }

    /// <summary>La carta que está mirando EL RIVAL (su <c>grpId</c>), o null. Ver la cabecera: es lo único que el registro sabe del ratón.</summary>
    public static int? RivalMira { get; private set; }

    /// <summary>La otra cara de la que mira el rival, si está transformada.</summary>
    public static int? RivalMiraOtraCara { get; private set; }

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
    // Tipo, zona y dueño van justo detrás del grpId y en ese orden; la otra cara,
    // más lejos. Todo opcional y acotado, que las líneas de estado son largas.
    private static readonly Regex Objeto = new(
        @"""instanceId"":\s*(\d+),\s*""grpId"":\s*(\d+)" +
        @"(?:,\s*""type"":\s*""GameObjectType_(\w+)"")?" +
        @"(?:,\s*""zoneId"":\s*(\d+))?" +
        @"(?:[^{]{0,120}?""ownerSeatId"":\s*(\d))?" +
        @"(?:[^{]{0,160}?""cardTypes"":\s*\[\s*""CardType_(\w+)"")?" +
        @"(?:[^{]{0,400}?""othersideGrpId"":\s*(\d+))?", RegexOptions.Compiled);
    private static readonly Regex Zona = new(@"""zoneId"":\s*(\d+),\s*""type"":\s*""ZoneType_(\w+)""", RegexOptions.Compiled);
    // Un traslado: qué instancia y a qué zona llega. Los detalles son una lista de
    // pares y `zone_dest` no es el primero, así que se salta con `.{0,400}?`.
    private static readonly Regex Traslado = new(
        @"""affectedIds"":\s*\[\s*(\d+)[^\]]*\],\s*""type"":\s*\[\s*""AnnotationType_ZoneTransfer""\s*\].{0,400}?""zone_dest"".{0,80}?""valueInt32"":\s*\[\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex Asiento = new(@"""systemSeatIds"":\s*\[\s*(\d)", RegexOptions.Compiled);
    // El aviso de «está mirando»: de qué asiento y qué objeto.
    private static readonly Regex BajoRaton = new(@"""seatIds"":\s*\[\s*(\d)\s*\],\s*""onHover"":\s*\{\s*""objectId"":\s*(\d+)", RegexOptions.Compiled);
    private const string PrefijoPrecon = "?=?Loc/";

    /// <summary>Instancia en la mesa → la carta que es, de quién, en qué zona y, si está transformada, su otra cara.</summary>
    private static readonly Dictionary<int, (int Grp, bool EsCarta, bool EsTierra, int? Zona, int? Dueno, int? Otra)> objetos = new();
    /// <summary>Zona → su tipo («Battlefield», «Stack», «Hand»…). Los números cambian en cada partida.</summary>
    private static readonly Dictionary<int, string> zonas = new();
    /// <summary>Tu asiento en la partida en curso (1 o 2), o 0 si aún no se sabe.</summary>
    private static int miAsiento;
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
            zonas.Clear();
            miAsiento = 0;
            Cambiar(null, null, null, null, null, false);
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

        string? mazo = Mazo; int? carta = Carta; int? otraCara = CartaOtraCara;
        int? rival = RivalMira; int? rivalOtra = RivalMiraOtraCara; bool enPartida = EnPartida;
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
            // Tu asiento: lo dice cada mensaje del servidor hacia ti.
            var asiento = Asiento.Match(linea);
            if (asiento.Success) miAsiento = int.Parse(asiento.Groups[1].Value);

            foreach (Match z in Zona.Matches(linea)) zonas[int.Parse(z.Groups[1].Value)] = z.Groups[2].Value;

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
                    var id = int.Parse(m.Groups[1].Value);
                    var esCarta = !m.Groups[3].Success || m.Groups[3].Value == "Card";
                    int? zona = m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : null;
                    int? dueno = m.Groups[5].Success ? int.Parse(m.Groups[5].Value) : null;
                    var esTierra = m.Groups[6].Success && m.Groups[6].Value == "Land";
                    int? otra = m.Groups[7].Success ? int.Parse(m.Groups[7].Value) : null;
                    objetos[id] = (int.Parse(m.Groups[2].Value), esCarta, esTierra, zona, dueno, otra);
                }

                /**
                 * TU CARTA DE AHORA: la última tuya que llega al campo o a la pila.
                 *
                 * Los traslados van en la misma línea que los objetos, así que a
                 * esta altura la instancia ya está en la tabla con su dueño. Sólo
                 * cartas (no habilidades ni fichas), sólo tuyas, y sólo a zonas
                 * que signifiquen jugar: robar a la mano o ir al cementerio no
                 * es «lo que estás jugando». Y SIN TIERRAS: la mitad de lo que se
                 * juega son tierras y «similares a Plains» no le sirve a nadie;
                 * la carta de ahora es tu último hechizo o criatura.
                 */
                foreach (Match t in Traslado.Matches(linea))
                {
                    if (!objetos.TryGetValue(int.Parse(t.Groups[1].Value), out var obj)) continue;
                    if (!obj.EsCarta || obj.EsTierra || miAsiento == 0 || obj.Dueno != miAsiento) continue;
                    if (!zonas.TryGetValue(int.Parse(t.Groups[2].Value), out var tipo)) continue;
                    if (tipo != "Battlefield" && tipo != "Stack") continue;
                    carta = obj.Grp;
                    otraCara = obj.Otra;
                }
            }
            if (linea.Contains("onHover", StringComparison.Ordinal))
            {
                var m = BajoRaton.Match(linea);
                if (m.Success && objetos.TryGetValue(int.Parse(m.Groups[2].Value), out var obj) && obj.EsCarta)
                {
                    // Del asiento que sea distinto del tuyo: es lo que mira el rival.
                    // (Si algún día Arena escribiera los tuyos, irían a tu carta.)
                    var quien = int.Parse(m.Groups[1].Value);
                    if (miAsiento != 0 && quien == miAsiento) { carta = obj.Grp; otraCara = obj.Otra; }
                    else { rival = obj.Grp; rivalOtra = obj.Otra; }
                }
            }
            if (linea.Contains("MatchGameRoomStateType_Playing", StringComparison.Ordinal)) enPartida = true;
            if (linea.Contains("MatchGameRoomStateType_MatchCompleted", StringComparison.Ordinal))
            {
                enPartida = false;
                carta = null;
                otraCara = null;
                rival = null;
                rivalOtra = null;
                objetos.Clear();
                zonas.Clear();
            }
        }
        Cambiar(mazo, carta, otraCara, rival, rivalOtra, enPartida);
    }

    private static void Cambiar(string? mazo, int? carta, int? otraCara, int? rival, int? rivalOtra, bool enPartida)
    {
        if (mazo == Mazo && carta == Carta && otraCara == CartaOtraCara
            && rival == RivalMira && rivalOtra == RivalMiraOtraCara && enPartida == EnPartida) return;
        Mazo = mazo; Carta = carta; CartaOtraCara = otraCara; RivalMira = rival; RivalMiraOtraCara = rivalOtra; EnPartida = enPartida;
        try { Cambio?.Invoke(); } catch { /* lo que haga quien escucha es cosa suya */ }
    }

    /// <summary>Quita los escapes de una cadena JSON que venía dentro de otra (\" → ", \ → \).</summary>
    private static string Desescapar(string s) => s.Replace("\\\"", "\"").Replace("\\\\", "\\");
}
