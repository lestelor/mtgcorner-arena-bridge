using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MtgCornerArenaBridge;

/// <summary>
/// EL DRAFT, EN LA COLUMNA (pedido del usuario el 2026-10-19: «que el profesor
/// también ayude en los draft, como Draftsim»).
///
/// Cada sobre que Arena apunta en su registro (Contexto.SobreDraft) se valora
/// con los números de 17lands de su edición, que la web da por GrpId
/// (/api/mtga-device/draft), y sale en la columna ordenado: la nota de 0 a 5 a
/// la izquierda, lo que gana la carta cuando está en la mano a la derecha, y
/// la que se aconseja con la raya azul. Al pasar el ratón, la carta en grande.
///
/// LA NOTA NO ES «LA MEJOR CARTA DEL SOBRE» (el usuario: «la gracia es que las
/// cartas elegidas combinen bien»). Sale de dos cosas:
///   · lo buena que es la carta en su edición: lo que gana el mazo cuando la
///     tiene en la mano (GIH WR), puesto en una escala del percentil 10 al 95
///     de la edición; con poca muestra, se acerca a la media;
///   · si va con lo que llevas: tus dos colores principales (lo elegido, cada
///     carta pesando lo buena que es) suben las de esos colores y bajan las de
///     fuera, poco al principio y del todo hacia la mitad del draft.
///
/// Y EL ENTRENADOR ELIGE ENTRE LAS MEJORES (fase 2): con las notas ya pintadas,
/// las cuatro primeras van a la web (/api/mtga-device/draft-consejo) con lo que
/// llevas, y la IA escoge una mirando lo que la nota no ve —sinergias con lo
/// elegido, la curva, si te falta quitar criaturas— y lo explica con la voz
/// del entrenador. Su elección pasa a llevar la raya azul, sale una fila
/// «Entrenador: coge X» con el porqué al pasar el ratón, y si no coincide con
/// la de mejor nota (que es cuando aporta algo) también un aviso con su figura.
///
/// En eventos competitivos (Arena Open, clasificatorios) no se enseña nada: la
/// ayuda externa está prohibida.
/// </summary>
internal static class Draft
{
    internal sealed record Respuesta(
        [property: JsonPropertyName("set")] string? Set,
        [property: JsonPropertyName("fuente")] string? Fuente,
        [property: JsonPropertyName("cartas")] Carta[]? Cartas);

    internal sealed record Carta(
        [property: JsonPropertyName("arena")] int Arena,
        [property: JsonPropertyName("nombre")] string? Nombre,
        [property: JsonPropertyName("color")] string? Color,
        [property: JsonPropertyName("rareza")] string? Rareza,
        [property: JsonPropertyName("gih")] double? Gih,
        [property: JsonPropertyName("alsa")] double? Alsa,
        [property: JsonPropertyName("partidas")] int Partidas,
        [property: JsonPropertyName("arte")] string? Arte,
        [property: JsonPropertyName("imagen")] string? Imagen,
        [property: JsonPropertyName("tipos")] string[]? Tipos = null)
    {
        public bool EsCriatura => Tipos?.Any(t => t.Contains("Creature", StringComparison.OrdinalIgnoreCase)) == true;
        public bool EsTierra => Tipos?.Any(t => t.Contains("Land", StringComparison.OrdinalIgnoreCase)) == true;
    }

    /// <summary>Una carta del sobre ya valorada: sin datos de 17lands, sin nota.</summary>
    internal sealed record Valorada(int Grp, Carta? Datos, double? Nota);

    internal sealed record RespuestaConsejo(
        [property: JsonPropertyName("eleccion")] int? Eleccion,
        [property: JsonPropertyName("por_que")] string? PorQue);

    /// <summary>El último consejo del entrenador en el draft (título y texto), para volver a verlo pulsando su fila.</summary>
    internal static (string Titulo, string Texto)? UltimoConsejo { get; private set; }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Dictionary<string, Respuesta> porEdicion = new();

    /// <summary>Los datos de una edición, una vez por sesión del programa (la web los cachea medio día).</summary>
    internal static async Task<Respuesta?> Datos(HttpClient http, string edicion)
    {
        if (edicion.Length == 0) return null;
        lock (porEdicion) if (porEdicion.TryGetValue(edicion, out var ya)) return ya;
        try
        {
            var r = await http.GetAsync($"/api/mtga-device/draft?set={Uri.EscapeDataString(edicion)}");
            if (!r.IsSuccessStatusCode) return null;
            var d = await r.Content.ReadFromJsonAsync<Respuesta>(Json);
            // Sin cartas no se guarda: puede que 17lands aún no tenga la edición y la tenga mañana.
            if (d?.Cartas is { Length: > 0 }) lock (porEdicion) porEdicion[edicion] = d;
            return d;
        }
        catch { return null; }
    }

    /// <summary>
    /// Las cartas del sobre, de la que más conviene a la que menos (ver la
    /// cabecera), y tus colores principales hasta ahora («UB»), si ya los hay.
    /// </summary>
    /// <summary>
    /// LA ESCALA DE LA EDICIÓN: de lo que gana una carta (GIH WR) a una nota de
    /// 0 a 5, del percentil 10 al 95 de las cartas con muestra (200 partidas en
    /// la mano; sin bastantes, todas). Con poca muestra, la carta se acerca a la
    /// media: 40 partidas de una rara no dicen lo mismo que 4.000 de una común.
    /// </summary>
    private static Func<Carta?, double?> Escala(Carta[] todas)
    {
        var muestra = todas.Where(c => c.Gih is not null && c.Partidas >= 200).Select(c => c.Gih!.Value).OrderBy(x => x).ToArray();
        if (muestra.Length < 10) muestra = todas.Where(c => c.Gih is not null).Select(c => c.Gih!.Value).OrderBy(x => x).ToArray();
        double Percentil(double q) => muestra[(int)Math.Clamp(Math.Round(q * (muestra.Length - 1)), 0, muestra.Length - 1)];
        var (bajo, alto) = muestra.Length > 0 ? (Percentil(0.10), Percentil(0.95)) : (0.50, 0.60);
        var media = muestra.Length > 0 ? muestra.Average() : 0.55;
        return c =>
        {
            if (c?.Gih is not double g) return null;
            var ajustado = (g * c.Partidas + media * 200) / (c.Partidas + 200);
            return Math.Clamp((ajustado - bajo) / Math.Max(0.001, alto - bajo), 0, 1) * 5;
        };
    }

    private static IEnumerable<char> Colores(Carta? c) => (c?.Color ?? "").Where(ch => "WUBRG".Contains(ch));

    internal static (List<Valorada> Cartas, string Colores) Valorar(Contexto.SobreDraft s, Respuesta datos)
    {
        var todas = datos.Cartas ?? [];
        var porGrp = new Dictionary<int, Carta>();
        foreach (var c in todas) porGrp.TryAdd(c.Arena, c);
        var Base = Escala(todas);

        // Tus colores: los de lo que llevas, cada carta pesando lo buena que es
        // (una común floja de un color no te mete en él).
        var peso = new Dictionary<char, double>();
        foreach (var g in s.Elegidas)
            if (porGrp.TryGetValue(g, out var c) && Base(c) is double b)
                foreach (var letra in Colores(c))
                    peso[letra] = peso.GetValueOrDefault(letra) + Math.Max(0, b - 1.5);
        var principales = peso.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Take(2).Select(kv => kv.Key).ToHashSet();
        // Cuánto pesan ya: nada en las tres primeras elecciones, del todo a la 18.
        var compromiso = principales.Count < 2 ? 0 : Math.Clamp((s.Elegidas.Length - 3) / 15.0, 0, 1);

        var lista = s.Cartas.Select(g =>
        {
            porGrp.TryGetValue(g, out var c);
            if (Base(c) is not double b) return new Valorada(g, c, null);
            var suyos = Colores(c).ToArray();
            var fuera = suyos.Count(ch => !principales.Contains(ch));
            // Con el draft avanzado, una carta de fuera casi nunca entra en el mazo: con
            // -1,2 por color, Countersculpt (azul) seguía primera en la elección 18 de un
            // negro-verde (probado el 2026-10-19 con el draft de Reality Fracture).
            var ajuste = suyos.Length == 0 ? 0 : fuera == 0 ? 0.6 * compromiso : -2.0 * compromiso * Math.Min(fuera, 2);
            return new Valorada(g, c, Math.Clamp(b + ajuste, 0, 5));
        })
        .OrderByDescending(v => v.Nota ?? -1)
        .ToList();
        return (lista, string.Concat("WUBRG".Where(principales.Contains)));
    }

    /// <summary>
    /// El sobre en la columna, o nada si el draft acabó. <paramref name="bajar"/>
    /// deja en disco la ilustración de cada fila (la de Program).
    /// </summary>
    internal static async Task Mostrar(HttpClient http, Contexto.SobreDraft? s, Func<string?, Task<string?>> bajar, bool deTaller = false,
        Func<string?>? tono = null, Action<string, string>? avisar = null)
    {
        // Sin draft, o en un evento donde la ayuda externa está prohibida: nada.
        if (s is null || Program.EventoCompetitivo(s.Evento)) { Columna.Draft(null, []); UltimoConsejo = null; return; }
        var datos = await Datos(http, s.Edicion);
        var (cartas, colores) = datos is null
            ? (s.Cartas.Select(g => new Valorada(g, null, null)).ToList(), "")
            : Valorar(s, datos);
        var mejor = cartas.FirstOrDefault(c => c.Nota is not null)?.Grp;
        // Las que 17lands no tiene (Alchemy recién salida), con su nombre de la base de Arena.
        var deArena = BaseArena.Nombres(cartas.Where(c => c.Datos?.Nombre is null).Select(c => c.Grp));
        var filas = new List<Columna.FilaDraft>();
        foreach (var v in cartas)
        {
            var arte = await bajar(v.Datos?.Arte);
            filas.Add(new Columna.FilaDraft(
                new Columna.CartaMazo(v.Grp, 0, v.Datos?.Nombre ?? deArena.GetValueOrDefault(v.Grp) ?? Textos.T("draft_sin_datos"), "", 0, false, arte, v.Datos?.Imagen),
                v.Nota is double n ? n.ToString("0.0", CultureInfo.InvariantCulture) : "—",
                v.Datos?.Gih is double gih ? (gih * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "",
                v.Grp == mejor));
        }
        // Si mientras bajaban las ilustraciones llegó otro sobre, éste ya no vale.
        // (En el taller no hay vigía: el sobre es el que se le da.)
        bool Sigue() => deTaller || ReferenceEquals(Contexto.Draft, s);
        if (!Sigue()) return;
        var titulo = Textos.T("col_draft", s.Sobre, s.Eleccion) + (colores.Length > 0 ? " · " + colores : "");
        UltimoConsejo = null;
        Columna.Draft(titulo, filas);

        // EL ENTRENADOR, con las notas ya a la vista: tarda un par de segundos.
        var candidatas = cartas.Where(c => c.Nota is not null).Take(4).Select(c => c.Grp).ToArray();
        if (candidatas.Length < 2) return;
        var consejo = await Consejo(http, s, candidatas, colores, tono?.Invoke());
        if (consejo is not { Eleccion: int elegida, PorQue: { Length: > 0 } porQue } || !Sigue()) return;
        var nombre = cartas.FirstOrDefault(c => c.Grp == elegida)?.Datos?.Nombre;
        if (nombre is null) return;
        filas = filas.Select(f => f with { Mejor = f.Carta.Grp == elegida }).ToList();
        UltimoConsejo = (titulo, $"{Textos.T("draft_coge", nombre)}. {porQue}");
        Columna.Draft(titulo, filas, (Textos.T("draft_entrenador", nombre), porQue));
        // El aviso, sólo cuando el entrenador no se queda con la de mejor nota.
        if (elegida != mejor) avisar?.Invoke(titulo, UltimoConsejo.Value.Texto);
    }

    // ── EL MAZO SUGERIDO, AL ACABAR EL DRAFT (fase 3) ──────────────────────

    /// <summary>
    /// UN MAZO DE 40 CON LO ELEGIDO: 23 hechizos y 17 tierras básicas.
    ///
    /// Los dos colores son la pareja con la que las 23 mejores cartas jugables
    /// (de esos colores o incoloras) suman más nota. Dentro, las 23 de más nota,
    /// con al menos 14 criaturas si las hay (se cambian los peores hechizos por
    /// las mejores criaturas que quedaron fuera). Las tierras, repartidas según
    /// cuántas cartas de cada color lleva el mazo. Sin datos de 17lands no se
    /// hace: no habría con qué elegir.
    /// </summary>
    internal sealed record MazoSugerido(string Colores, List<Carta> Cartas, List<(char Color, int N)> Tierras);

    internal static MazoSugerido? Construir(int[] pool, Respuesta datos)
    {
        var todas = datos.Cartas ?? [];
        var porGrp = new Dictionary<int, Carta>();
        foreach (var c in todas) porGrp.TryAdd(c.Arena, c);
        var Base = Escala(todas);
        var jugables = pool.Where(porGrp.ContainsKey).Select(g => porGrp[g]).Where(c => !c.EsTierra).ToList();
        if (jugables.Count < 15) return null;
        double Nota(Carta c) => Base(c) ?? 1.0;

        List<Carta>? mejores = null; var mejorSuma = double.MinValue; var pareja = "";
        const string Wubrg = "WUBRG";
        for (var i = 0; i < 5; i++)
            for (var j = i + 1; j < 5; j++)
            {
                var par = $"{Wubrg[i]}{Wubrg[j]}";
                var dentro = jugables.Where(c => Colores(c).All(par.Contains)).OrderByDescending(Nota).Take(23).ToList();
                var suma = dentro.Sum(Nota) - Math.Max(0, 23 - dentro.Count) * 3;   // que falten cartas pesa
                if (suma > mejorSuma) { mejorSuma = suma; mejores = dentro; pareja = par; }
            }
        if (mejores is null) return null;
        // Al menos 14 criaturas, si las hay.
        var fuera = jugables.Where(c => Colores(c).All(pareja.Contains) && !mejores.Contains(c)).OrderByDescending(Nota).ToList();
        while (mejores.Count(c => c.EsCriatura) < 14)
        {
            var entra = fuera.FirstOrDefault(c => c.EsCriatura);
            var sale = mejores.Where(c => !c.EsCriatura).OrderBy(Nota).FirstOrDefault();
            if (entra is null || sale is null) break;
            mejores.Remove(sale); mejores.Add(entra); fuera.Remove(entra);
        }
        // Las tierras: 17, por cuántas cartas de cada color (una bicolor cuenta en los dos).
        var porColor = pareja.ToDictionary(ch => ch, ch => mejores.Count(c => Colores(c).Contains(ch)));
        var total = Math.Max(1, porColor.Values.Sum());
        var primera = (int)Math.Round(17.0 * porColor[pareja[0]] / total);
        primera = Math.Clamp(primera, porColor[pareja[0]] > 0 ? 5 : 0, porColor[pareja[1]] > 0 ? 12 : 17);
        var tierras = new List<(char, int)> { (pareja[0], primera), (pareja[1], 17 - primera) }.Where(t => t.Item2 > 0).ToList();
        return new MazoSugerido(pareja, mejores.OrderBy(c => !c.EsCriatura).ThenByDescending(Nota).ToList(), tierras);
    }

    /// <summary>El último mazo sugerido, para volver a abrirlo desde la columna.</summary>
    internal static (string Titulo, IReadOnlyList<PanelCartas.Seccion> Secciones, string? Plan)? UltimoMazo { get; private set; }

    internal sealed record RespuestaPlan([property: JsonPropertyName("plan")] string? Plan);

    /// <summary>
    /// Al terminar un draft: el mazo sugerido en la ventana de cartas (criaturas y
    /// otros hechizos, con las tierras en el título), la fila de la columna para
    /// volver a verlo, y el plan de juego del entrenador en su aviso.
    /// </summary>
    internal static async Task MostrarMazo(HttpClient http, Contexto.PoolDraft pool, Func<string?, Task<string?>> bajar,
        Func<string?>? tono = null, Action<string, string>? avisar = null)
    {
        if (Program.EventoCompetitivo(pool.Evento)) return;
        var datos = await Datos(http, pool.Edicion);
        if (datos is null || Construir(pool.Cartas, datos) is not { } mazo) return;
        var titulo = Textos.T("draft_mazo_titulo", mazo.Colores,
            string.Join(" · ", mazo.Tierras.Select(t => $"{t.N} {Textos.T("tierra_" + char.ToLowerInvariant(t.Color))}")));
        async Task<List<PanelCartas.Carta>> Bajar(IEnumerable<Carta> cartas)
        {
            var lista = new List<PanelCartas.Carta>();
            foreach (var c in cartas)
                if (await bajar(c.Imagen) is { } fichero) lista.Add(new PanelCartas.Carta(c.Nombre ?? "", fichero, null, null));
            return lista;
        }
        var criaturas = mazo.Cartas.Where(c => c.EsCriatura).ToList();
        var otras = mazo.Cartas.Where(c => !c.EsCriatura).ToList();
        var secciones = new List<PanelCartas.Seccion>
        {
            new(Textos.T("draft_mazo_criaturas", criaturas.Count), null, await Bajar(criaturas)),
            new(Textos.T("draft_mazo_hechizos", otras.Count), null, await Bajar(otras)),
        };
        UltimoMazo = (titulo, secciones, null);
        Columna.MazoDraft(Textos.T("draft_mazo_fila", mazo.Colores));
        PanelCartas.Mostrar(titulo, secciones);

        // El plan del entrenador: cómo se juega este mazo, en su voz.
        if (await Plan(http, pool, mazo, tono?.Invoke()) is { Length: > 0 } plan)
        {
            UltimoMazo = (titulo, secciones, plan);
            avisar?.Invoke(Textos.T("draft_mazo_plan"), plan);
        }
    }

    /// <summary>El plan de juego del entrenador para el mazo sugerido, o null si la web no contesta.</summary>
    internal static async Task<string?> Plan(HttpClient http, Contexto.PoolDraft pool, MazoSugerido mazo, string? tono)
    {
        try
        {
            using var r = await http.PostAsJsonAsync("/api/mtga-device/draft-mazo", new
            {
                set = pool.Edicion, mazo = mazo.Cartas.Select(c => c.Arena), colores = mazo.Colores,
                banquillo = pool.Cartas.Where(g => !mazo.Cartas.Any(c => c.Arena == g)).Distinct().Take(30),
                idioma = Textos.Idioma, tono,
            }, Json);
            return r.IsSuccessStatusCode ? (await r.Content.ReadFromJsonAsync<RespuestaPlan>(Json))?.Plan : null;
        }
        catch { return null; /* sin plan: el mazo sigue a la vista */ }
    }

    /// <summary>Vuelve a abrir el último mazo sugerido (la fila de la columna), con su plan.</summary>
    internal static void AbrirMazo(Action<string, string>? avisar)
    {
        if (UltimoMazo is not { } m) return;
        PanelCartas.Mostrar(m.Titulo, m.Secciones);
        if (m.Plan is { Length: > 0 } plan) avisar?.Invoke(Textos.T("draft_mazo_plan"), plan);
    }

    /// <summary>Lo que dice el entrenador de este sobre, o null si la web no contesta.</summary>
    internal static async Task<RespuestaConsejo?> Consejo(HttpClient http, Contexto.SobreDraft s, int[] candidatas, string colores, string? tono)
    {
        try
        {
            var r = await http.PostAsJsonAsync("/api/mtga-device/draft-consejo", new
            {
                set = s.Edicion, sobre = s.Sobre, eleccion = s.Eleccion, candidatas, elegidas = s.Elegidas, colores,
                idioma = Textos.Idioma, tono,
            }, Json);
            return r.IsSuccessStatusCode ? await r.Content.ReadFromJsonAsync<RespuestaConsejo>(Json) : null;
        }
        catch { return null; }
    }
}
