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
    public static int? CartaRival { get; private set; }

    /// <summary>La otra cara de la que mira el rival, si está transformada.</summary>
    public static int? CartaRivalOtraCara { get; private set; }

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

    /// <summary>Todo lo que el rival ha enseñado en la partida (mesa, cementerio, pila, exilio): grpIds. Para sus sinergias.</summary>
    public static int[] VistasRival { get; private set; } = [];

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

    /// <summary>
    /// LAS CARTAS DE ESE MAZO: su <c>grpId</c> y cuántas, del mazo principal y
    /// de la zona de mando. Vienen en la misma línea que el nombre (el
    /// `Deck` del request de EventSetDeckV3 y DeckUpsertDeckV3), así que no hay
    /// que pedírselas a nadie. Las pinta el desplegable «Mejorar» de la columna
    /// (pedido del usuario el 2026-10-03).
    /// </summary>
    public static (int Grp, int N)[] CartasMazo { get; private set; } = [];

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
        /// <summary>Lo que tenías en la mano al empezar el turno: el resumen sólo puede aconsejar con esto.</summary>
        public readonly List<int> ManoYo = new();
    }

    /// <summary>Una mano ofrecida al empezar (la de 7 y una por cada mulligan) y si se aceptó.</summary>
    public sealed class ManoOfrecida
    {
        public int[] Cartas = [];
        public bool? Aceptada;
    }

    /// <summary>Una partida terminada, tal como se manda a la web para el resumen.</summary>
    public sealed record Partida(string? Formato, string? Mazo, bool? Gane, string? Razon, IReadOnlyList<Turno> Turnos, IReadOnlyList<ManoOfrecida> Manos);

    /// <summary>La última partida terminada, con su cronología, o null.</summary>
    public static Partida? UltimaPartida { get; private set; }

    /// <summary>Acaba de terminar una partida: UltimaPartida está puesta. Salta en el hilo del vigía.</summary>
    public static event Action? PartidaAcabada;

    /// <summary>Algo de lo de arriba ha cambiado. Salta en el hilo del vigía.</summary>
    public static event Action? Cambio;

    /// <summary>
    /// TU BIBLIOTECA EN PARTIDA, para el rastreador (pedido del usuario el
    /// 2026-10-03): cuántas cartas quedan en ella y, por carta, cuántas tuyas
    /// se han visto FUERA —mano, mesa, cementerio, exilio, pila, mando—. Lo
    /// que queda de cada una es lo del mazo menos eso.
    ///
    /// Sale de las ZONAS, no de la última zona de cada instancia: Arena le da
    /// a una carta un número nuevo cada vez que cambia de zona y el viejo se
    /// queda en el limbo, así que contar instancias por su última zona contaba
    /// dos veces lo que se jugaba desde la mano. Cada zona llega entera con la
    /// lista de lo que tiene, y eso sí es el estado de verdad.
    /// </summary>
    public static int BibliotecaTam { get; private set; }
    public static IReadOnlyDictionary<int, int> FueraDeBiblioteca { get; private set; } = new Dictionary<int, int>();
    /// <summary>La biblioteca ha cambiado (has robado, jugado, buscado…). Salta en el hilo del vigía.</summary>
    public static event Action? BibliotecaCambio;

    /// <summary>
    /// ARENA TE OFRECE UNA MANO (la inicial o la de un mulligan): sus cartas
    /// con repeticiones, cuántas son tierra y cuántos mulligans llevas. Para el
    /// consejo de mulligan. Salta en el hilo del vigía, una vez por mano.
    /// </summary>
    public static event Action<int[], int, int>? ManoNueva;

    /// <summary>Una carta en la mesa: cuál es, si está girada y si es tierra.</summary>
    public sealed record EnMesa(int Grp, bool Girada, bool Tierra);

    /// <summary>
    /// LA MESA AL EMPEZAR TU PRIMERA FASE PRINCIPAL, para el consejo de cada
    /// turno (experimental, pedido del usuario el 2026-10-03): tu mano con
    /// repeticiones, lo tuyo y lo del rival en el campo (girado o no), las
    /// vidas y el turno. Sólo lo que se ve en la pantalla: el registro no trae
    /// la mano del rival, y aunque la trajera no se usaría.
    /// </summary>
    public sealed record Mesa(int Turno, int[] Mano, EnMesa[] Mia, EnMesa[] DelRival, int? VidaYo, int? VidaRival);

    /// <summary>Empieza tu primera fase principal (una vez por turno). Salta en el hilo del vigía.</summary>
    public static event Action<Mesa>? MiFasePrincipal;

    /// <summary>
    /// Empieza el turno DEL RIVAL (su mantenimiento), una vez por turno, con la
    /// mesa: para los instantáneos y las cartas con destello que se pueden
    /// jugar en su turno (el usuario, 2026-10-03). Salta en el hilo del vigía.
    /// </summary>
    public static event Action<Mesa>? TurnoDelRival;
    /// <summary>Arena ha vuelto a empezar el registro (lo rota al arrancar): sesión nueva, marcador a cero.</summary>
    public static event Action? RegistroReiniciado;

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
    // La zona entera: su dueño (las de cada jugador) y lo que tiene ahora. Sin
    // «objectInstanceIds» es que está vacía.
    // La fase y de quién es el turno: «"phase": "Phase_Main1", "turnNumber": 18, "activePlayer": 1».
    private static readonly Regex Fase = new(@"""phase"":\s*""Phase_(\w+)"",\s*""turnNumber"":\s*(\d+),\s*""activePlayer"":\s*(\d)", RegexOptions.Compiled);
    // El mantenimiento de un turno: «"phase": "Phase_Beginning", "step": "Step_Upkeep", "turnNumber": 8, "activePlayer": 2».
    private static readonly Regex Mantenimiento = new(@"""phase"":\s*""Phase_Beginning"",\s*""step"":\s*""Step_Upkeep"",\s*""turnNumber"":\s*(\d+),\s*""activePlayer"":\s*(\d)", RegexOptions.Compiled);
    // Una carta con lo que la describe, hasta la siguiente o hasta lo que viene
    // detrás de la lista de cartas (anotaciones, zonas, turno…): dentro,
    // «"isTapped": true» si está girada. Sin esos finales, la ÚLTIMA carta de
    // la lista no encontraba dónde acabar y se quedaba girada para siempre.
    private static readonly Regex CartaGirada = new(@"""instanceId"":\s*(\d+),\s*""grpId"":\s*\d+,\s*""type"":\s*""GameObjectType_Card""(.{0,3000}?)(?=""instanceId""|""annotations""|""persistentAnnotations""|""diffDeleted|""turnInfo""|""zones""|""players""|""timers""|""actions""|""gameInfo""|$)", RegexOptions.Compiled);
    private static readonly Regex ZonaCompleta = new(@"""zoneId"":\s*(\d+),\s*""type"":\s*""ZoneType_\w+"",\s*""visibility"":\s*""\w+""(?:,\s*""ownerSeatId"":\s*(\d+))?(?:,\s*""objectInstanceIds"":\s*\[([\d,\s]*)\])?", RegexOptions.Compiled);
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
    // Cada carta del mazo, en el mismo JSON escapado: {\"cardId\":58437,\"quantity\":19}.
    private static readonly Regex CartaDelMazo = new(@"\\\x22cardId\\\x22:(\d+),\\\x22quantity\\\x22:(\d+)", RegexOptions.Compiled);
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
    /// <summary>Zona → lo que tiene ahora (instancias) y, si es de un jugador, de quién.</summary>
    private static readonly Dictionary<int, int[]> idsZona = new();
    private static readonly Dictionary<int, int> duenoZona = new();
    private static string firmaBiblioteca = "", firmaMano = "";
    /// <summary>Las vidas de ahora, de cualquier mensaje: las del turno sólo se apuntan cuando cambian en ese turno.</summary>
    private static int? vidaYoAhora, vidaRivalAhora;
    /// <summary>Instancia → si está girada (la última vez que el juego la describió).</summary>
    private static readonly Dictionary<int, bool> girada = new();
    /// <summary>El turno de tu última fase principal avisada, y el que está por avisar al acabar la tanda.</summary>
    private static int turnoAvisado, turnoPorAvisar;
    /// <summary>La mesa tal como estaba en la línea en que empezó la fase: al acabar la tanda ya puede haber tierras giradas.</summary>
    private static Mesa? mesaPorAvisar;
    /// <summary>Lo mismo para el principio del turno del rival.</summary>
    private static int turnoRivalAvisado;
    private static Mesa? mesaRivalPorAvisar;
    /// <summary>Los mulligans de la mano en curso: suma con cada «mulligan» tuyo y vuelve a cero al quedártela.</summary>
    private static int mulligansHechos;
    /// <summary>
    /// En la primera lectura (la cola del registro al arrancar) no se avisa de
    /// manos: serían las de partidas ya jugadas, y el consejo saldría tarde y
    /// sobre una mano que ya no está.
    /// </summary>
    private static bool leyendoLoViejo;
    /// <summary>Tu asiento en la partida en curso (1 o 2), o 0 si aún no se sabe.</summary>
    private static int miAsiento;
    /// <summary>La cronología de la partida en curso y lo que hace falta para cerrarla.</summary>
    private static readonly List<Turno> turnos = new();
    /// <summary>
    /// EL ÚLTIMO TURNO DE LA PARTIDA QUE ACABA DE TERMINAR: las vidas finales (el
    /// −25 del golpe de gracia) llegan unas líneas DESPUÉS de MatchCompleted
    /// (visto el 2026-09-27: la curva se quedaba en 10 y el usuario había perdido),
    /// así que se siguen apuntando aquí hasta que empieza otra partida.
    /// </summary>
    private static Turno? turnoFinal;
    /// <summary>La partida ha acabado y aún no ha empezado otra: los mensajes de estado que siguen (traen turnInfo) no abren turnos nuevos.</summary>
    private static bool acabada;
    /// <summary>Lo que quedó de la última lectura sin su salto de línea: Arena aún lo estaba escribiendo, y se completa con la siguiente.</summary>
    private static string resto = "";
    private static readonly List<ManoOfrecida> manos = new();
    private static readonly Dictionary<int, int> equipoPorAsiento = new();
    private static string idPartida = "";
    private static string? formato, evento, edicion;
    private static (int Grp, int N)[] cartasMazo = [];
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
    /// <summary>
    /// VICTORIAS Y DERROTAS DE TODA LA SESIÓN DE ARENA, leyendo el registro entero
    /// una vez (Arena lo empieza de cero al arrancar, así que «el fichero» es «la
    /// sesión»). El seguimiento normal empieza por la cola (COLA_AL_EMPEZAR) y el
    /// marcador salía a medias: el 2026-09-27 el usuario llevaba 2-12 y veía 2-0.
    /// Misma lógica de asiento y equipo que el seguimiento; sin ningún efecto.
    /// </summary>
    public static (int Victorias, int Derrotas) MarcadorDelRegistro(string rutaLog)
    {
        int v = 0, d = 0, asiento = 0; var equipos = new Dictionary<int, int>(); var id = "";
        try
        {
            using var fs = new FileStream(rutaLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs, System.Text.Encoding.UTF8);
            while (sr.ReadLine() is { } linea)
            {
                var idm = IdPartida.Match(linea);
                if (idm.Success && idm.Groups[1].Value != id) { id = idm.Groups[1].Value; equipos.Clear(); }
                var a = Asiento.Match(linea);
                if (a.Success) asiento = int.Parse(a.Groups[1].Value);
                foreach (Match eq in Equipo.Matches(linea)) equipos[int.Parse(eq.Groups[1].Value)] = int.Parse(eq.Groups[2].Value);
                if (!linea.Contains("MatchGameRoomStateType_MatchCompleted", StringComparison.Ordinal)) continue;
                // El mensaje se repite varias veces por partida; sólo la primera trae los equipos aún puestos.
                var g = Ganador.Match(linea);
                if (g.Success && asiento != 0 && equipos.TryGetValue(asiento, out var mio))
                {
                    if (int.Parse(g.Groups[1].Value) == mio) v++; else d++;
                }
                equipos.Clear(); id = "";
            }
        }
        catch { /* sin registro: 0-0 */ }
        return (v, d);
    }

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
            idsZona.Clear();
            duenoZona.Clear();
            miAsiento = 0;
            turnos.Clear();
            manos.Clear();
            equipoPorAsiento.Clear();
            turnoFinal = null;
            acabada = false;
            resto = "";
            idPartida = "";
            try { RegistroReiniciado?.Invoke(); } catch { /* cosa de quien escucha */ }
            Cambiar(null, null, null, null, null, false, "", [], []);
        }
        if (estrenando)
        {
            // Sólo la cola: ver COLA_AL_EMPEZAR.
            estrenando = false;
            leyendoLoViejo = true;
            posicion = Math.Max(0, fs.Length - COLA_AL_EMPEZAR);
        }
        if (fs.Length == posicion) return;
        fs.Seek(posicion, SeekOrigin.Begin);
        using var sr = new StreamReader(fs, System.Text.Encoding.UTF8);
        var texto = resto + sr.ReadToEnd();
        posicion = fs.Length;
        // LÍNEAS ENTERAS: la última puede venir a medias (Arena la sigue
        // escribiendo), y un mensaje partido en dos se pierde para todas las
        // expresiones de abajo —asiento, equipos, resultado—. Se guarda y se
        // completa con la lectura siguiente.
        var corte = texto.LastIndexOf('\n');
        if (corte < 0) { resto = texto; return; }
        resto = texto[(corte + 1)..];
        texto = texto[..corte];

        string? mazo = Mazo; int? carta = Carta; int? otraCara = CartaOtraCara;
        int? rival = CartaRival; int? rivalOtra = CartaRivalOtraCara; bool enPartida = EnPartida;
        foreach (var linea in texto.Split('\n'))
        {
            if (linea.Contains("DeckUpsertDeckV3", StringComparison.Ordinal) || linea.Contains("EventSetDeckV3", StringComparison.Ordinal))
            {
                var m = NombreMazo.Match(linea);
                if (m.Success)
                {
                    var nombre = Desescapar(m.Groups[1].Value);
                    if (nombre.Length > 0 && !nombre.StartsWith(PrefijoPrecon, StringComparison.Ordinal))
                    {
                        mazo = nombre;
                        cartasMazo = CartasDe(linea);
                    }
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

            // EL MULLIGAN: cada vez que Arena te ofrece una mano, se apunta; la
            // decisión (quedársela o no) llega en tu respuesta, y va a la última.
            if (linea.Contains("GREMessageType_MulliganReq", StringComparison.Ordinal) && miAsiento != 0)
            {
                var mano = ManoAhora();
                if (mano.Length > 0 && (manos.Count == 0 || manos[^1].Aceptada is not null || !manos[^1].Cartas.SequenceEqual(mano)))
                    manos.Add(new ManoOfrecida { Cartas = mano });
            }
            if (manos.Count > 0 && manos[^1].Aceptada is null)
            {
                if (linea.Contains("MulliganOption_AcceptHand", StringComparison.Ordinal)) manos[^1].Aceptada = true;
                else if (linea.Contains("MulliganOption_Mulligan", StringComparison.Ordinal)) manos[^1].Aceptada = false;
            }
            // LA CRONOLOGÍA: una partida nueva vacía la anterior; cada turno nuevo
            // abre una entrada; las vidas, el equipo de cada asiento y el daño a
            // los jugadores se van apuntando en el turno en curso.
            var idm = IdPartida.Match(linea);
            if (idm.Success && idm.Groups[1].Value != idPartida)
            {
                idPartida = idm.Groups[1].Value;
                turnoFinal = null;
                acabada = false;
                turnos.Clear();
                manos.Clear();
                equipoPorAsiento.Clear();
            }
            foreach (Match eq in Equipo.Matches(linea)) equipoPorAsiento[int.Parse(eq.Groups[1].Value)] = int.Parse(eq.Groups[2].Value);
            var ti = TurnoInfo.Match(linea);
            if (ti.Success && !acabada)
            {
                var n = int.Parse(ti.Groups[1].Value);
                if (turnos.Count == 0 || turnos[^1].N != n)
                {
                    var activo = miAsiento != 0 && int.Parse(ti.Groups[2].Value) == miAsiento ? "yo" : "rival";
                    var turno = new Turno { N = n, Activo = activo };
                    // La mano con la que se empieza el turno.
                    turno.ManoYo.AddRange(ManoAhora());
                    turnos.Add(turno);
                }
            }
            var actual = turnos.Count > 0 ? turnos[^1] : turnoFinal;
            if (actual is not null)
            {
                foreach (Match v in Vida.Matches(linea))
                {
                    var vida = int.Parse(v.Groups[1].Value);
                    if (int.Parse(v.Groups[2].Value) == miAsiento) actual.VidaYo = vidaYoAhora = vida; else actual.VidaRival = vidaRivalAhora = vida;
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
            foreach (Match z in ZonaCompleta.Matches(linea))
            {
                var idz = int.Parse(z.Groups[1].Value);
                if (z.Groups[2].Success) duenoZona[idz] = int.Parse(z.Groups[2].Value);
                idsZona[idz] = z.Groups[3].Success
                    ? z.Groups[3].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToArray()
                    : [];
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
                    if (!obj.EsCarta || obj.EsTierra || miAsiento == 0) continue;
                    if (!zonas.TryGetValue(destino, out var tipo)) continue;
                    if (tipo != "Battlefield" && tipo != "Stack") continue;
                    // La última carta (no tierra) que cada bando pone en juego o en
                    // la pila: la tuya y la del rival, por separado. La del rival
                    // sale en la columna con parecidas EN TUS COLORES (pedido del
                    // usuario el 2026-09-26: «creía que podría ver las del otro
                    // mazo, similares con mis tierras»).
                    if (obj.Dueno == miAsiento) { carta = obj.Grp; otraCara = obj.Otra; }
                    else if (obj.Dueno == 3 - miAsiento) { rival = obj.Grp; rivalOtra = obj.Otra; }
                }
            }
            // Tus respuestas: lo que cuenta los mulligans de la mano que llega.
            if (linea.Contains("MulliganOption_Mulligan", StringComparison.Ordinal)) mulligansHechos++;
            else if (linea.Contains("MulliganOption_AcceptHand", StringComparison.Ordinal)) mulligansHechos = 0;
            if (linea.Contains("GameObjectType_Card", StringComparison.Ordinal))
                foreach (Match cg in CartaGirada.Matches(linea))
                    girada[int.Parse(cg.Groups[1].Value)] = cg.Groups[2].Value.Contains("\"isTapped\": true", StringComparison.Ordinal);
            foreach (Match mt in Mantenimiento.Matches(linea))
            {
                var n = int.Parse(mt.Groups[1].Value);
                if (miAsiento != 0 && int.Parse(mt.Groups[2].Value) != miAsiento && n != turnoRivalAvisado)
                {
                    turnoRivalAvisado = n;
                    mesaRivalPorAvisar = MesaAhora(n);
                }
            }
            foreach (Match f in Fase.Matches(linea))
            {
                var n = int.Parse(f.Groups[2].Value);
                if (miAsiento != 0 && f.Groups[1].Value == "Main1" && int.Parse(f.Groups[3].Value) == miAsiento && n != turnoAvisado && n != turnoPorAvisar)
                {
                    turnoPorAvisar = n;
                    // La foto AHORA, con esta línea ya leída (zonas y cartas van antes).
                    mesaPorAvisar = MesaAhora(n);
                }
            }
            if (linea.Contains("GREMessageType_MulliganReq", StringComparison.Ordinal) && miAsiento != 0)
            {
                var (cartasMano, tierrasMano, firma) = ManoCompleta();
                if (cartasMano.Length > 0 && firma != firmaMano)
                {
                    firmaMano = firma;
                    if (!leyendoLoViejo)
                        try { ManoNueva?.Invoke(cartasMano, tierrasMano, mulligansHechos); } catch { /* cosa de quien escucha */ }
                }
            }
            if (linea.Contains("onHover", StringComparison.Ordinal))
            {
                var m = BajoRaton.Match(linea);
                if (m.Success && objetos.TryGetValue(int.Parse(m.Groups[2].Value), out var obj) && obj.EsCarta)
                {
                    // Arena sólo escribe el ratón del RIVAL, y lo que mira cambia
                    // a cada segundo: ya no se ofrece (la carta del rival es la
                    // última que ha jugado, ver arriba). Si algún día escribiera
                    // el tuyo, iría a tu carta.
                    var quien = int.Parse(m.Groups[1].Value);
                    if (miAsiento != 0 && quien == miAsiento) { carta = obj.Grp; otraCara = obj.Otra; }
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
                    UltimaPartida = new Partida(formato, mazo, gane, razon, turnos.ToArray(), manos.ToArray());
                    try { PartidaAcabada?.Invoke(); } catch { /* cosa de quien escucha */ }
                }
                turnoFinal = turnos.Count > 0 ? turnos[^1] : null;
                acabada = true;
                turnos.Clear();
                manos.Clear();
                equipoPorAsiento.Clear();
                // El id de la partida se conserva: las líneas que siguen al fin
                // (las de las vidas finales) lo repiten, y borrarlo aquí las hacía
                // pasar por «partida nueva» y tiraba turnoFinal antes de tiempo.
                enPartida = false;
                carta = null;
                otraCara = null;
                rival = null;
                rivalOtra = null;
                objetos.Clear();
                zonas.Clear();
                idsZona.Clear();
                duenoZona.Clear();
                girada.Clear();
                turnoAvisado = turnoPorAvisar = turnoRivalAvisado = 0;
                mesaRivalPorAvisar = null;
                vidaYoAhora = vidaRivalAhora = null;
                firmaMano = "";
                miAsiento = 0;
            }
        }
        // Lo que se deriva de la tabla entera: tus colores y la mesa del rival.
        var colores = "";
        var mesa = new SortedSet<int>();
        var vistas = new SortedSet<int>();
        if (miAsiento != 0)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var o in objetos.Values)
            {
                if (o.Dueno == miAsiento)
                {
                    foreach (var c in o.Colores) if (!sb.ToString().Contains(c)) sb.Append(c);
                }
                else if (o.EsCarta && o.Grp > 0 && o.Dueno == 3 - miAsiento && o.Zona is { } z && zonas.TryGetValue(z, out var tz))
                {
                    if (tz == "Battlefield") mesa.Add(o.Grp);
                    if (tz is "Battlefield" or "Graveyard" or "Stack" or "Exile") vistas.Add(o.Grp);
                }
            }
            colores = sb.ToString();
        }
        Cambiar(mazo, carta, otraCara, rival, rivalOtra, enPartida, colores, [.. mesa], [.. vistas]);
        ContarBiblioteca();
        if (turnoPorAvisar != 0 && turnoPorAvisar != turnoAvisado)
        {
            turnoAvisado = turnoPorAvisar;
            turnoPorAvisar = 0;
            var mesaAviso = mesaPorAvisar ?? MesaAhora(turnoAvisado);
            mesaPorAvisar = null;
            if (!leyendoLoViejo && miAsiento != 0)
                try { MiFasePrincipal?.Invoke(mesaAviso); } catch { /* cosa de quien escucha */ }
        }
        if (mesaRivalPorAvisar is { } mesaRival)
        {
            mesaRivalPorAvisar = null;
            if (!leyendoLoViejo && miAsiento != 0)
                try { TurnoDelRival?.Invoke(mesaRival); } catch { /* cosa de quien escucha */ }
        }
        leyendoLoViejo = false;
    }

    /// <summary>Las zonas en las que una carta tuya ha salido de la biblioteca (el limbo no: ahí van los números viejos).</summary>
    private static readonly HashSet<string> FueraDeLaBiblioteca = ["Hand", "Battlefield", "Graveyard", "Exile", "Stack", "Command"];

    /// <summary>Recuenta tu biblioteca y avisa si ha cambiado. Ver BibliotecaTam.</summary>
    private static void ContarBiblioteca()
    {
        var tam = 0;
        var fuera = new Dictionary<int, int>();
        if (miAsiento != 0)
        {
            foreach (var (idz, ids) in idsZona)
            {
                if (!zonas.TryGetValue(idz, out var tipo)) continue;
                if (tipo == "Library")
                {
                    if (duenoZona.TryGetValue(idz, out var d) && d == miAsiento) tam = ids.Length;
                    continue;
                }
                if (!FueraDeLaBiblioteca.Contains(tipo)) continue;
                foreach (var id in ids)
                    if (objetos.TryGetValue(id, out var o) && o.EsCarta && o.Grp > 0 && o.Dueno == miAsiento)
                        fuera[o.Grp] = fuera.GetValueOrDefault(o.Grp) + 1;
            }
        }
        var firma = tam + "|" + string.Join(",", fuera.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}x{kv.Value}"));
        if (firma == firmaBiblioteca) return;
        firmaBiblioteca = firma;
        BibliotecaTam = tam;
        FueraDeBiblioteca = fuera;
        try { BibliotecaCambio?.Invoke(); } catch { /* cosa de quien escucha */ }
    }

    /// <summary>La mesa de ahora, de las zonas: tu mano, el campo de cada uno con lo girado, y las vidas del turno.</summary>
    private static Mesa MesaAhora(int turno)
    {
        var (mano, _, _) = ManoCompleta();
        var mia = new List<EnMesa>();
        var suya = new List<EnMesa>();
        foreach (var (idz, ids) in idsZona)
        {
            if (!zonas.TryGetValue(idz, out var tipo) || tipo != "Battlefield") continue;
            foreach (var id in ids)
            {
                if (!objetos.TryGetValue(id, out var o) || !o.EsCarta || o.Grp <= 0) continue;
                var c = new EnMesa(o.Grp, girada.GetValueOrDefault(id), o.EsTierra);
                if (o.Dueno == miAsiento) mia.Add(c); else suya.Add(c);
            }
        }
        return new Mesa(turno, mano, [.. mia], [.. suya], vidaYoAhora, vidaRivalAhora);
    }

    /// <summary>
    /// Tu mano ahora, CON REPETICIONES (dos Llanuras son dos), cuántas son
    /// tierra y una firma para no avisar dos veces de la misma: de la zona de
    /// la mano, que es la lista de verdad.
    /// </summary>
    private static (int[] Cartas, int Tierras, string Firma) ManoCompleta()
    {
        foreach (var (idz, ids) in idsZona)
        {
            if (!zonas.TryGetValue(idz, out var tipo) || tipo != "Hand") continue;
            if (!duenoZona.TryGetValue(idz, out var d) || d != miAsiento) continue;
            var cartas = new List<int>();
            var tierras = 0;
            foreach (var id in ids)
            {
                if (!objetos.TryGetValue(id, out var o) || o.Grp <= 0) continue;
                cartas.Add(o.Grp);
                if (o.EsTierra) tierras++;
            }
            return (cartas.ToArray(), tierras, string.Join(",", ids));
        }
        return ([], 0, "");
    }

    /// <summary>Tus cartas cuya zona es una mano ahora mismo (las del rival no tienen grpId): distintas, en orden de instancia.</summary>
    private static int[] ManoAhora()
    {
        if (miAsiento == 0) return [];
        var mano = new List<int>();
        foreach (var o in objetos.Values)
            if (o.Dueno == miAsiento && o.Grp > 0 && o.Zona is { } zm && zonas.TryGetValue(zm, out var tzm) && tzm == "Hand" && !mano.Contains(o.Grp))
                mano.Add(o.Grp);
        return mano.ToArray();
    }

    /// <summary>
    /// Las cartas del mazo de una línea: las de `MainDeck` y `CommandZone`
    /// (el comandante también es del mazo), sumadas por carta. El banquillo no:
    /// no es lo que se va a jugar.
    /// </summary>
    internal static (int Grp, int N)[] CartasDe(string linea)
    {
        var cuenta = new Dictionary<int, int>();
        foreach (var zona in new[] { "MainDeck", "CommandZone" })
        {
            var i = linea.IndexOf(zona, StringComparison.Ordinal);
            if (i < 0) continue;
            var fin = linea.IndexOf(']', i);
            if (fin < 0) continue;
            foreach (Match m in CartaDelMazo.Matches(linea, i))
            {
                if (m.Index > fin) break;
                var grp = int.Parse(m.Groups[1].Value);
                cuenta[grp] = cuenta.GetValueOrDefault(grp) + int.Parse(m.Groups[2].Value);
            }
        }
        return cuenta.Select(kv => (kv.Key, kv.Value)).ToArray();
    }

    private static void Cambiar(string? mazo, int? carta, int? otraCara, int? rival, int? rivalOtra, bool enPartida, string colores, int[] mesa, int[] vistas)
    {
        if (mazo == null) cartasMazo = [];
        if (mazo == Mazo && cartasMazo.SequenceEqual(CartasMazo) && carta == Carta && otraCara == CartaOtraCara
            && rival == CartaRival && rivalOtra == CartaRivalOtraCara && enPartida == EnPartida
            && colores == MisColores && mesa.SequenceEqual(MesaRival) && vistas.SequenceEqual(VistasRival)
            && formato == Formato && evento == Evento && edicion == Edicion) return;
        Mazo = mazo; CartasMazo = cartasMazo; Carta = carta; CartaOtraCara = otraCara; CartaRival = rival; CartaRivalOtraCara = rivalOtra; EnPartida = enPartida;
        MisColores = colores; MesaRival = mesa; VistasRival = vistas; Formato = formato; Evento = evento; Edicion = edicion;
        try { Cambio?.Invoke(); } catch { /* lo que haga quien escucha es cosa suya */ }
    }

    /// <summary>Quita los escapes de una cadena JSON que venía dentro de otra (\" → ", \ → \).</summary>
    private static string Desescapar(string s) => s.Replace("\\\"", "\"").Replace("\\\\", "\\");
}
