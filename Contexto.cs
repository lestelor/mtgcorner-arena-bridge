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
    /// LOS COLORES QUE ESTÁS JUGANDO, como letras («WU»): la unión de los colores
    /// de tus propias cartas vistas en la partida, mano incluida. Sirve para
    /// que las parecidas que se aconsejen sean jugables con tus tierras.
    /// </summary>
    public static string MisColores { get; private set; } = "";

    /// <summary>
    /// LA MESA DEL RIVAL: las cartas suyas (su <c>grpId</c>) que hay ahora en el
    /// campo de batalla, sin repetir. Con esto se buscan sus combos montados y
    /// los que tiene a una carta (ver Program.RevisarAmenazas).
    /// </summary>
    public static int[] MesaRival { get; private set; } = [];

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

    /// <summary>El formato del mazo de la cola («standard», «alchemy», «brawl»…), en minúsculas, o null.</summary>
    public static string? Formato { get; private set; }

    /// <summary>El evento de la cola («Ladder», «QuickDraft_EOE_…»), o null.</summary>
    public static string? Evento { get; private set; }

    /// <summary>En Limitado, el código de la edición que se está jugando (de «QuickDraft_EOE_…» → «eoe»), o null.</summary>
    public static string? Edicion { get; private set; }

    /// <summary>Un turno de la partida en curso, para el resumen. Las jugadas son grpIds.</summary>
    public sealed class Turno
    {
        public int N;
        public string Activo = "yo";
        public int? VidaYo, VidaRival;
        public readonly List<int> JugadasYo = new(), JugadasRival = new();
        public int DanoAYo, DanoARival;
        public readonly HashSet<int> Vistas = new();
    }

    /// <summary>Una partida terminada, tal como se manda a la web para el resumen.</summary>
    public sealed record Partida(string? Formato, string? Mazo, bool? Gane, string? Razon, IReadOnlyList<Turno> Turnos);

    /// <summary>La última partida terminada, con su cronología, o null.</summary>
    public static Partida? UltimaPartida { get; private set; }

    /// <summary>Acaba de terminar una partida: UltimaPartida está puesta. Salta en el hilo del vigía.</summary>
    public static event Action? PartidaAcabada;

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
        @"(?:[^{]{0,200}?""color"":\s*\[([^\]]{0,120}?)\])?" +
        @"(?:[^{]{0,400}?""othersideGrpId"":\s*(\d+))?", RegexOptions.Compiled);
    private static readonly Regex Zona = new(@"""zoneId"":\s*(\d+),\s*""type"":\s*""ZoneType_(\w+)""", RegexOptions.Compiled);
    // Un traslado: qué instancia y a qué zona llega. Los detalles son una lista de
    // pares y `zone_dest` no es el primero, así que se salta con `.{0,400}?`.
    private static readonly Regex Traslado = new(
        @"""affectedIds"":\s*\[\s*(\d+)[^\]]*\],\s*""type"":\s*\[\s*""AnnotationType_ZoneTransfer""\s*\].{0,400}?""zone_dest"".{0,80}?""valueInt32"":\s*\[\s*(\d+)", RegexOptions.Compiled);
    // SÓLO los mensajes dirigidos a un asiento: los hay para los dos («[ 1, 2 ]»)
    // y tomar el primero de ésos te sentaba en el 1 cada pocos segundos
    // (visto el 2026-09-26: los colores propios alternaban con los del rival).
    private static readonly Regex Asiento = new(@"""systemSeatIds"":\s*\[\s*(\d)\s*\]", RegexOptions.Compiled);
    // El evento y el formato van en el `request` de EventSetDeckV3, JSON escapado
    // dentro de una cadena: \x22 es la comilla, \\ la barra que la precede.
    private static readonly Regex EventoCola = new(@"\\\x22EventName\\\x22:\\\x22([A-Za-z0-9_-]+)", RegexOptions.Compiled);
    private static readonly Regex FormatoMazo = new(@"\\\x22name\\\x22:\\\x22Format\\\x22,\\\x22value\\\x22:\\\x22([A-Za-z]+)", RegexOptions.Compiled);
    // La cronología: turno y jugador activo, vidas por asiento, equipo por
    // asiento, daño a un jugador (los jugadores son los objetos 1 y 2), y el
    // resultado.
    private static readonly Regex TurnoInfo = new(@"""turnNumber"":\s*(\d+),\s*""activePlayer"":\s*(\d)", RegexOptions.Compiled);
    private static readonly Regex Vida = new(@"""lifeTotal"":\s*(-?\d+),\s*""systemSeatNumber"":\s*(\d)", RegexOptions.Compiled);
    private static readonly Regex Equipo = new(@"""systemSeatNumber"":\s*(\d),.{0,200}?""teamId"":\s*(\d)", RegexOptions.Compiled);
    private static readonly Regex Dano = new(@"""affectedIds"":\s*\[\s*(\d+)\s*\],\s*""type"":\s*\[\s*""AnnotationType_DamageDealt""\s*\].{0,160}?""valueInt32"":\s*\[\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex Ganador = new(@"""scope"":\s*""MatchScope_Match"",\s*""result"":\s*""[A-Za-z_]+"",\s*""winningTeamId"":\s*(\d),\s*""reason"":\s*""ResultReason_(\w+)""", RegexOptions.Compiled);
    private static readonly Regex IdPartida = new(@"""matchId"":\s*""([0-9a-f-]{36})""", RegexOptions.Compiled);
    // El aviso de «está mirando»: de qué asiento y qué objeto.
    private static readonly Regex BajoRaton = new(@"""seatIds"":\s*\[\s*(\d)\s*\],\s*""onHover"":\s*\{\s*""objectId"":\s*(\d+)", RegexOptions.Compiled);
    private const string PrefijoPrecon = "?=?Loc/";

    /// <summary>Instancia en la mesa → la carta que es, de quién, en qué zona y, si está transformada, su otra cara.</summary>
    private static readonly Dictionary<int, (int Grp, bool EsCarta, bool EsTierra, int? Zona, int? Dueno, string Colores, int? Otra)> objetos = new();

    /// <summary>«CardColor_White», «CardColor_Blue»… → «W», «U»…</summary>
    private static string Letras(string colores)
    {
        var sb = new System.Text.StringBuilder(5);
        foreach (var (palabra, letra) in new[] { ("White", 'W'), ("Blue", 'U'), ("Black", 'B'), ("Red", 'R'), ("Green", 'G') })
            if (colores.Contains("CardColor_" + palabra, StringComparison.Ordinal)) sb.Append(letra);
        return sb.ToString();
    }
    /// <summary>Zona → su tipo («Battlefield», «Stack», «Hand»…). Los números cambian en cada partida.</summary>
    private static readonly Dictionary<int, string> zonas = new();
    /// <summary>Tu asiento en la partida en curso (1 o 2), o 0 si aún no se sabe.</summary>
    private static int miAsiento;
    /// <summary>La cronología de la partida en curso y lo que hace falta para cerrarla.</summary>
    private static readonly List<Turno> turnos = new();
    private static readonly Dictionary<int, int> equipoPorAsiento = new();
    private static string idPartida = "";
    private static string? formato, evento, edicion;
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
            turnos.Clear();
            equipoPorAsiento.Clear();
            idPartida = "";
            Cambiar(null, null, null, null, null, false, "", []);
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
                // Y con qué formato y en qué cola: es lo que decide que las
                // parecidas y los combos sean de lo que se está jugando.
                var f = FormatoMazo.Match(linea);
                if (f.Success) formato = f.Groups[1].Value.ToLowerInvariant();
                var e = EventoCola.Match(linea);
                if (e.Success)
                {
                    evento = e.Groups[1].Value;
                    // «QuickDraft_EOE_20260901», «PremierDraft_EOE_…», «Sealed_EOE_…»: la
                    // edición va entre los dos primeros guiones bajos.
                    var partes = evento.Split('_');
                    edicion = partes.Length >= 2 && evento.Contains("Draft", StringComparison.OrdinalIgnoreCase) || partes.Length >= 2 && evento.Contains("Sealed", StringComparison.OrdinalIgnoreCase)
                        ? partes[1].ToLowerInvariant()
                        : null;
                }
            }
            // Tu asiento: lo dice cada mensaje del servidor hacia ti.
            var asiento = Asiento.Match(linea);
            if (asiento.Success) miAsiento = int.Parse(asiento.Groups[1].Value);

            // LA CRONOLOGÍA: una partida nueva vacía la anterior; cada turno nuevo
            // abre una entrada; las vidas, el equipo de cada asiento y el daño a
            // los jugadores se van apuntando en el turno en curso.
            var idm = IdPartida.Match(linea);
            if (idm.Success && idm.Groups[1].Value != idPartida)
            {
                idPartida = idm.Groups[1].Value;
                turnos.Clear();
                equipoPorAsiento.Clear();
            }
            foreach (Match eq in Equipo.Matches(linea)) equipoPorAsiento[int.Parse(eq.Groups[1].Value)] = int.Parse(eq.Groups[2].Value);
            var ti = TurnoInfo.Match(linea);
            if (ti.Success)
            {
                var n = int.Parse(ti.Groups[1].Value);
                if (turnos.Count == 0 || turnos[^1].N != n)
                {
                    var activo = miAsiento != 0 && int.Parse(ti.Groups[2].Value) == miAsiento ? "yo" : "rival";
                    turnos.Add(new Turno { N = n, Activo = activo });
                }
            }
            if (turnos.Count > 0)
            {
                var actual = turnos[^1];
                foreach (Match v in Vida.Matches(linea))
                {
                    var vida = int.Parse(v.Groups[1].Value);
                    if (int.Parse(v.Groups[2].Value) == miAsiento) actual.VidaYo = vida; else actual.VidaRival = vida;
                }
                foreach (Match d in Dano.Matches(linea))
                {
                    var a = int.Parse(d.Groups[1].Value);
                    if (a > 2) continue;   // a una criatura, no a un jugador
                    var puntos = int.Parse(d.Groups[2].Value);
                    if (a == miAsiento) actual.DanoAYo += puntos; else actual.DanoARival += puntos;
                }
            }

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
                    var coloresObjeto = m.Groups[7].Success ? Letras(m.Groups[7].Value) : "";
                    int? otra = m.Groups[8].Success ? int.Parse(m.Groups[8].Value) : null;
                    // Un objeto que ya se conocía y vuelve sin zona (un cambio parcial)
                    // conserva la que tenía.
                    if (zona is null && objetos.TryGetValue(id, out var previo)) zona = previo.Zona;
                    objetos[id] = (int.Parse(m.Groups[2].Value), esCarta, esTierra, zona, dueno, coloresObjeto, otra);
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
                    var idObj = int.Parse(t.Groups[1].Value);
                    var destino = int.Parse(t.Groups[2].Value);
                    if (!objetos.TryGetValue(idObj, out var obj)) continue;
                    // La zona nueva se apunta SIEMPRE: la mesa del rival se calcula
                    // de aquí, y una carta que va al cementerio tiene que salir.
                    objetos[idObj] = obj with { Zona = destino };
                    // Y a la cronología: lo que cada bando pone en juego (tierras
                    // incluidas: para el resumen sí cuentan), una vez por instancia.
                    if (obj.EsCarta && obj.Dueno is { } dueno && turnos.Count > 0 && miAsiento != 0
                        && zonas.TryGetValue(destino, out var tz) && (tz == "Battlefield" || tz == "Stack")
                        && turnos[^1].Vistas.Add(idObj))
                    {
                        (dueno == miAsiento ? turnos[^1].JugadasYo : turnos[^1].JugadasRival).Add(obj.Grp);
                    }
                    if (!obj.EsCarta || obj.EsTierra || miAsiento == 0 || obj.Dueno != miAsiento) continue;
                    if (!zonas.TryGetValue(destino, out var tipo)) continue;
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
                // El resultado, y la partida entera lista para el resumen.
                var g = Ganador.Match(linea);
                bool? gane = null; string? razon = null;
                if (g.Success)
                {
                    razon = g.Groups[2].Value;
                    if (miAsiento != 0 && equipoPorAsiento.TryGetValue(miAsiento, out var miEquipo)) gane = int.Parse(g.Groups[1].Value) == miEquipo;
                }
                if (turnos.Count > 0)
                {
                    UltimaPartida = new Partida(formato, mazo, gane, razon, turnos.ToArray());
                    try { PartidaAcabada?.Invoke(); } catch { /* cosa de quien escucha */ }
                }
                turnos.Clear();
                equipoPorAsiento.Clear();
                idPartida = "";
                enPartida = false;
                carta = null;
                otraCara = null;
                rival = null;
                rivalOtra = null;
                objetos.Clear();
                zonas.Clear();
                miAsiento = 0;
            }
        }
        // Lo que se deriva de la tabla entera: tus colores y la mesa del rival.
        var colores = "";
        var mesa = new SortedSet<int>();
        if (miAsiento != 0)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var o in objetos.Values)
            {
                if (o.Dueno == miAsiento)
                {
                    foreach (var c in o.Colores) if (!sb.ToString().Contains(c)) sb.Append(c);
                }
                else if (o.EsCarta && o.Dueno == 3 - miAsiento && o.Zona is { } z && zonas.TryGetValue(z, out var tz) && tz == "Battlefield") mesa.Add(o.Grp);
            }
            colores = sb.ToString();
        }
        Cambiar(mazo, carta, otraCara, rival, rivalOtra, enPartida, colores, [.. mesa]);
    }

    private static void Cambiar(string? mazo, int? carta, int? otraCara, int? rival, int? rivalOtra, bool enPartida, string colores, int[] mesa)
    {
        if (mazo == Mazo && carta == Carta && otraCara == CartaOtraCara
            && rival == RivalMira && rivalOtra == RivalMiraOtraCara && enPartida == EnPartida
            && colores == MisColores && mesa.SequenceEqual(MesaRival)
            && formato == Formato && evento == Evento && edicion == Edicion) return;
        Mazo = mazo; Carta = carta; CartaOtraCara = otraCara; RivalMira = rival; RivalMiraOtraCara = rivalOtra; EnPartida = enPartida;
        MisColores = colores; MesaRival = mesa; Formato = formato; Evento = evento; Edicion = edicion;
        try { Cambio?.Invoke(); } catch { /* lo que haga quien escucha es cosa suya */ }
    }

    /// <summary>Quita los escapes de una cadena JSON que venía dentro de otra (\" → ", \ → \).</summary>
    private static string Desescapar(string s) => s.Replace("\\\"", "\"").Replace("\\\\", "\\");
}
