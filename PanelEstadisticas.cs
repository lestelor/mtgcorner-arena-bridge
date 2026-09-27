using System.Runtime.InteropServices;

namespace MtgCornerArenaBridge;

/// <summary>
/// «STATS»: EL OVERVIEW DE LA WEB, ENCIMA DEL JUEGO.
///
/// Pedido del usuario el 2026-09-27 («que salga lo de overview de la web, curva
/// de maná, estadísticas, gráfica de victorias… en la pantalla», «con un
/// diseño elegante»), y rehecho ese mismo día tras verlo:
///
///   · arriba, cuatro tarjetas: total con su anillo, hoy, racha y rango con su
///     insignia de verdad (las de la web, en PNG: GDI+ no lee WebP);
///   · debajo, EL SELECTOR DE MAZOS: «Todos» y una ficha por mazo con su color.
///     Elegir uno no lleva a la web («simplemente debería remarcar en las
///     gráficas dónde los he utilizado»): filtra las tarjetas, apaga en la
///     escalera las partidas de los demás mazos, rehace la evolución con las
///     suyas y enseña su curva;
///   · la escalera de rango A TODO EL ANCHO, cada tramo del color de su mazo y
///     con la insignia donde se sube de nivel, como la de la web;
///   · abajo, a medias y a la misma altura, la evolución y la curva de maná
///     repartida por tipo de carta, con su caja al pasar como en la web.
///
/// Los datos vienen de /api/mtga-device/estadisticas; lo que depende del mazo
/// elegido (tarjetas, evolución) se cuenta aquí a partir de las partidas, que
/// así cambiar de mazo no pide nada a la red.
/// </summary>
internal static partial class PanelCartas
{
    // ── Los datos, ya preparados por Program ──────────────────────────────

    public sealed record PartidaEst(DateTime Cuando, bool Gane, string? Mazo);
    public sealed record PeldanoEst(int Antes, int Pos, string Clase, int Nivel, bool Gane, DateTime Cuando, string? Mazo, bool Subio);
    public sealed record SegmentoCurva(string Tipo, int N, string[] Cartas);
    public sealed record ColumnaCurva(int Cmc, int Total, SegmentoCurva[] Tipos);
    public sealed record AnalisisMazo(int Tierras, double Medio, double ManoBuena, double TierraT3, ColumnaCurva[] Curva);
    public sealed record MazoEst(string Nombre, int Ganadas, int Perdidas, bool Actual, AnalisisMazo? Analisis);

    public sealed record Estadisticas(
        int Ganadas, int Perdidas, PartidaEst[] Partidas, MazoEst[] Mazos,
        string? Clase, int? Nivel, PeldanoEst[] Escalera,
        IReadOnlyDictionary<string, string> Iconos, string? MazoArena);

    private static Estadisticas? est;
    /// <summary>El mazo elegido en el selector: -1 es «Todos».</summary>
    private static int mazoEst = -1;

    // Dónde está cada cosa en la ventana, para el ratón (se rehacen al pintar).
    private static RECT[] rectFichasEst = [];
    private static RECT[] rectPuntosEst = [];
    private static (int X, int Y)[] centroPuntosEst = [];
    private static PuntoEvolucion[] puntosEst = [];
    private static RECT[] rectPeldanosEst = [];
    private static int desdePeldano;
    private static (RECT R, string Texto)[] rectSegmentosEst = [];

    private const int BAJO_FICHA_EST = -1000, BAJO_PUNTO_EST = -900, BAJO_PELDANO_EST = -2000, BAJO_SEGMENTO_EST = -3000;

    private const int ALTO_TARJETAS = 112, ALTO_SELECTOR = 46, ALTO_ESCALERA = 290, ALTO_INFERIOR = 340;
    private const int RADIO = 10;

    private sealed record PuntoEvolucion(string Corta, string Larga, int Valor, int Ganadas, int Perdidas);

    // ── Colores ───────────────────────────────────────────────────────────
    private static readonly uint TARJETA = Argb(255, 16, 23, 40), BORDE = Argb(255, 36, 48, 74);
    private static readonly uint BLANCO = Rgb(241, 245, 249), APAGADO = Rgb(148, 163, 184), TENUE = Rgb(100, 116, 139), CEJA = Rgb(125, 211, 252);
    private static uint Argb(int a, int r, int g, int b) => (uint)((a << 24) | (r << 16) | (g << 8) | b);
    private static uint Argb(int a, (int R, int G, int B) c) => Argb(a, c.R, c.G, c.B);
    private static (int R, int G, int B) ColorPorcentaje(double p) => p >= 0.55 ? (52, 211, 153) : p >= 0.45 ? (251, 191, 36) : (248, 113, 113);
    private static (int R, int G, int B) ColorClase(string? clase) => clase switch
    {
        "Bronze" => (205, 127, 50),
        "Silver" => (192, 199, 212),
        "Gold" => (250, 204, 21),
        "Platinum" => (94, 234, 212),
        "Diamond" => (129, 140, 248),
        "Mythic" => (249, 115, 22),
        _ => (100, 116, 139),
    };
    /// <summary>Los colores de mazo de la escalera de la web (PALETA_MAZOS), en el orden del selector.</summary>
    private static readonly (int R, int G, int B)[] PALETA = [(251, 191, 36), (167, 139, 250), (56, 189, 248), (244, 114, 182), (163, 230, 53), (251, 146, 60)];
    private static readonly (int R, int G, int B) SIN_MAZO = (148, 163, 184);
    private static (int R, int G, int B) ColorMazo(string? nombre)
    {
        if (est is not { } e || nombre is null) return SIN_MAZO;
        var i = Array.FindIndex(e.Mazos, m => m.Nombre == nombre);
        return i < 0 ? SIN_MAZO : PALETA[i % PALETA.Length];
    }
    /// <summary>Los colores por tipo de la curva de la web (ManaCurveCard.TYPE_COLOR).</summary>
    private static (int R, int G, int B) ColorTipo(string tipo) => tipo switch
    {
        "creature" => (52, 211, 153),
        "instant" => (56, 189, 248),
        "sorcery" => (167, 139, 250),
        "artifact" => (148, 163, 184),
        "enchantment" => (251, 191, 36),
        "planeswalker" => (244, 114, 182),
        "battle" => (251, 146, 60),
        "land" => (163, 134, 107),
        _ => (100, 116, 139),
    };

    // ── GDI+ y lo que falta de GDI ────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)] private struct PuntoG { public int X, Y; }
    [DllImport("gdiplus.dll")] private static extern int GdipSetSmoothingMode(IntPtr g, int modo);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateSolidFill(uint argb, out IntPtr brocha);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteBrush(IntPtr brocha);
    [DllImport("gdiplus.dll")] private static extern int GdipCreatePen1(uint argb, float ancho, int unidad, out IntPtr pluma);
    [DllImport("gdiplus.dll")] private static extern int GdipDeletePen(IntPtr pluma);
    [DllImport("gdiplus.dll")] private static extern int GdipSetPenStartCap(IntPtr pluma, int remate);
    [DllImport("gdiplus.dll")] private static extern int GdipSetPenEndCap(IntPtr pluma, int remate);
    [DllImport("gdiplus.dll")] private static extern int GdipSetPenLineJoin(IntPtr pluma, int union);
    [DllImport("gdiplus.dll")] private static extern int GdipSetPenDashStyle(IntPtr pluma, int estilo);
    [DllImport("gdiplus.dll")] private static extern int GdipDrawLinesI(IntPtr g, IntPtr pluma, PuntoG[] puntos, int n);
    [DllImport("gdiplus.dll")] private static extern int GdipDrawLineI(IntPtr g, IntPtr pluma, int x1, int y1, int x2, int y2);
    [DllImport("gdiplus.dll")] private static extern int GdipFillPolygonI(IntPtr g, IntPtr brocha, PuntoG[] puntos, int n, int relleno);
    [DllImport("gdiplus.dll")] private static extern int GdipFillEllipseI(IntPtr g, IntPtr brocha, int x, int y, int ancho, int alto);
    [DllImport("gdiplus.dll")] private static extern int GdipDrawArcI(IntPtr g, IntPtr pluma, int x, int y, int ancho, int alto, float inicio, float barrido);
    [DllImport("gdiplus.dll")] private static extern int GdipCreatePath(int relleno, out IntPtr camino);
    [DllImport("gdiplus.dll")] private static extern int GdipDeletePath(IntPtr camino);
    [DllImport("gdiplus.dll")] private static extern int GdipAddPathArcI(IntPtr camino, int x, int y, int ancho, int alto, float inicio, float barrido);
    [DllImport("gdiplus.dll")] private static extern int GdipAddPathLineI(IntPtr camino, int x1, int y1, int x2, int y2);
    [DllImport("gdiplus.dll")] private static extern int GdipClosePathFigure(IntPtr camino);
    [DllImport("gdiplus.dll")] private static extern int GdipFillPath(IntPtr g, IntPtr brocha, IntPtr camino);
    [DllImport("gdiplus.dll")] private static extern int GdipDrawPath(IntPtr g, IntPtr pluma, IntPtr camino);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateLineBrushI(ref PuntoG p1, ref PuntoG p2, uint c1, uint c2, int envoltura, out IntPtr brocha);
    [DllImport("gdiplus.dll")] private static extern int GdipSetClipRectI(IntPtr g, int x, int y, int ancho, int alto, int modo);
    [DllImport("gdiplus.dll")] private static extern int GdipResetClip(IntPtr g);
    [DllImport("gdi32.dll")] private static extern int SetTextCharacterExtra(IntPtr hdc, int extra);

    private const int SUAVE = 4 /* SmoothingModeAntiAlias */, PIXEL = 2 /* UnitPixel */, REMATE_REDONDO = 2, UNION_REDONDA = 2, GUIONES = 1;

    private static IntPtr Lienzo(IntPtr hdc)
    {
        IniciarGdiPlus();
        if (GdipCreateFromHDC(hdc, out var g) != 0 || g == IntPtr.Zero) return IntPtr.Zero;
        GdipSetSmoothingMode(g, SUAVE);
        GdipSetInterpolationMode(g, 7);
        return g;
    }

    private static IntPtr Pluma(uint argb, float ancho, bool redonda = true)
    {
        GdipCreatePen1(argb, ancho, PIXEL, out var p);
        if (redonda) { GdipSetPenStartCap(p, REMATE_REDONDO); GdipSetPenEndCap(p, REMATE_REDONDO); GdipSetPenLineJoin(p, UNION_REDONDA); }
        return p;
    }

    /// <summary>Un rectángulo redondeado; con `soloArriba`, redondo arriba y recto abajo (las barras).</summary>
    private static void Rectangulo(IntPtr g, int x, int y, int w, int h, int r, uint relleno, uint? borde = null, bool soloArriba = false, uint? rellenoAbajo = null)
    {
        if (w <= 0 || h <= 0) return;
        r = Math.Max(1, Math.Min(r, Math.Min(w, h) / 2));
        GdipCreatePath(0, out var c);
        var d = r * 2;
        GdipAddPathArcI(c, x, y, d, d, 180, 90);
        GdipAddPathArcI(c, x + w - d, y, d, d, 270, 90);
        if (soloArriba)
        {
            GdipAddPathLineI(c, x + w, y + r, x + w, y + h);
            GdipAddPathLineI(c, x + w, y + h, x, y + h);
        }
        else
        {
            GdipAddPathArcI(c, x + w - d, y + h - d, d, d, 0, 90);
            GdipAddPathArcI(c, x, y + h - d, d, d, 90, 90);
        }
        GdipClosePathFigure(c);
        IntPtr b;
        if (rellenoAbajo is { } abajo)
        {
            var p1 = new PuntoG { X = 0, Y = y - 1 }; var p2 = new PuntoG { X = 0, Y = y + h + 1 };
            GdipCreateLineBrushI(ref p1, ref p2, relleno, abajo, 0, out b);
        }
        else GdipCreateSolidFill(relleno, out b);
        GdipFillPath(g, b, c);
        GdipDeleteBrush(b);
        if (borde is { } bc) { var p = Pluma(bc, 1, false); GdipDrawPath(g, p, c); GdipDeletePen(p); }
        GdipDeletePath(c);
    }

    private static void Circulo(IntPtr g, int cx, int cy, int r, uint argb)
    {
        GdipCreateSolidFill(argb, out var b);
        GdipFillEllipseI(g, b, cx - r, cy - r, r * 2, r * 2);
        GdipDeleteBrush(b);
    }

    /// <summary>Una imagen con su proporción, de alto `h`, centrada en `cx`. Devuelve el ancho con que se pintó.</summary>
    private static int Insignia(IntPtr g, string? fichero, int cx, int y, int h)
    {
        if (fichero is null) return 0;
        var img = Imagen(fichero);
        if (img == IntPtr.Zero) return 0;
        GdipGetImageWidth(img, out var iw); GdipGetImageHeight(img, out var ih);
        var w = ih > 0 ? (int)Math.Round(h * (double)iw / ih) : h;
        GdipDrawImageRectI(g, img, cx - w / 2, y, w, h);
        return w;
    }

    private static void Escribir(IntPtr hdc, string t, int x, int y, int w, int h, int tam, int peso, uint color, uint formato, int extra = 0)
    {
        if (w <= 0 || t.Length == 0) return;
        var f = CreateFont(tam, 0, 0, 0, peso, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        var anterior = SelectObject(hdc, f);
        SetTextColor(hdc, color);
        if (extra != 0) SetTextCharacterExtra(hdc, extra);
        var r = new RECT { Left = x, Top = y, Right = x + w, Bottom = y + h };
        DrawText(hdc, t, -1, ref r, formato);
        if (extra != 0) SetTextCharacterExtra(hdc, 0);
        SelectObject(hdc, anterior); DeleteObject(f);
    }

    private static int Medir(IntPtr hdc, string t, int tam, int peso)
    {
        var f = CreateFont(tam, 0, 0, 0, peso, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        var anterior = SelectObject(hdc, f);
        var r = new RECT();
        DrawText(hdc, t, -1, ref r, DT_CALCRECT | DT_SINGLELINE);
        SelectObject(hdc, anterior); DeleteObject(f);
        return r.Right - r.Left;
    }

    private static void Ceja(IntPtr hdc, string texto, string? nota, int x, int y, int w)
    {
        var mayus = texto.ToUpperInvariant();
        Escribir(hdc, mayus, x, y, w, 18, 13, 700, CEJA, DT_LEFT | DT_SINGLELINE | DT_VCENTER, 2);
        if (nota is { Length: > 0 })
        {
            // DT_CALCRECT no cuenta el espacio entre letras: se suma a mano.
            var ancho = Medir(hdc, mayus, 13, 700) + 2 * mayus.Length;
            Escribir(hdc, nota, x + ancho + 12, y, w - ancho - 12, 18, 14, 400, TENUE, DT_LEFT | DT_SINGLELINE | DT_VCENTER | DT_END_ELLIPSIS);
        }
    }

    private static string Pct(double p) => $"{Math.Round(p * 100)}%";

    private static string Decimal1(double v)
    {
        var s = v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        return Textos.Idioma is "es" or "de" or "fr" or "pt" or "it" ? s.Replace('.', ',') : s;
    }

    private static string NombreClase(string? clase) => clase is null ? "" : Textos.T("clase_" + clase.ToLowerInvariant());
    private static string NombreNivel(string clase, int nivel) => clase == "Mythic" ? NombreClase(clase) : $"{NombreClase(clase)} {nivel}";

    /// <summary>«27 sept» (en: «Sep 27»; ja/zh: «9月27日»), y la larga con el año.</summary>
    private static (string Corta, string Larga) Fecha(DateTime f)
    {
        var meses = Textos.T("fecha_meses").Split(',');
        var mes = meses.Length == 12 ? meses[f.Month - 1].Trim() : f.Month.ToString();
        var corta = Textos.Idioma is "ja" or "zh" ? $"{f.Month}月{f.Day}日" : Textos.Idioma == "en" ? $"{mes} {f.Day}" : $"{f.Day} {mes}";
        return (corta, Textos.T("fecha_corta", f.Day, mes, f.Year, f.Month));
    }

    // ── Lo que depende del mazo elegido ───────────────────────────────────

    private static string? NombreElegido(Estadisticas e) => mazoEst >= 0 && mazoEst < e.Mazos.Length ? e.Mazos[mazoEst].Nombre : null;
    private static PartidaEst[] Filtradas(Estadisticas e) => NombreElegido(e) is { } n ? e.Partidas.Where(p => p.Mazo == n).ToArray() : e.Partidas;

    /// <summary>El mazo cuya curva se enseña: el elegido; con «Todos», el que Arena tiene puesto; si no, el primero que tenga lista.</summary>
    private static MazoEst? MazoDeLaCurva(Estadisticas e)
    {
        if (mazoEst >= 0 && mazoEst < e.Mazos.Length) return e.Mazos[mazoEst];
        return e.Mazos.FirstOrDefault(m => m.Nombre == e.MazoArena && m.Analisis is not null) ?? e.Mazos.FirstOrDefault(m => m.Analisis is not null);
    }

    /// <summary>La evolución: por día si hay varios (los últimos 30), si no partida a partida (las últimas 60).</summary>
    private static (bool PorDia, PuntoEvolucion[] Puntos) Evolucion(PartidaEst[] partidas)
    {
        var dias = partidas.GroupBy(p => p.Cuando.Date).OrderBy(g => g.Key).ToArray();
        var acumulado = 0;
        if (dias.Length >= 2)
        {
            return (true, dias.TakeLast(30).Select(g =>
            {
                var gan = g.Count(p => p.Gane); var per = g.Count() - gan;
                acumulado += gan - per;
                var (corta, larga) = Fecha(g.Key);
                return new PuntoEvolucion(corta, larga, acumulado, gan, per);
            }).ToArray());
        }
        return (false, partidas.TakeLast(60).Select(p =>
        {
            acumulado += p.Gane ? 1 : -1;
            return new PuntoEvolucion(p.Cuando.ToString("HH:mm"), $"{Fecha(p.Cuando).Corta} · {p.Cuando:HH:mm}", acumulado, p.Gane ? 1 : 0, p.Gane ? 0 : 1);
        }).ToArray());
    }

    // ── La disposición ────────────────────────────────────────────────────

    private static void AnadirBloquesEstadisticas(List<Bloque> lista, Estadisticas e)
    {
        lista.Add(new Bloque("", false, false, ALTO_TARJETAS, Especial: "est_tarjetas", Col: 0));
        lista.Add(new Bloque("", false, false, ALTO_SELECTOR, Especial: "est_selector", Col: 0));
        if (e.Escalera.Length >= 2) lista.Add(new Bloque("", false, false, ALTO_ESCALERA, Especial: "est_escalera", Col: 0));
        lista.Add(new Bloque("", false, false, ALTO_INFERIOR, Especial: "est_evolucion", Col: 1));
        lista.Add(new Bloque("", false, false, ALTO_INFERIOR, Especial: "est_curva", Col: 2));
        rectFichasEst = new RECT[e.Mazos.Length + 1];
    }

    private static void PintarEstadistica(IntPtr zona, string especial, Estadisticas e, int y, int alto, int arriba, int bx, int bw)
    {
        switch (especial)
        {
            case "est_tarjetas": PintarTarjetas(zona, e, y, bx, bw); break;
            case "est_selector": PintarSelector(zona, e, y, arriba, bx, bw); break;
            case "est_escalera": PintarEscalera(zona, e, y, alto, arriba, bx, bw); break;
            case "est_evolucion": PintarEvolucion(zona, e, y, alto, arriba, bx, bw); break;
            case "est_curva": PintarCurva(zona, e, y, alto, arriba, bx, bw); break;
        }
    }

    // ── El ratón: qué hay debajo, qué se puede pulsar, qué hace ───────────

    private static int QueHayEnEstadisticas(int x, int y)
    {
        if (est is null) return 0;
        static bool Dentro(RECT r, int x, int y) => r.Right > 0 && x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;
        for (var i = 0; i < rectFichasEst.Length; i++) if (Dentro(rectFichasEst[i], x, y)) return BAJO_FICHA_EST - i;
        for (var i = 0; i < rectSegmentosEst.Length; i++) if (Dentro(rectSegmentosEst[i].R, x, y)) return BAJO_SEGMENTO_EST - i;
        for (var i = 0; i < rectPuntosEst.Length; i++) if (Dentro(rectPuntosEst[i], x, y)) return BAJO_PUNTO_EST - i;
        for (var i = 0; i < rectPeldanosEst.Length; i++) if (Dentro(rectPeldanosEst[i], x, y)) return BAJO_PELDANO_EST - i;
        return 0;
    }

    private static bool PulsableEstadisticas(int i) => est is not null && i <= BAJO_FICHA_EST && i > BAJO_FICHA_EST - rectFichasEst.Length;

    /// <summary>Pulsar una ficha elige ese mazo (o «Todos»); pulsar la elegida vuelve a «Todos».</summary>
    private static bool PulsarEstadisticas(int i, IntPtr ventana)
    {
        if (!PulsableEstadisticas(i)) return false;
        var elegido = BAJO_FICHA_EST - i - 1;
        mazoEst = elegido == mazoEst ? -1 : elegido;
        InvalidateRect(ventana, IntPtr.Zero, false);
        return true;
    }

    // ── Las cuatro tarjetas ───────────────────────────────────────────────

    private static void PintarTarjetas(IntPtr hdc, Estadisticas e, int y, int bx, int bw)
    {
        const int hueco = 14, alto = ALTO_TARJETAS - 8, pad = 16;
        var w = (bw - hueco * 3) / 4;
        int X(int i) => bx + i * (w + hueco);
        // Con un mazo elegido, sus números; el rango es de la cuenta.
        var elegido = mazoEst >= 0 && mazoEst < e.Mazos.Length ? e.Mazos[mazoEst] : null;
        var ganadas = elegido?.Ganadas ?? e.Ganadas; var perdidas = elegido?.Perdidas ?? e.Perdidas;
        var total = ganadas + perdidas;
        var pct = total > 0 ? (double)ganadas / total : 0;
        var partidas = Filtradas(e);
        var hoy = partidas.Where(p => p.Cuando.Date == DateTime.Now.Date).ToArray();
        var hoyGan = hoy.Count(p => p.Gane); var hoyPer = hoy.Length - hoyGan;
        var pctHoy = hoy.Length > 0 ? (double)hoyGan / hoy.Length : 0;
        bool? rachaGane = partidas.Length > 0 ? partidas[^1].Gane : null;
        var rachaN = 0;
        for (var i = partidas.Length - 1; i >= 0 && partidas[i].Gane == rachaGane; i--) rachaN++;
        (int R, int G, int B)? colorSel = elegido is not null ? ColorMazo(elegido.Nombre) : null;

        var g = Lienzo(hdc);
        if (g != IntPtr.Zero)
        {
            for (var i = 0; i < 4; i++) Rectangulo(g, X(i), y, w, alto, RADIO, TARJETA, i < 3 && colorSel is { } cs ? Argb(150, cs) : BORDE);
            var lado = 58; var ax = X(0) + w - pad - lado; var ay = y + (alto - lado) / 2;
            var fondoAnillo = Pluma(Argb(255, 36, 48, 74), 7);
            GdipDrawArcI(g, fondoAnillo, ax, ay, lado, lado, 0, 360);
            GdipDeletePen(fondoAnillo);
            if (total > 0)
            {
                var arco = Pluma(Argb(255, ColorPorcentaje(pct)), 7);
                GdipDrawArcI(g, arco, ax, ay, lado, lado, -90, (float)(360 * Math.Max(0.004, pct)));
                GdipDeletePen(arco);
            }
            if (hoy.Length > 0)
            {
                var barX = X(1) + pad; var barY = y + alto - pad - 6; var barW = w - pad * 2;
                Rectangulo(g, barX, barY, barW, 6, 3, Argb(255, 248, 113, 113));
                var verde = (int)Math.Round(barW * pctHoy);
                if (verde > 0) Rectangulo(g, barX, barY, verde, 6, 3, Argb(255, 52, 211, 153));
            }
            var cabe = Math.Max(1, (w - pad * 2 + 3) / 11);
            var ultimas = partidas.TakeLast(cabe).ToArray();
            for (var i = 0; i < ultimas.Length; i++)
                Circulo(g, X(2) + pad + 4 + i * 11, y + alto - pad - 4, 4, ultimas[i].Gane ? Argb(255, 52, 211, 153) : Argb(255, 248, 113, 113));
            // La insignia de verdad, la misma de la web.
            if (e.Clase is { } clase && e.Iconos.TryGetValue($"{clase}-{(clase == "Mythic" ? 1 : e.Nivel ?? 4)}", out var fichero))
                Insignia(g, fichero, X(3) + pad + 34, y + 30, 58);
            GdipDeleteGraphics(g);
        }

        const uint una = DT_LEFT | DT_SINGLELINE | DT_VCENTER;
        Ceja(hdc, Textos.T("est_total"), null, X(0) + pad, y + 12, w - pad * 2);
        Escribir(hdc, $"{ganadas} – {perdidas}", X(0) + pad, y + 34, w - pad * 2 - 64, 40, 32, 700, BLANCO, una | DT_END_ELLIPSIS);
        Escribir(hdc, Textos.T("est_partidas", total), X(0) + pad, y + 74, w - pad * 2 - 64, 20, 14, 400, APAGADO, una);
        {
            var lado = 58; var ax = X(0) + w - pad - lado; var ay = y + (alto - lado) / 2;
            var (r, gg, b) = ColorPorcentaje(pct);
            Escribir(hdc, total > 0 ? Pct(pct) : "–", ax, ay, lado, lado, 16, 700, total > 0 ? Rgb(r, gg, b) : APAGADO, DT_CENTER | DT_SINGLELINE | DT_VCENTER);
        }
        Ceja(hdc, Textos.T("est_hoy"), null, X(1) + pad, y + 12, w - pad * 2);
        Escribir(hdc, $"{hoyGan} – {hoyPer}", X(1) + pad, y + 34, w - pad * 2, 40, 32, 700, BLANCO, una | DT_END_ELLIPSIS);
        if (hoy.Length > 0)
        {
            var (r, gg, b) = ColorPorcentaje(pctHoy);
            var ancho = Medir(hdc, $"{hoyGan} – {hoyPer}", 32, 700);
            Escribir(hdc, Pct(pctHoy), X(1) + pad + ancho + 10, y + 46, w - pad * 2 - ancho - 10, 24, 16, 700, Rgb(r, gg, b), una);
        }
        else Escribir(hdc, Textos.T("est_hoy_nada"), X(1) + pad, y + 74, w - pad * 2, 20, 14, 400, APAGADO, una | DT_END_ELLIPSIS);
        Ceja(hdc, Textos.T("est_racha"), null, X(2) + pad, y + 12, w - pad * 2);
        if (rachaGane is { } gano)
        {
            var n = rachaN.ToString();
            Escribir(hdc, n, X(2) + pad, y + 34, w - pad * 2, 40, 32, 700, gano ? Rgb(52, 211, 153) : Rgb(248, 113, 113), una);
            var ancho = Medir(hdc, n, 32, 700);
            var etiqueta = Textos.T(gano ? (rachaN == 1 ? "est_racha_v1" : "est_racha_vn") : (rachaN == 1 ? "est_racha_d1" : "est_racha_dn"));
            Escribir(hdc, etiqueta, X(2) + pad + ancho + 10, y + 46, w - pad * 2 - ancho - 10, 22, 15, 600, APAGADO, una | DT_END_ELLIPSIS);
        }
        else Escribir(hdc, "–", X(2) + pad, y + 34, w - pad * 2, 40, 32, 700, APAGADO, una);
        Ceja(hdc, Textos.T("est_rango"), null, X(3) + pad, y + 12, w - pad * 2);
        if (e.Clase is { } c)
        {
            var tx = X(3) + pad + 80;
            Escribir(hdc, NombreClase(c), tx, y + 38, X(3) + w - pad - tx, 30, 24, 700, BLANCO, una | DT_END_ELLIPSIS);
            if (c != "Mythic" && e.Nivel is { } nivel) Escribir(hdc, Textos.T("est_nivel", nivel), tx, y + 68, X(3) + w - pad - tx, 20, 14, 400, APAGADO, una);
        }
        else Escribir(hdc, Textos.T("est_sin_rango"), X(3) + pad, y + 40, w - pad * 2, 30, 20, 600, APAGADO, una | DT_END_ELLIPSIS);
    }

    // ── El selector de mazos ──────────────────────────────────────────────

    private static void PintarSelector(IntPtr hdc, Estadisticas e, int y, int arriba, int bx, int bw)
    {
        // «Todos» y una ficha por mazo: punto de su color, nombre, V–D y %.
        var fichas = new List<(string Nombre, string Datos, (int R, int G, int B) Color, bool Arena)>
        {
            (Textos.T("est_todos"), $"{e.Ganadas}–{e.Perdidas}", (125, 211, 252), false),
        };
        foreach (var m in e.Mazos)
        {
            var j = m.Ganadas + m.Perdidas;
            fichas.Add((m.Nombre, j > 0 ? $"{m.Ganadas}–{m.Perdidas} · {Pct((double)m.Ganadas / j)}" : "0–0", ColorMazo(m.Nombre), m.Nombre == e.MazoArena));
        }
        const int alto = 34, hueco = 8, padIzq = 30, padDer = 14;
        // Los anchos: el nombre se recorta si no caben todas en una fila.
        var anchosDatos = fichas.Select(f => Medir(hdc, f.Datos, 13, 500)).ToArray();
        var anchosNombre = fichas.Select(f => Medir(hdc, f.Nombre, 14, 600)).ToArray();
        int AnchoFicha(int i, int maxNombre) => padIzq + Math.Min(anchosNombre[i], maxNombre) + 10 + anchosDatos[i] + padDer;
        var maxNombre = 400;
        while (maxNombre > 60 && Enumerable.Range(0, fichas.Count).Sum(i => AnchoFicha(i, maxNombre)) + hueco * (fichas.Count - 1) > bw) maxNombre -= 10;

        var x = bx;
        var rects = new RECT[fichas.Count];
        for (var i = 0; i < fichas.Count; i++)
        {
            var w = AnchoFicha(i, maxNombre);
            if (x + w > bx + bw) { rects[i] = default; continue; }
            rects[i] = new RECT { Left = x, Top = y + 4, Right = x + w, Bottom = y + 4 + alto };
            x += w + hueco;
        }
        var g = Lienzo(hdc);
        if (g != IntPtr.Zero)
        {
            for (var i = 0; i < fichas.Count; i++)
            {
                var r = rects[i]; if (r.Right == 0) continue;
                var elegida = mazoEst == i - 1;
                var sobre = bajoRaton == BAJO_FICHA_EST - i;
                var c = fichas[i].Color;
                Rectangulo(g, r.Left, r.Top, r.Right - r.Left, alto, alto / 2,
                    elegida ? Argb(60, c) : sobre ? Argb(255, 30, 41, 64) : Argb(255, 20, 28, 48),
                    elegida ? Argb(255, c) : Argb(255, 44, 58, 88));
                Circulo(g, r.Left + 16, r.Top + alto / 2, 5, Argb(255, c));
                // El mazo que Arena tiene puesto: un aro ámbar alrededor de su punto.
                if (fichas[i].Arena)
                {
                    var aro = Pluma(Argb(255, 251, 191, 36), 1.5f);
                    GdipDrawArcI(g, aro, r.Left + 16 - 8, r.Top + alto / 2 - 8, 16, 16, 0, 360);
                    GdipDeletePen(aro);
                }
            }
            GdipDeleteGraphics(g);
        }
        for (var i = 0; i < fichas.Count; i++)
        {
            var r = rects[i];
            if (r.Right == 0) { rectFichasEst[i] = default; continue; }
            var elegida = mazoEst == i - 1;
            var anchoNombre = Math.Min(anchosNombre[i], maxNombre);
            Escribir(hdc, fichas[i].Nombre, r.Left + padIzq, r.Top, anchoNombre, alto, 14, 600, elegida ? Rgb(255, 255, 255) : BLANCO, DT_LEFT | DT_SINGLELINE | DT_VCENTER | DT_END_ELLIPSIS);
            Escribir(hdc, fichas[i].Datos, r.Left + padIzq + anchoNombre + 10, r.Top, anchosDatos[i] + 4, alto, 13, 500, APAGADO, DT_LEFT | DT_SINGLELINE | DT_VCENTER);
            rectFichasEst[i] = new RECT { Left = r.Left, Top = arriba + r.Top, Right = r.Right, Bottom = arriba + r.Bottom };
        }
    }

    // ── La escalera de rango, a todo el ancho ─────────────────────────────

    private static void PintarEscalera(IntPtr hdc, Estadisticas e, int y, int alto, int arriba, int bx, int bw)
    {
        const int anchoRotulos = 96;
        var cy0 = y + 28; var ch = alto - 34;
        int gx = bx + anchoRotulos + 8, gw = bw - anchoRotulos - 8 - 16, gy = cy0 + 40, gh = ch - 40 - 34;
        // Las que caben con aire (9 px por partida como poco), las últimas.
        var cuantas = Math.Min(e.Escalera.Length, Math.Max(10, gw / 9));
        desdePeldano = e.Escalera.Length - cuantas;
        var p = e.Escalera[desdePeldano..];
        Ceja(hdc, Textos.T("est_escalera"), Textos.T("est_escalera_nota", p.Length), bx, y, bw);
        var suelo = (int)Math.Floor((Math.Min(p.Min(q => q.Pos), p.Min(q => q.Antes)) - 1) / 6.0) * 6;
        var techo = (int)Math.Ceiling((Math.Max(p.Max(q => q.Pos), p.Max(q => q.Antes)) + 1) / 6.0) * 6;
        if (techo - suelo < 12) techo = suelo + 12;
        double ancho = gw / (double)p.Length;
        int X(double i) => gx + (int)Math.Round(i * ancho);
        int Y(double pos) => gy + (int)Math.Round((techo - pos) * gh / (double)(techo - suelo));
        string ClaseDe(int pos) => pos >= 120 ? "Mythic" : new[] { "Bronze", "Silver", "Gold", "Platinum", "Diamond" }[Math.Clamp(pos / 24, 0, 4)];
        int NivelDe(int pos) => 4 - (pos % 24) / 6;
        var elegido = NombreElegido(e);

        // Las insignias: donde se SUBE de nivel, encima de ese punto; si chocan, gana la de después.
        var insignias = new List<(RECT R, string Fichero)>();
        for (var i = 0; i < p.Length; i++)
        {
            if (!p[i].Subio || !e.Iconos.TryGetValue($"{p[i].Clase}-{(p[i].Clase == "Mythic" ? 1 : p[i].Nivel)}", out var f)) continue;
            const int h = 26, w = 32;
            var cx = X(i + 1);
            var left = Math.Clamp(cx - w / 2, gx, gx + gw - w);
            var top = Math.Max(cy0 + 4, Y(p[i].Pos) - h - 6);
            var r = new RECT { Left = left, Top = top, Right = left + w, Bottom = top + h };
            insignias.RemoveAll(o => o.R.Left < r.Right && r.Left < o.R.Right && o.R.Top < r.Bottom && r.Top < o.R.Bottom);
            insignias.Add((r, f));
        }

        var g = Lienzo(hdc);
        if (g != IntPtr.Zero)
        {
            Rectangulo(g, bx, cy0, bw, ch, RADIO, TARJETA, BORDE);
            // Una franja por nivel, del color de su clase; más marcada donde cambia de clase.
            for (var b = suelo; b < techo; b += 6)
            {
                var c = ColorClase(ClaseDe(b));
                GdipCreateSolidFill(Argb((b / 6) % 2 == 0 ? 14 : 24, c), out var br);
                var y1 = Y(b + 6); var y2 = Y(b);
                GdipFillPolygonI(g, br, [new PuntoG { X = gx, Y = y1 }, new PuntoG { X = gx + gw, Y = y1 }, new PuntoG { X = gx + gw, Y = y2 }, new PuntoG { X = gx, Y = y2 }], 4, 0);
                GdipDeleteBrush(br);
                var raya = Pluma(b % 24 == 0 ? Argb(110, c) : Argb(255, 30, 41, 64), 1, false);
                GdipDrawLineI(g, raya, gx, y2, gx + gw, y2);
                GdipDeletePen(raya);
            }
            // Una raya de guiones donde empieza cada día.
            var rayaDia = Pluma(Argb(255, 51, 65, 85), 1, false);
            GdipSetPenDashStyle(rayaDia, GUIONES);
            for (var i = 1; i < p.Length; i++)
                if (p[i].Cuando.Date != p[i - 1].Cuando.Date) GdipDrawLineI(g, rayaDia, X(i), gy - 4, X(i), gy + gh + 4);
            GdipDeletePen(rayaDia);
            // La escalera: tramo llano y luego sus escaloncitos, del color de su
            // mazo; con un mazo elegido, las partidas de los demás, apagadas.
            for (var i = 0; i < p.Length; i++)
            {
                var apagada = elegido is not null && p[i].Mazo != elegido;
                var color = ColorMazo(p[i].Mazo);
                var salto = Math.Abs(p[i].Pos - p[i].Antes);
                var arranque = gx + (i + (salto > 0 ? 0.4 : 1)) * ancho;
                var camino = new List<PuntoG> { new() { X = X(i), Y = Y(p[i].Antes) }, new() { X = (int)Math.Round(arranque), Y = Y(p[i].Antes) } };
                var pasoX = (X(i + 1) - arranque) / Math.Max(salto, 1);
                for (var k = 1; k <= salto; k++)
                {
                    var pos = p[i].Antes + Math.Sign(p[i].Pos - p[i].Antes) * k;
                    camino.Add(new PuntoG { X = (int)Math.Round(arranque + pasoX * (k - 1)), Y = Y(pos) });
                    camino.Add(new PuntoG { X = (int)Math.Round(arranque + pasoX * k), Y = Y(pos) });
                }
                var linea = Pluma(Argb(apagada ? 45 : 255, color), apagada ? 2f : 2.6f);
                GdipDrawLinesI(g, linea, camino.ToArray(), camino.Count);
                GdipDeletePen(linea);
                var sobre = bajoRaton == BAJO_PELDANO_EST - i;
                var punto = p[i].Gane ? (52, 211, 153) : (248, 113, 113);
                if (sobre) Circulo(g, X(i + 1), Y(p[i].Pos), 6, Argb(255, 241, 245, 249));
                // Con muchas partidas, puntos más pequeños: si no, tapan la línea.
                Circulo(g, X(i + 1), Y(p[i].Pos), sobre ? 4 : ancho < 12 ? 2 : 3, Argb(apagada ? 60 : 255, punto));
            }
            foreach (var (r, f) in insignias) Insignia(g, f, r.Left + (r.Right - r.Left) / 2, r.Top, r.Bottom - r.Top);
            GdipDeleteGraphics(g);
        }
        // Dónde está cada partida, para la caja al pasar: su columna entera.
        rectPeldanosEst = new RECT[p.Length];
        for (var i = 0; i < p.Length; i++)
            rectPeldanosEst[i] = new RECT { Left = X(i + 0.5), Top = arriba + gy - 6, Right = Math.Max(X(i + 0.5) + 2, X(i + 1.5)), Bottom = arriba + gy + gh + 6 };
        // El nombre de cada nivel a la izquierda, y la fecha donde empieza cada día.
        var altoFranja = gh * 6.0 / (techo - suelo);
        var cadaCuantos = Math.Max(1, (int)Math.Ceiling(14 / altoFranja));
        var k2 = 0;
        for (var b = suelo; b < techo; b += 6, k2++)
        {
            if (k2 % cadaCuantos != 0) continue;
            var clase = ClaseDe(b);
            var (r, gg, bb) = ColorClase(clase);
            var y1 = Y(b + 6); var y2 = Y(b);
            Escribir(hdc, NombreNivel(clase, NivelDe(b)), bx + 12, Math.Min(y1, y2 - 16), anchoRotulos - 4, Math.Max(16, y2 - y1), 13, 600, Rgb(r * 8 / 10 + 40, gg * 8 / 10 + 40, bb * 8 / 10 + 40), DT_LEFT | DT_SINGLELINE | DT_VCENTER | DT_END_ELLIPSIS);
        }
        var finFecha = int.MinValue;
        for (var i = 0; i < p.Length; i++)
        {
            if (i > 0 && p[i].Cuando.Date == p[i - 1].Cuando.Date) continue;
            var texto = Fecha(p[i].Cuando).Corta;
            var w = Medir(hdc, texto, 13, 500);
            var tx = Math.Max(Math.Min(X(i) + 3, gx + gw - w), gx);
            if (tx < finFecha + 8) continue;
            finFecha = tx + w;
            Escribir(hdc, texto, tx, gy + gh + 10, w + 4, 18, 13, 500, TENUE, DT_LEFT | DT_SINGLELINE | DT_VCENTER);
        }
    }

    // ── La evolución ──────────────────────────────────────────────────────

    private static void PintarEvolucion(IntPtr hdc, Estadisticas e, int y, int alto, int arriba, int bx, int bw)
    {
        var (porDia, puntos) = Evolucion(Filtradas(e));
        puntosEst = puntos;
        var elegido = NombreElegido(e);
        Ceja(hdc, Textos.T("est_evolucion"), elegido ?? Textos.T(porDia ? "est_evolucion_dia" : "est_evolucion_partida"), bx, y, bw);
        var cy0 = y + 28; var ch = alto - 34;
        int gx = bx + 48, gy = cy0 + 18, gw = bw - 48 - 24, gh = ch - 18 - 32;
        var valores = puntos.Select(q => q.Valor).Append(0).ToArray();
        var max = Math.Max(1, valores.Max()); var min = Math.Min(-1, valores.Min());
        var paso = Math.Max(1, (int)Math.Ceiling((max - min) / 4.0));
        max = (int)Math.Ceiling(max / (double)paso) * paso; min = (int)Math.Floor(min / (double)paso) * paso;
        int Y(double v) => gy + (int)Math.Round((max - v) * gh / (double)(max - min));
        int X(int i) => puntos.Length <= 1 ? gx + gw / 2 : gx + (int)Math.Round(i * gw / (double)(puntos.Length - 1));
        var cero = Y(0);
        var pts = puntos.Select((q, i) => new PuntoG { X = X(i), Y = Y(q.Valor) }).ToArray();
        var colorLinea = elegido is not null ? ColorMazo(elegido) : (56, 189, 248);

        var g = Lienzo(hdc);
        if (g != IntPtr.Zero)
        {
            Rectangulo(g, bx, cy0, bw, ch, RADIO, TARJETA, BORDE);
            var raya = Pluma(Argb(255, 30, 41, 64), 1, false);
            for (var v = min; v <= max; v += paso) GdipDrawLineI(g, raya, gx, Y(v), gx + gw, Y(v));
            GdipDeletePen(raya);
            if (pts.Length >= 2)
            {
                var area = pts.Prepend(new PuntoG { X = pts[0].X, Y = cero }).Append(new PuntoG { X = pts[^1].X, Y = cero }).ToArray();
                GdipSetClipRectI(g, gx - 2, gy - 4, gw + 4, cero - gy + 4, 0);
                var a1 = new PuntoG { X = 0, Y = gy }; var a2 = new PuntoG { X = 0, Y = cero + 1 };
                GdipCreateLineBrushI(ref a1, ref a2, Argb(110, 52, 211, 153), Argb(10, 52, 211, 153), 0, out var verde);
                GdipFillPolygonI(g, verde, area, area.Length, 0); GdipDeleteBrush(verde);
                GdipSetClipRectI(g, gx - 2, cero, gw + 4, gy + gh - cero + 4, 0);
                var b1 = new PuntoG { X = 0, Y = cero - 1 }; var b2 = new PuntoG { X = 0, Y = gy + gh };
                GdipCreateLineBrushI(ref b1, ref b2, Argb(10, 248, 113, 113), Argb(110, 248, 113, 113), 0, out var rojo);
                GdipFillPolygonI(g, rojo, area, area.Length, 0); GdipDeleteBrush(rojo);
                GdipResetClip(g);
            }
            var lineaCero = Pluma(Argb(255, 100, 116, 139), 1, false);
            GdipSetPenDashStyle(lineaCero, GUIONES);
            GdipDrawLineI(g, lineaCero, gx, cero, gx + gw, cero);
            GdipDeletePen(lineaCero);
            if (pts.Length >= 2)
            {
                var linea = Pluma(Argb(255, colorLinea), 2.5f);
                GdipDrawLinesI(g, linea, pts, pts.Length);
                GdipDeletePen(linea);
            }
            var bajo = bajoRaton <= BAJO_PUNTO_EST && bajoRaton > BAJO_FICHA_EST && BAJO_PUNTO_EST - bajoRaton < pts.Length ? BAJO_PUNTO_EST - bajoRaton : -1;
            var radio = pts.Length > 30 ? 2 : 3;
            for (var i = 0; i < pts.Length; i++)
            {
                if (i == bajo) { Circulo(g, pts[i].X, pts[i].Y, 7, Argb(255, 241, 245, 249)); Circulo(g, pts[i].X, pts[i].Y, 4, Argb(255, colorLinea)); }
                else Circulo(g, pts[i].X, pts[i].Y, radio, Argb(255, colorLinea));
            }
            GdipDeleteGraphics(g);
        }
        rectPuntosEst = new RECT[pts.Length];
        centroPuntosEst = new (int, int)[pts.Length];
        for (var i = 0; i < pts.Length; i++)
        {
            var medio = pts.Length <= 1 ? gw : gw / (pts.Length - 1);
            rectPuntosEst[i] = new RECT { Left = pts[i].X - Math.Max(4, medio / 2), Top = arriba + gy - 6, Right = pts[i].X + Math.Max(4, medio / 2), Bottom = arriba + gy + gh + 6 };
            centroPuntosEst[i] = (pts[i].X, arriba + pts[i].Y);
        }
        if (puntos.Length == 0) { Escribir(hdc, Textos.T("est_mazos_nada"), gx, gy, gw, gh, 15, 400, APAGADO, DT_CENTER | DT_SINGLELINE | DT_VCENTER); return; }
        foreach (var v in new[] { max, 0, min }.Distinct())
            Escribir(hdc, v > 0 ? $"+{v}" : $"{v}", bx + 4, Y(v) - 9, 38, 18, 13, 500, TENUE, DT_RIGHT | DT_SINGLELINE | DT_VCENTER);
        var marcas = puntos.Length <= 2 ? new[] { 0, puntos.Length - 1 } : new[] { 0, puntos.Length / 2, puntos.Length - 1 };
        foreach (var i in marcas.Distinct())
        {
            var formato = i == 0 ? DT_LEFT : i == puntos.Length - 1 ? DT_RIGHT : DT_CENTER;
            var lx = i == 0 ? X(i) - 4 : i == puntos.Length - 1 ? X(i) - 116 : X(i) - 60;
            Escribir(hdc, puntos[i].Corta, lx, gy + gh + 10, 120, 18, 13, 500, TENUE, formato | DT_SINGLELINE | DT_VCENTER);
        }
        var ultimo = puntos[^1].Valor;
        var (r, gg, b) = ultimo >= 0 ? (52, 211, 153) : (248, 113, 113);
        Escribir(hdc, ultimo > 0 ? $"+{ultimo}" : $"{ultimo}", Math.Min(X(puntos.Length - 1) - 50, bx + bw - 60), Y(ultimo) - 26, 56, 18, 14, 700, Rgb(r, gg, b), DT_RIGHT | DT_SINGLELINE | DT_VCENTER);
    }

    // ── La curva de maná, por tipo de carta ───────────────────────────────

    private static void PintarCurva(IntPtr hdc, Estadisticas e, int y, int alto, int arriba, int bx, int bw)
    {
        var mazo = MazoDeLaCurva(e);
        Ceja(hdc, Textos.T("est_curva"), mazo?.Nombre ?? e.MazoArena, bx, y, bw);
        var cy0 = y + 28;
        rectSegmentosEst = [];
        if (mazo?.Analisis is not { } a)
        {
            var g0 = Lienzo(hdc);
            if (g0 != IntPtr.Zero) { Rectangulo(g0, bx, cy0, bw, alto - 34, RADIO, TARJETA, BORDE); GdipDeleteGraphics(g0); }
            Escribir(hdc, Textos.T(e.MazoArena is null && mazo is null ? "est_sin_mazo" : "est_mazo_no_subido"), bx + 16, cy0 + 16, bw - 32, alto - 60, 15, 400, APAGADO, DT_LEFT | DT_WORDBREAK);
            return;
        }
        const int altoGrafica = 206;
        var columnas = a.Curva;
        var n = columnas.Length;
        int gx = bx + 16, gw = bw - 32, gy = cy0 + 30, gh = altoGrafica - 30 - 30;
        var maximo = Math.Max(1, columnas.Max(c => c.Total));
        const int hueco = 10; var anchoBarra = (gw - hueco * (n - 1)) / n;
        var tipos = columnas.SelectMany(c => c.Tipos.Select(t => t.Tipo)).Distinct().ToArray();
        var segmentos = new List<(RECT R, string Texto)>();

        // Las fichas de debajo, colocadas en filas.
        var fichas = new[]
        {
            Textos.T("est_tierras", a.Tierras),
            Textos.T("est_medio", Decimal1(a.Medio)),
            Textos.T("est_mano", Pct(a.ManoBuena)),
            Textos.T("est_t3", Pct(a.TierraT3)),
        };
        var yLeyenda = cy0 + altoGrafica + 10;
        var colocadas = new List<(int X, int Y, int W, string Texto)>();
        {
            var fx = bx; var fy = yLeyenda + 28;
            foreach (var t in fichas)
            {
                var w = Medir(hdc, t, 14, 500) + 22;
                if (fx + w > bx + bw && fx > bx) { fx = bx; fy += 32; }
                colocadas.Add((fx, fy, w, t));
                fx += w + 8;
            }
        }

        var g = Lienzo(hdc);
        if (g != IntPtr.Zero)
        {
            Rectangulo(g, bx, cy0, bw, altoGrafica, RADIO, TARJETA, BORDE);
            var rayaMedia = Pluma(Argb(255, 26, 35, 56), 1, false);
            GdipDrawLineI(g, rayaMedia, gx, gy + gh / 2, gx + gw, gy + gh / 2);
            GdipDeletePen(rayaMedia);
            for (var i = 0; i < n; i++)
            {
                var x = gx + i * (anchoBarra + hueco);
                var base0 = gy + gh;
                var hTotal = columnas[i].Total == 0 ? 0 : Math.Max(4, (int)Math.Round(gh * columnas[i].Total / (double)maximo));
                var acumulado = 0;
                for (var s = 0; s < columnas[i].Tipos.Length; s++)
                {
                    var seg = columnas[i].Tipos[s];
                    // El último trozo cierra al alto total, para que no se pierdan píxeles al redondear.
                    var arribaDel = s == columnas[i].Tipos.Length - 1;
                    var h = arribaDel ? hTotal - acumulado : Math.Max(3, (int)Math.Round(hTotal * seg.N / (double)columnas[i].Total));
                    if (h <= 0) continue;
                    var top = base0 - acumulado - h;
                    var c = ColorTipo(seg.Tipo);
                    var sobre = bajoRaton == BAJO_SEGMENTO_EST - segmentos.Count;
                    Rectangulo(g, x, top, anchoBarra, h, arribaDel ? Math.Min(6, anchoBarra / 2) : 1, Argb(255, c), sobre ? Argb(255, 241, 245, 249) : null, soloArriba: true, rellenoAbajo: Argb(176, c));
                    if (!arribaDel)
                    {
                        var corte = Pluma(Argb(90, 2, 6, 23), 1, false);
                        GdipDrawLineI(g, corte, x, top, x + anchoBarra, top);
                        GdipDeletePen(corte);
                    }
                    var texto = $"{seg.N} · {Textos.T("tipo_" + seg.Tipo)}" + (seg.Cartas.Length > 0 ? "\n" + string.Join("\n", seg.Cartas.Take(8)) : "");
                    segmentos.Add((new RECT { Left = x, Top = arriba + top, Right = x + anchoBarra, Bottom = arriba + top + h }, texto));
                    acumulado += h;
                }
            }
            var suelo = Pluma(Argb(255, 51, 65, 85), 1, false);
            GdipDrawLineI(g, suelo, gx, gy + gh, gx + gw, gy + gh);
            GdipDeletePen(suelo);
            // La leyenda: un cuadradito de cada tipo presente.
            var lx = bx;
            foreach (var t in tipos)
            {
                Rectangulo(g, lx, yLeyenda + 5, 10, 10, 2, Argb(255, ColorTipo(t)));
                lx += 16 + Medir(hdc, Textos.T("tipo_" + t), 13, 500) + 14;
            }
            foreach (var f in colocadas) Rectangulo(g, f.X, f.Y, f.W, 26, 13, Argb(255, 26, 34, 54), Argb(255, 44, 58, 88));
            GdipDeleteGraphics(g);
        }
        rectSegmentosEst = segmentos.ToArray();
        for (var i = 0; i < n; i++)
        {
            var x = gx + i * (anchoBarra + hueco);
            var v = columnas[i].Total;
            var h = v == 0 ? 0 : Math.Max(4, (int)Math.Round(gh * v / (double)maximo));
            if (v > 0) Escribir(hdc, v.ToString(), x, gy + gh - h - 22, anchoBarra, 20, 14, 700, BLANCO, DT_CENTER | DT_SINGLELINE | DT_VCENTER);
            Escribir(hdc, i == n - 1 ? $"{columnas[i].Cmc}+" : $"{columnas[i].Cmc}", x, gy + gh + 6, anchoBarra, 20, 14, 600, APAGADO, DT_CENTER | DT_SINGLELINE | DT_VCENTER);
        }
        {
            var lx = bx;
            foreach (var t in tipos)
            {
                var nombre = Textos.T("tipo_" + t);
                var w = Medir(hdc, nombre, 13, 500);
                Escribir(hdc, nombre, lx + 16, yLeyenda, w + 4, 20, 13, 500, APAGADO, DT_LEFT | DT_SINGLELINE | DT_VCENTER);
                lx += 16 + w + 14;
            }
        }
        foreach (var f in colocadas)
            Escribir(hdc, f.Texto, f.X + 11, f.Y, f.W - 18, 26, 14, 500, Rgb(203, 213, 225), DT_LEFT | DT_SINGLELINE | DT_VCENTER);
    }

    // ── Las cajas al pasar el ratón ───────────────────────────────────────

    private static void PintarAyudaEstadisticas(IntPtr hdc)
    {
        if (est is not { } e) return;
        string? texto = null; int cx = 0, cy = 0;
        if (bajoRaton <= BAJO_PUNTO_EST && bajoRaton > BAJO_FICHA_EST && BAJO_PUNTO_EST - bajoRaton < puntosEst.Length && BAJO_PUNTO_EST - bajoRaton < centroPuntosEst.Length)
        {
            var i = BAJO_PUNTO_EST - bajoRaton;
            var q = puntosEst[i];
            texto = Textos.T("est_punto", q.Larga, q.Ganadas, q.Perdidas, q.Valor > 0 ? $"+{q.Valor}" : $"{q.Valor}");
            (cx, cy) = centroPuntosEst[i];
        }
        else if (bajoRaton <= BAJO_PELDANO_EST && bajoRaton > BAJO_SEGMENTO_EST && BAJO_PELDANO_EST - bajoRaton < rectPeldanosEst.Length && desdePeldano + BAJO_PELDANO_EST - bajoRaton < e.Escalera.Length)
        {
            var i = BAJO_PELDANO_EST - bajoRaton;
            var q = e.Escalera[desdePeldano + i];
            texto = $"{Fecha(q.Cuando).Corta} {q.Cuando:HH:mm} · {NombreNivel(q.Clase, q.Nivel)} · {(q.Gane ? "✓" : "✗")}" + (q.Mazo is { } m ? $" · {m}" : "");
            var r = rectPeldanosEst[i];
            (cx, cy) = ((r.Left + r.Right) / 2, r.Top + 10);
        }
        else if (bajoRaton <= BAJO_SEGMENTO_EST && BAJO_SEGMENTO_EST - bajoRaton < rectSegmentosEst.Length)
        {
            var (r, t) = rectSegmentosEst[BAJO_SEGMENTO_EST - bajoRaton];
            texto = t;
            (cx, cy) = ((r.Left + r.Right) / 2, r.Top);
        }
        if (texto is null) return;
        var lineas = texto.Split('\n');
        var ancho = lineas.Select((l, k) => Medir(hdc, l, 14, k == 0 && lineas.Length > 1 ? 700 : 500)).Max() + 24;
        var alto = 12 + lineas.Length * 20;
        var x = Math.Clamp(cx - ancho / 2, MARGEN, ANCHO_PANEL - MARGEN - ancho);
        var yCaja = Math.Max(ALTO_CABECERA + 4, cy - alto - 12);
        var g = Lienzo(hdc);
        if (g != IntPtr.Zero) { Rectangulo(g, x, yCaja, ancho, alto, 8, Argb(250, 30, 41, 59), Argb(255, 56, 189, 248)); GdipDeleteGraphics(g); }
        for (var k = 0; k < lineas.Length; k++)
            Escribir(hdc, lineas[k], x + 12, yCaja + 6 + k * 20, ancho - 24, 20, 14, k == 0 && lineas.Length > 1 ? 700 : 500, k == 0 ? BLANCO : Rgb(203, 213, 225), DT_LEFT | DT_SINGLELINE | DT_VCENTER);
    }
}
