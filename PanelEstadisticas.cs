using System.Runtime.InteropServices;

namespace MtgCornerArenaBridge;

/// <summary>
/// «STATS»: EL OVERVIEW DE LA WEB, ENCIMA DEL JUEGO.
///
/// Pedido del usuario el 2026-09-27 («que salga lo de overview de la web, curva
/// de maná, estadísticas, gráfica de victorias… no en la web sino en la
/// pantalla», y «con un diseño elegante»). Es un modo más del panel del
/// resumen: la misma ventana, la misma cabecera y el mismo pie, y en la zona de
/// texto, en vez de secciones, cinco bloques:
///
///   · arriba, a lo ancho, cuatro tarjetas: el total con su porcentaje en un
///     anillo, lo de hoy, la racha con las últimas veinte y el rango;
///   · a la izquierda, la evolución (victorias menos derrotas, acumuladas) y
///     tus mazos con su porcentaje;
///   · a la derecha, la escalera de rango y la curva de maná del mazo que Arena
///     tiene elegido, con sus probabilidades de tierras.
///
/// Todo viene ya contado de /api/mtga-device/estadisticas. Aquí sólo se pinta:
/// las formas con GDI+ y suavizado (anillos, líneas, áreas, barras con
/// degradado, tarjetas redondeadas), el texto con GDI como el resto del panel.
/// </summary>
internal static partial class PanelCartas
{
    // ── Los datos, ya preparados por Program ──────────────────────────────

    /// <summary>Un punto de la evolución: su etiqueta corta (eje), la larga (al pasar), el acumulado y lo de ese punto.</summary>
    public sealed record PuntoEvolucion(string Corta, string Larga, int Valor, int Ganadas, int Perdidas);
    public sealed record MazoEst(string Nombre, int Ganadas, int Perdidas, string? Ruta, bool Actual);
    public sealed record PeldanoEst(int Posicion, string Clase, int Nivel, bool Gane);
    public sealed record ColorEst(string Color, int Fuentes, double T3);

    public sealed record Estadisticas(
        int Ganadas, int Perdidas, int HoyGanadas, int HoyPerdidas,
        bool? RachaGane, int RachaN, bool[] Ultimas,
        bool PorDia, PuntoEvolucion[] Evolucion,
        MazoEst[] Mazos,
        string? Clase, int? Nivel,
        PeldanoEst[] Escalera,
        string? MazoNombre, bool MazoEnLaWeb, int Tierras, int[] Curva, double Medio,
        double ManoBuena, double TierraT3, ColorEst[] Colores);

    private static Estadisticas? est;
    private static RECT[] rectPuntosEst = [];
    private static RECT[] rectMazosEst = [];
    /// <summary>Dónde cae cada punto de la evolución, para la caja al pasar.</summary>
    private static (int X, int Y)[] centroPuntosEst = [];
    private const int BAJO_MAZO_EST = -800, BAJO_PUNTO_EST = -900;

    private const int ALTO_TARJETAS = 112, ALTO_EVOLUCION = 250, ALTO_ESCALERA = 250, ALTO_FILA_MAZO = 34;
    private const int RADIO = 10;

    // ── Colores ───────────────────────────────────────────────────────────
    private static readonly uint TARJETA = Argb(255, 16, 23, 40), BORDE = Argb(255, 36, 48, 74);
    private static readonly uint BLANCO = Rgb(241, 245, 249), APAGADO = Rgb(148, 163, 184), TENUE = Rgb(100, 116, 139), CEJA = Rgb(125, 211, 252);
    private static uint Argb(int a, int r, int g, int b) => (uint)((a << 24) | (r << 16) | (g << 8) | b);
    /// <summary>Verde de 55 % para arriba, ámbar de 45 a 55, rojo por debajo: lo que dice «vas bien» sin leer el número.</summary>
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
    [DllImport("gdiplus.dll")] private static extern int GdipClosePathFigure(IntPtr camino);
    [DllImport("gdiplus.dll")] private static extern int GdipAddPathLineI(IntPtr camino, int x1, int y1, int x2, int y2);
    [DllImport("gdiplus.dll")] private static extern int GdipFillPath(IntPtr g, IntPtr brocha, IntPtr camino);
    [DllImport("gdiplus.dll")] private static extern int GdipDrawPath(IntPtr g, IntPtr pluma, IntPtr camino);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateLineBrushI(ref PuntoG p1, ref PuntoG p2, uint c1, uint c2, int envoltura, out IntPtr brocha);
    [DllImport("gdiplus.dll")] private static extern int GdipSetClipRectI(IntPtr g, int x, int y, int ancho, int alto, int modo);
    [DllImport("gdiplus.dll")] private static extern int GdipResetClip(IntPtr g);
    [DllImport("gdi32.dll")] private static extern int SetTextCharacterExtra(IntPtr hdc, int extra);

    private const int SUAVE = 4 /* SmoothingModeAntiAlias */, PIXEL = 2 /* UnitPixel */, REMATE_REDONDO = 2, UNION_REDONDA = 2, GUIONES = 1;

    /// <summary>Un lienzo GDI+ sobre el DC, con suavizado. Se borra con GdipDeleteGraphics ANTES de escribir con GDI encima.</summary>
    private static IntPtr Lienzo(IntPtr hdc)
    {
        IniciarGdiPlus();
        if (GdipCreateFromHDC(hdc, out var g) != 0 || g == IntPtr.Zero) return IntPtr.Zero;
        GdipSetSmoothingMode(g, SUAVE);
        return g;
    }

    private static IntPtr Pluma(uint argb, float ancho, bool redonda = true)
    {
        GdipCreatePen1(argb, ancho, PIXEL, out var p);
        if (redonda) { GdipSetPenStartCap(p, REMATE_REDONDO); GdipSetPenEndCap(p, REMATE_REDONDO); GdipSetPenLineJoin(p, UNION_REDONDA); }
        return p;
    }

    private static void Rectangulo(IntPtr g, int x, int y, int w, int h, int r, uint relleno, uint? borde = null)
    {
        if (w <= 0 || h <= 0) return;
        r = Math.Max(1, Math.Min(r, Math.Min(w, h) / 2));
        GdipCreatePath(0, out var c);
        var d = r * 2;
        GdipAddPathArcI(c, x, y, d, d, 180, 90);
        GdipAddPathArcI(c, x + w - d, y, d, d, 270, 90);
        GdipAddPathArcI(c, x + w - d, y + h - d, d, d, 0, 90);
        GdipAddPathArcI(c, x, y + h - d, d, d, 90, 90);
        GdipClosePathFigure(c);
        GdipCreateSolidFill(relleno, out var b);
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

    /// <summary>Texto con GDI, con su fuente de usar y tirar. `extra`: espacio entre letras (las cejas).</summary>
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

    private static int Medir(IntPtr hdc, string t, int tam, int peso, int extra = 0)
    {
        var f = CreateFont(tam, 0, 0, 0, peso, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        var anterior = SelectObject(hdc, f);
        if (extra != 0) SetTextCharacterExtra(hdc, extra);
        var r = new RECT();
        DrawText(hdc, t, -1, ref r, DT_CALCRECT | DT_SINGLELINE);
        if (extra != 0) SetTextCharacterExtra(hdc, 0);
        SelectObject(hdc, anterior); DeleteObject(f);
        return r.Right - r.Left;
    }

    /// <summary>La ceja de cada bloque: en mayúsculas, pequeña, espaciada y en celeste, como en la web; con su nota a la derecha.</summary>
    private static void Ceja(IntPtr hdc, string texto, string? nota, int x, int y, int w)
    {
        Escribir(hdc, texto.ToUpperInvariant(), x, y, w, 18, 13, 700, CEJA, DT_LEFT | DT_SINGLELINE | DT_VCENTER, 2);
        if (nota is { Length: > 0 })
        {
            // DT_CALCRECT no cuenta el espacio entre letras: se suma a mano.
            var ancho = Medir(hdc, texto.ToUpperInvariant(), 13, 700) + 2 * texto.Length;
            Escribir(hdc, nota, x + ancho + 12, y, w - ancho - 12, 18, 14, 400, TENUE, DT_LEFT | DT_SINGLELINE | DT_VCENTER | DT_END_ELLIPSIS);
        }
    }

    private static string Pct(double p) => $"{Math.Round(p * 100)}%";

    /// <summary>Un decimal con la coma donde se escribe con coma.</summary>
    private static string Decimal1(double v)
    {
        var s = v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        return Textos.Idioma is "es" or "de" or "fr" or "pt" or "it" ? s.Replace('.', ',') : s;
    }

    private static string NombreClase(string? clase) => clase is null ? "" : Textos.T("clase_" + clase.ToLowerInvariant());

    // ── La disposición: qué bloques y dónde ──────────────────────────────

    /// <summary>Los bloques del modo Stats, para Disponer. Izquierda: evolución y mazos; derecha: escalera y curva.</summary>
    private static void AnadirBloquesEstadisticas(List<Bloque> lista, Estadisticas e)
    {
        lista.Add(new Bloque("", false, false, ALTO_TARJETAS, Especial: "est_tarjetas", Col: 0));
        lista.Add(new Bloque("", false, false, ALTO_EVOLUCION, Especial: "est_evolucion", Col: 1));
        lista.Add(new Bloque("", false, false, 30 + Math.Max(1, e.Mazos.Length) * ALTO_FILA_MAZO, Especial: "est_mazos", Col: 1));
        if (e.Escalera.Length >= 2) lista.Add(new Bloque("", false, false, ALTO_ESCALERA, Especial: "est_escalera", Col: 2));
        lista.Add(new Bloque("", false, false, e.Curva.Length > 0 ? 30 + 170 + 8 + 2 * 32 : 30 + 56, Especial: "est_curva", Col: 2));
        rectPuntosEst = new RECT[e.Evolucion.Length];
        centroPuntosEst = new (int, int)[e.Evolucion.Length];
        rectMazosEst = new RECT[e.Mazos.Length];
    }

    /// <summary>Pinta un bloque del modo Stats en la zona (que ya va desplazada por la rueda).</summary>
    private static void PintarEstadistica(IntPtr zona, string especial, Estadisticas e, int y, int alto, int arriba, int bx, int bw)
    {
        switch (especial)
        {
            case "est_tarjetas": PintarTarjetas(zona, e, y, bx, bw); break;
            case "est_evolucion": PintarEvolucion(zona, e, y, alto, arriba, bx, bw); break;
            case "est_mazos": PintarMazos(zona, e, y, arriba, bx, bw); break;
            case "est_escalera": PintarEscalera(zona, e, y, alto, bx, bw); break;
            case "est_curva": PintarCurva(zona, e, y, alto, bx, bw); break;
        }
    }

    // ── Las cuatro tarjetas ───────────────────────────────────────────────

    private static void PintarTarjetas(IntPtr hdc, Estadisticas e, int y, int bx, int bw)
    {
        const int hueco = 14, alto = ALTO_TARJETAS - 8, pad = 16;
        var w = (bw - hueco * 3) / 4;
        int X(int i) => bx + i * (w + hueco);
        var total = e.Ganadas + e.Perdidas;
        var pct = total > 0 ? (double)e.Ganadas / total : 0;
        var hoyTotal = e.HoyGanadas + e.HoyPerdidas;
        var pctHoy = hoyTotal > 0 ? (double)e.HoyGanadas / hoyTotal : 0;

        // Las formas, primero y todas juntas.
        var g = Lienzo(hdc);
        if (g != IntPtr.Zero)
        {
            for (var i = 0; i < 4; i++) Rectangulo(g, X(i), y, w, alto, RADIO, TARJETA, BORDE);
            // El anillo del total.
            var lado = 58; var ax = X(0) + w - pad - lado; var ay = y + (alto - lado) / 2;
            var fondoAnillo = Pluma(Argb(255, 36, 48, 74), 7);
            GdipDrawArcI(g, fondoAnillo, ax, ay, lado, lado, 0, 360);
            GdipDeletePen(fondoAnillo);
            if (total > 0)
            {
                var (r, gg, b) = ColorPorcentaje(pct);
                var arco = Pluma(Argb(255, r, gg, b), 7);
                GdipDrawArcI(g, arco, ax, ay, lado, lado, -90, (float)(360 * Math.Max(0.004, pct)));
                GdipDeletePen(arco);
            }
            // La barra de hoy: lo ganado en verde y lo perdido en rojo, en proporción.
            if (hoyTotal > 0)
            {
                var barX = X(1) + pad; var barY = y + alto - pad - 6; var barW = w - pad * 2;
                Rectangulo(g, barX, barY, barW, 6, 3, Argb(255, 248, 113, 113));
                var verde = (int)Math.Round(barW * pctHoy);
                if (verde > 0) Rectangulo(g, barX, barY, verde, 6, 3, Argb(255, 52, 211, 153));
            }
            // Las últimas partidas, un punto cada una.
            var cabe = Math.Max(1, (w - pad * 2 + 3) / 11);
            var ultimas = e.Ultimas.Skip(Math.Max(0, e.Ultimas.Length - cabe)).ToArray();
            for (var i = 0; i < ultimas.Length; i++)
                Circulo(g, X(2) + pad + 4 + i * 11, y + alto - pad - 4, 4, ultimas[i] ? Argb(255, 52, 211, 153) : Argb(255, 248, 113, 113));
            // El emblema del rango: un rombo con el color de su clase.
            if (e.Clase is { } clase)
            {
                var (r, gg, b) = ColorClase(clase);
                var cx = X(3) + pad + 22; var cy = y + alto / 2 + 8;
                var rombo = new[] { new PuntoG { X = cx, Y = cy - 24 }, new PuntoG { X = cx + 20, Y = cy }, new PuntoG { X = cx, Y = cy + 24 }, new PuntoG { X = cx - 20, Y = cy } };
                var arribaG = new PuntoG { X = cx, Y = cy - 24 }; var abajoG = new PuntoG { X = cx, Y = cy + 24 };
                GdipCreateLineBrushI(ref arribaG, ref abajoG, Argb(255, Math.Min(255, r + 40), Math.Min(255, gg + 40), Math.Min(255, b + 40)), Argb(255, r * 6 / 10, gg * 6 / 10, b * 6 / 10), 0, out var degradado);
                GdipFillPolygonI(g, degradado, rombo, 4, 0);
                GdipDeleteBrush(degradado);
            }
            GdipDeleteGraphics(g);
        }

        // Los textos.
        const uint una = DT_LEFT | DT_SINGLELINE | DT_VCENTER;
        // 1 · Total
        Ceja(hdc, Textos.T("est_total"), null, X(0) + pad, y + 12, w - pad * 2);
        Escribir(hdc, $"{e.Ganadas} – {e.Perdidas}", X(0) + pad, y + 34, w - pad * 2 - 64, 40, 32, 700, BLANCO, una | DT_END_ELLIPSIS);
        Escribir(hdc, Textos.T("est_partidas", total), X(0) + pad, y + 74, w - pad * 2 - 64, 20, 14, 400, APAGADO, una);
        {
            var lado = 58; var ax = X(0) + w - pad - lado; var ay = y + (alto - lado) / 2;
            var (r, gg, b) = ColorPorcentaje(pct);
            Escribir(hdc, total > 0 ? Pct(pct) : "–", ax, ay, lado, lado, 16, 700, total > 0 ? Rgb(r, gg, b) : APAGADO, DT_CENTER | DT_SINGLELINE | DT_VCENTER);
        }
        // 2 · Hoy
        Ceja(hdc, Textos.T("est_hoy"), null, X(1) + pad, y + 12, w - pad * 2);
        Escribir(hdc, $"{e.HoyGanadas} – {e.HoyPerdidas}", X(1) + pad, y + 34, w - pad * 2, 40, 32, 700, BLANCO, una | DT_END_ELLIPSIS);
        if (hoyTotal > 0)
        {
            var (r, gg, b) = ColorPorcentaje(pctHoy);
            var ancho = Medir(hdc, $"{e.HoyGanadas} – {e.HoyPerdidas}", 32, 700);
            Escribir(hdc, Pct(pctHoy), X(1) + pad + ancho + 10, y + 46, w - pad * 2 - ancho - 10, 24, 16, 700, Rgb(r, gg, b), una);
        }
        else Escribir(hdc, Textos.T("est_hoy_nada"), X(1) + pad, y + 74, w - pad * 2, 20, 14, 400, APAGADO, una | DT_END_ELLIPSIS);
        // 3 · Racha
        Ceja(hdc, Textos.T("est_racha"), null, X(2) + pad, y + 12, w - pad * 2);
        if (e.RachaGane is { } gano)
        {
            var n = e.RachaN.ToString();
            Escribir(hdc, n, X(2) + pad, y + 34, w - pad * 2, 40, 32, 700, gano ? Rgb(52, 211, 153) : Rgb(248, 113, 113), una);
            var ancho = Medir(hdc, n, 32, 700);
            var etiqueta = Textos.T(gano ? (e.RachaN == 1 ? "est_racha_v1" : "est_racha_vn") : (e.RachaN == 1 ? "est_racha_d1" : "est_racha_dn"));
            Escribir(hdc, etiqueta, X(2) + pad + ancho + 10, y + 46, w - pad * 2 - ancho - 10, 22, 15, 600, APAGADO, una | DT_END_ELLIPSIS);
        }
        else Escribir(hdc, "–", X(2) + pad, y + 34, w - pad * 2, 40, 32, 700, APAGADO, una);
        // 4 · Rango
        Ceja(hdc, Textos.T("est_rango"), null, X(3) + pad, y + 12, w - pad * 2);
        if (e.Clase is { } c)
        {
            var cx = X(3) + pad + 22; var cy = y + alto / 2 + 8;
            if (c != "Mythic" && e.Nivel is { } nv) Escribir(hdc, nv.ToString(), cx - 20, cy - 12, 40, 24, 17, 800, Rgb(15, 23, 42), DT_CENTER | DT_SINGLELINE | DT_VCENTER);
            var tx = X(3) + pad + 54;
            Escribir(hdc, NombreClase(c), tx, y + 38, X(3) + w - pad - tx, 30, 24, 700, BLANCO, una | DT_END_ELLIPSIS);
            if (c != "Mythic" && e.Nivel is { } nivel) Escribir(hdc, Textos.T("est_nivel", nivel), tx, y + 68, X(3) + w - pad - tx, 20, 14, 400, APAGADO, una);
        }
        else Escribir(hdc, Textos.T("est_sin_rango"), X(3) + pad, y + 40, w - pad * 2, 30, 20, 600, APAGADO, una | DT_END_ELLIPSIS);
    }

    // ── La evolución ──────────────────────────────────────────────────────

    private static void PintarEvolucion(IntPtr hdc, Estadisticas e, int y, int alto, int arriba, int bx, int bw)
    {
        Ceja(hdc, Textos.T("est_evolucion"), Textos.T(e.PorDia ? "est_evolucion_dia" : "est_evolucion_partida"), bx, y, bw);
        var cy0 = y + 28; var ch = alto - 34;
        // El área de la gráfica, dentro de su tarjeta.
        int gx = bx + 48, gy = cy0 + 16, gw = bw - 48 - 22, gh = ch - 16 - 30;
        var puntos = e.Evolucion;
        var valores = puntos.Select(p => p.Valor).Append(0).ToArray();
        var max = Math.Max(1, valores.Max()); var min = Math.Min(-1, valores.Min());
        // Un poco de aire arriba y abajo, y números redondos.
        var paso = Math.Max(1, (int)Math.Ceiling((max - min) / 4.0));
        max = (int)Math.Ceiling(max / (double)paso) * paso; min = (int)Math.Floor(min / (double)paso) * paso;
        int Y(double v) => gy + (int)Math.Round((max - v) * gh / (double)(max - min));
        int X(int i) => puntos.Length <= 1 ? gx + gw / 2 : gx + (int)Math.Round(i * gw / (double)(puntos.Length - 1));
        var cero = Y(0);
        var pts = puntos.Select((p, i) => new PuntoG { X = X(i), Y = Y(p.Valor) }).ToArray();

        var g = Lienzo(hdc);
        if (g != IntPtr.Zero)
        {
            Rectangulo(g, bx, cy0, bw, ch, RADIO, TARJETA, BORDE);
            // Las rayas de fondo.
            var raya = Pluma(Argb(255, 30, 41, 64), 1, false);
            for (var v = min; v <= max; v += paso) GdipDrawLineI(g, raya, gx, Y(v), gx + gw, Y(v));
            GdipDeletePen(raya);
            if (pts.Length >= 2)
            {
                // El área entre la línea y el cero: verde por encima, roja por debajo.
                var area = pts.Prepend(new PuntoG { X = pts[0].X, Y = cero }).Append(new PuntoG { X = pts[^1].X, Y = cero }).ToArray();
                GdipSetClipRectI(g, gx - 2, gy - 4, gw + 4, cero - gy + 4, 0);
                var arribaG = new PuntoG { X = 0, Y = gy }; var ceroG = new PuntoG { X = 0, Y = cero + 1 };
                GdipCreateLineBrushI(ref arribaG, ref ceroG, Argb(110, 52, 211, 153), Argb(10, 52, 211, 153), 0, out var verde);
                GdipFillPolygonI(g, verde, area, area.Length, 0); GdipDeleteBrush(verde);
                GdipSetClipRectI(g, gx - 2, cero, gw + 4, gy + gh - cero + 4, 0);
                var ceroG2 = new PuntoG { X = 0, Y = cero - 1 }; var abajoG = new PuntoG { X = 0, Y = gy + gh };
                GdipCreateLineBrushI(ref ceroG2, ref abajoG, Argb(10, 248, 113, 113), Argb(110, 248, 113, 113), 0, out var rojo);
                GdipFillPolygonI(g, rojo, area, area.Length, 0); GdipDeleteBrush(rojo);
                GdipResetClip(g);
            }
            // El cero, en guiones.
            var lineaCero = Pluma(Argb(255, 100, 116, 139), 1, false);
            GdipSetPenDashStyle(lineaCero, GUIONES);
            GdipDrawLineI(g, lineaCero, gx, cero, gx + gw, cero);
            GdipDeletePen(lineaCero);
            // La línea y sus puntos.
            if (pts.Length >= 2)
            {
                var linea = Pluma(Argb(255, 56, 189, 248), 2.5f);
                GdipDrawLinesI(g, linea, pts, pts.Length);
                GdipDeletePen(linea);
            }
            var bajo = bajoRaton <= BAJO_PUNTO_EST && BAJO_PUNTO_EST - bajoRaton < pts.Length ? BAJO_PUNTO_EST - bajoRaton : -1;
            var radio = pts.Length > 30 ? 2 : 3;
            for (var i = 0; i < pts.Length; i++)
            {
                if (i == bajo) { Circulo(g, pts[i].X, pts[i].Y, 7, Argb(255, 241, 245, 249)); Circulo(g, pts[i].X, pts[i].Y, 4, Argb(255, 56, 189, 248)); }
                else Circulo(g, pts[i].X, pts[i].Y, radio, Argb(255, 125, 211, 252));
            }
            GdipDeleteGraphics(g);
        }
        // Dónde está cada punto en la ventana: una franja vertical, que es fácil de coger.
        for (var i = 0; i < pts.Length; i++)
        {
            var medio = pts.Length <= 1 ? gw : gw / (pts.Length - 1);
            rectPuntosEst[i] = new RECT { Left = pts[i].X - Math.Max(4, medio / 2), Top = arriba + gy - 6, Right = pts[i].X + Math.Max(4, medio / 2), Bottom = arriba + gy + gh + 6 };
            centroPuntosEst[i] = (pts[i].X, arriba + pts[i].Y);
        }
        // Los números del eje y las etiquetas de abajo.
        foreach (var v in new[] { max, 0, min }.Distinct())
            Escribir(hdc, v > 0 ? $"+{v}" : $"{v}", bx + 4, Y(v) - 9, 38, 18, 13, 500, TENUE, DT_RIGHT | DT_SINGLELINE | DT_VCENTER);
        if (puntos.Length > 0)
        {
            var marcas = puntos.Length <= 2 ? new[] { 0, puntos.Length - 1 } : new[] { 0, puntos.Length / 2, puntos.Length - 1 };
            foreach (var i in marcas.Distinct())
            {
                var formato = i == 0 ? DT_LEFT : i == puntos.Length - 1 ? DT_RIGHT : DT_CENTER;
                var lx = i == 0 ? X(i) - 4 : i == puntos.Length - 1 ? X(i) - 116 : X(i) - 60;
                Escribir(hdc, puntos[i].Corta, lx, gy + gh + 8, 120, 18, 13, 500, TENUE, formato | DT_SINGLELINE | DT_VCENTER);
            }
            // El último valor, junto a su punto.
            var ultimo = puntos[^1].Valor;
            var (r, gg, b) = ultimo >= 0 ? (52, 211, 153) : (248, 113, 113);
            Escribir(hdc, ultimo > 0 ? $"+{ultimo}" : $"{ultimo}", Math.Min(X(puntos.Length - 1) - 50, bx + bw - 60), Y(ultimo) - 26, 56, 18, 14, 700, Rgb(r, gg, b), DT_RIGHT | DT_SINGLELINE | DT_VCENTER);
        }
    }

    // ── Tus mazos ─────────────────────────────────────────────────────────

    private static void PintarMazos(IntPtr hdc, Estadisticas e, int y, int arriba, int bx, int bw)
    {
        Ceja(hdc, Textos.T("est_mazos"), Textos.T("est_mazos_nota"), bx, y, bw);
        var y0 = y + 30;
        const int anchoBarra = 88, anchoPct = 46, anchoMarcador = 68;
        var g = Lienzo(hdc);
        for (var i = 0; i < e.Mazos.Length; i++)
        {
            var m = e.Mazos[i];
            var fy = y0 + i * ALTO_FILA_MAZO;
            rectMazosEst[i] = new RECT { Left = bx, Top = arriba + fy, Right = bx + bw, Bottom = arriba + fy + ALTO_FILA_MAZO - 4 };
            if (g == IntPtr.Zero) continue;
            var sobre = bajoRaton == BAJO_MAZO_EST - i;
            if (sobre || m.Actual) Rectangulo(g, bx, fy, bw, ALTO_FILA_MAZO - 4, 8, sobre ? Argb(255, 26, 34, 54) : TARJETA, m.Actual ? Argb(255, 120, 90, 20) : null);
            var jugadas = m.Ganadas + m.Perdidas;
            var p = jugadas > 0 ? (double)m.Ganadas / jugadas : 0;
            var bxBarra = bx + bw - 12 - anchoPct - anchoBarra;
            var byBarra = fy + (ALTO_FILA_MAZO - 4) / 2 - 3;
            Rectangulo(g, bxBarra, byBarra, anchoBarra, 6, 3, Argb(255, 36, 48, 74));
            var (r, gg, b) = ColorPorcentaje(p);
            var lleno = (int)Math.Round(anchoBarra * p);
            if (lleno > 0) Rectangulo(g, bxBarra, byBarra, Math.Max(6, lleno), 6, 3, Argb(255, r, gg, b));
            if (m.Actual) Circulo(g, bx + 12, fy + (ALTO_FILA_MAZO - 4) / 2, 4, Argb(255, 251, 191, 36));
        }
        if (g != IntPtr.Zero) GdipDeleteGraphics(g);
        if (e.Mazos.Length == 0) { Escribir(hdc, Textos.T("est_mazos_nada"), bx, y0, bw, 24, 15, 400, APAGADO, DT_LEFT | DT_SINGLELINE | DT_VCENTER); return; }
        for (var i = 0; i < e.Mazos.Length; i++)
        {
            var m = e.Mazos[i];
            var fy = y0 + i * ALTO_FILA_MAZO; var fh = ALTO_FILA_MAZO - 4;
            var jugadas = m.Ganadas + m.Perdidas;
            var p = jugadas > 0 ? (double)m.Ganadas / jugadas : 0;
            var (r, gg, b) = ColorPorcentaje(p);
            var derecha = bx + bw - 12;
            Escribir(hdc, Pct(p), derecha - anchoPct, fy, anchoPct, fh, 15, 700, Rgb(r, gg, b), DT_RIGHT | DT_SINGLELINE | DT_VCENTER);
            var xMarcador = derecha - anchoPct - anchoBarra - 12 - anchoMarcador;
            Escribir(hdc, $"{m.Ganadas}–{m.Perdidas}", xMarcador, fy, anchoMarcador, fh, 14, 500, APAGADO, DT_RIGHT | DT_SINGLELINE | DT_VCENTER);
            var xNombre = bx + 26;
            var etiqueta = m.Actual ? "  " + Textos.T("est_en_uso") : "";
            var anchoEtiqueta = etiqueta.Length > 0 ? Medir(hdc, etiqueta.ToUpperInvariant(), 12, 700) + etiqueta.Length + 6 : 0;
            var anchoNombre = Math.Max(20, xMarcador - 10 - xNombre - anchoEtiqueta);
            var sobre = bajoRaton == BAJO_MAZO_EST - i;
            Escribir(hdc, m.Nombre, xNombre, fy, anchoNombre, fh, 15, 600, sobre ? Rgb(255, 255, 255) : m.Ruta is not null ? Rgb(186, 230, 253) : BLANCO, DT_LEFT | DT_SINGLELINE | DT_VCENTER | DT_END_ELLIPSIS);
            if (etiqueta.Length > 0)
            {
                var usado = Math.Min(anchoNombre, Medir(hdc, m.Nombre, 15, 600));
                Escribir(hdc, etiqueta.ToUpperInvariant(), xNombre + usado, fy, anchoEtiqueta + 10, fh, 12, 700, Rgb(251, 191, 36), DT_LEFT | DT_SINGLELINE | DT_VCENTER, 1);
            }
        }
    }

    // ── La escalera de rango ──────────────────────────────────────────────

    private static void PintarEscalera(IntPtr hdc, Estadisticas e, int y, int alto, int bx, int bw)
    {
        var peldanos = e.Escalera;
        Ceja(hdc, Textos.T("est_escalera"), Textos.T("est_escalera_nota", peldanos.Length), bx, y, bw);
        var cy0 = y + 28; var ch = alto - 34;
        int gx = bx + 92, gy = cy0 + 16, gw = bw - 92 - 18, gh = ch - 32;
        // El tramo: de nivel a nivel (seis escalones), con uno de aire.
        var bajoPos = (int)Math.Floor((peldanos.Min(p => p.Posicion) - 1) / 6.0) * 6;
        var altoPos = (int)Math.Ceiling((peldanos.Max(p => p.Posicion) + 1) / 6.0) * 6;
        if (altoPos - bajoPos < 12) altoPos = bajoPos + 12;
        int Y(double pos) => gy + (int)Math.Round((altoPos - pos) * gh / (double)(altoPos - bajoPos));
        int X(int i) => gx + (int)Math.Round(i * gw / (double)Math.Max(1, peldanos.Length - 1));
        string ClaseDe(int pos) => pos >= 120 ? "Mythic" : new[] { "Bronze", "Silver", "Gold", "Platinum", "Diamond" }[Math.Clamp(pos / 24, 0, 4)];
        int NivelDe(int pos) => 4 - (pos % 24) / 6;

        var g = Lienzo(hdc);
        if (g != IntPtr.Zero)
        {
            Rectangulo(g, bx, cy0, bw, ch, RADIO, TARJETA, BORDE);
            // Una franja por nivel, del color de su clase, muy suave; más marcada donde cambia de clase.
            for (var b = bajoPos; b < altoPos; b += 6)
            {
                var (r, gg, bb) = ColorClase(ClaseDe(b));
                var franja = Argb((b / 6) % 2 == 0 ? 16 : 26, r, gg, bb);
                GdipCreateSolidFill(franja, out var br);
                var y1 = Y(b + 6); var y2 = Y(b);
                GdipFillPolygonI(g, br, new[] { new PuntoG { X = gx, Y = y1 }, new PuntoG { X = gx + gw, Y = y1 }, new PuntoG { X = gx + gw, Y = y2 }, new PuntoG { X = gx, Y = y2 } }, 4, 0);
                GdipDeleteBrush(br);
                if (b % 24 == 0)
                {
                    var p = Pluma(Argb(120, r, gg, bb), 1, false);
                    GdipDrawLineI(g, p, gx, Y(b), gx + gw, Y(b));
                    GdipDeletePen(p);
                }
            }
            // La escalera: plano y luego el salto, como se mueve en Arena.
            var (cr, cg, cb) = ColorClase(peldanos[^1].Clase);
            var linea = Pluma(Argb(255, cr, cg, cb), 2.5f);
            var camino = new List<PuntoG> { new() { X = X(0), Y = Y(peldanos[0].Posicion) } };
            for (var i = 1; i < peldanos.Length; i++)
            {
                camino.Add(new PuntoG { X = X(i), Y = Y(peldanos[i - 1].Posicion) });
                camino.Add(new PuntoG { X = X(i), Y = Y(peldanos[i].Posicion) });
            }
            GdipDrawLinesI(g, linea, camino.ToArray(), camino.Count);
            GdipDeletePen(linea);
            for (var i = 0; i < peldanos.Length; i++)
                Circulo(g, X(i), Y(peldanos[i].Posicion), 3, peldanos[i].Gane ? Argb(255, 52, 211, 153) : Argb(255, 248, 113, 113));
            GdipDeleteGraphics(g);
        }
        // El nombre de cada nivel, a la izquierda de su franja.
        for (var b = bajoPos; b < altoPos; b += 6)
        {
            var clase = ClaseDe(b);
            var nombre = clase == "Mythic" ? NombreClase(clase) : $"{NombreClase(clase)} {NivelDe(b)}";
            var (r, gg, bb) = ColorClase(clase);
            var y1 = Y(b + 6); var y2 = Y(b);
            if (y2 - y1 >= 12) Escribir(hdc, nombre, bx + 10, y1, 78, y2 - y1, 12, 600, Rgb(r * 8 / 10 + 30, gg * 8 / 10 + 30, bb * 8 / 10 + 30), DT_LEFT | DT_SINGLELINE | DT_VCENTER | DT_END_ELLIPSIS);
        }
    }

    // ── La curva de maná ──────────────────────────────────────────────────

    private static void PintarCurva(IntPtr hdc, Estadisticas e, int y, int alto, int bx, int bw)
    {
        Ceja(hdc, Textos.T("est_curva"), e.MazoNombre, bx, y, bw);
        var cy0 = y + 28;
        if (e.Curva.Length == 0)
        {
            var g0 = Lienzo(hdc);
            if (g0 != IntPtr.Zero) { Rectangulo(g0, bx, cy0, bw, alto - 34, RADIO, TARJETA, BORDE); GdipDeleteGraphics(g0); }
            Escribir(hdc, Textos.T(e.MazoNombre is null ? "est_sin_mazo" : "est_mazo_no_subido"), bx + 16, cy0 + 14, bw - 32, alto - 34 - 20, 15, 400, APAGADO, DT_LEFT | DT_WORDBREAK);
            return;
        }
        const int altoGrafica = 170;
        // Las barras: de 1 a 6+, y el 0 sólo si hay algo que cueste 0.
        var desde = e.Curva[0] > 0 ? 0 : 1;
        var n = e.Curva.Length - desde;
        int gx = bx + 18, gw = bw - 36, gy = cy0 + 30, gh = altoGrafica - 30 - 30;
        var maximo = Math.Max(1, e.Curva.Max());
        var hueco = 12; var anchoBarra = (gw - hueco * (n - 1)) / n;

        // Las etiquetas de abajo: tierras, coste medio, mano inicial, tierra a tiempo, fuentes por color.
        var fichas = new List<(string Texto, (int R, int G, int B)? Punto)>
        {
            (Textos.T("est_tierras", e.Tierras), null),
            (Textos.T("est_medio", Decimal1(e.Medio)), null),
            (Textos.T("est_mano", Pct(e.ManoBuena)), null),
            (Textos.T("est_t3", Pct(e.TierraT3)), null),
        };
        foreach (var c in e.Colores.Where(c => c.Fuentes > 0))
            fichas.Add((Textos.T("est_fuentes", c.Fuentes), c.Color switch { "W" => (248, 231, 185), "U" => (96, 165, 250), "B" => (148, 136, 158), "R" => (248, 113, 113), "G" => (74, 222, 128), _ => (148, 163, 184) }));
        // Se colocan en filas, sin salirse del ancho.
        var colocadas = new List<(int X, int Y, int W, string Texto, (int R, int G, int B)? Punto)>();
        {
            var fx = bx; var fy = cy0 + altoGrafica + 8;
            foreach (var (t, punto) in fichas)
            {
                var w = Medir(hdc, t, 14, 500) + 22 + (punto is null ? 0 : 14);
                if (fx + w > bx + bw && fx > bx) { fx = bx; fy += 32; }
                colocadas.Add((fx, fy, w, t, punto));
                fx += w + 8;
            }
        }

        var g = Lienzo(hdc);
        if (g != IntPtr.Zero)
        {
            Rectangulo(g, bx, cy0, bw, altoGrafica, RADIO, TARJETA, BORDE);
            for (var i = 0; i < n; i++)
            {
                var v = e.Curva[desde + i];
                var x = gx + i * (anchoBarra + hueco);
                var h = v == 0 ? 0 : Math.Max(4, (int)Math.Round(gh * v / (double)maximo));
                if (h > 0)
                {
                    var tope = new PuntoG { X = 0, Y = gy + gh - h }; var suelo = new PuntoG { X = 0, Y = gy + gh };
                    GdipCreateLineBrushI(ref tope, ref suelo, Argb(255, 125, 211, 252), Argb(255, 37, 99, 235), 0, out var degradado);
                    GdipCreatePath(0, out var c);
                    // Redonda arriba y plana abajo, apoyada en el suelo.
                    var r = Math.Max(1, Math.Min(6, Math.Min(anchoBarra / 2, h / 2)));
                    GdipAddPathArcI(c, x, gy + gh - h, r * 2, r * 2, 180, 90);
                    GdipAddPathArcI(c, x + anchoBarra - r * 2, gy + gh - h, r * 2, r * 2, 270, 90);
                    GdipAddPathLineI(c, x + anchoBarra, gy + gh - h + r, x + anchoBarra, gy + gh);
                    GdipAddPathLineI(c, x + anchoBarra, gy + gh, x, gy + gh);
                    GdipClosePathFigure(c);
                    GdipFillPath(g, degradado, c);
                    GdipDeletePath(c); GdipDeleteBrush(degradado);
                }
            }
            var suelo2 = Pluma(Argb(255, 51, 65, 85), 1, false);
            GdipDrawLineI(g, suelo2, gx, gy + gh, gx + gw, gy + gh);
            GdipDeletePen(suelo2);
            foreach (var f in colocadas)
            {
                Rectangulo(g, f.X, f.Y, f.W, 26, 13, Argb(255, 26, 34, 54), Argb(255, 44, 58, 88));
                if (f.Punto is { } pc) Circulo(g, f.X + 15, f.Y + 13, 5, Argb(255, pc.R, pc.G, pc.B));
            }
            GdipDeleteGraphics(g);
        }
        for (var i = 0; i < n; i++)
        {
            var v = e.Curva[desde + i];
            var x = gx + i * (anchoBarra + hueco);
            var h = v == 0 ? 0 : Math.Max(4, (int)Math.Round(gh * v / (double)maximo));
            if (v > 0) Escribir(hdc, v.ToString(), x, gy + gh - h - 22, anchoBarra, 20, 14, 700, BLANCO, DT_CENTER | DT_SINGLELINE | DT_VCENTER);
            var coste = desde + i == e.Curva.Length - 1 ? $"{desde + i}+" : $"{desde + i}";
            Escribir(hdc, coste, x, gy + gh + 6, anchoBarra, 20, 14, 600, APAGADO, DT_CENTER | DT_SINGLELINE | DT_VCENTER);
        }
        foreach (var f in colocadas)
            Escribir(hdc, f.Texto, f.X + 11 + (f.Punto is null ? 0 : 14), f.Y, f.W - 22 - (f.Punto is null ? 0 : 14) + 4, 26, 14, 500, Rgb(203, 213, 225), DT_LEFT | DT_SINGLELINE | DT_VCENTER);
    }

    // ── Al pasar el ratón por un punto de la evolución ────────────────────

    private static void PintarAyudaEstadisticas(IntPtr hdc)
    {
        if (est is not { } e || bajoRaton > BAJO_PUNTO_EST || BAJO_PUNTO_EST - bajoRaton >= e.Evolucion.Length) return;
        var i = BAJO_PUNTO_EST - bajoRaton;
        var p = e.Evolucion[i];
        var texto = Textos.T("est_punto", p.Larga, p.Ganadas, p.Perdidas, p.Valor > 0 ? $"+{p.Valor}" : $"{p.Valor}");
        var ancho = Medir(hdc, texto, 14, 500) + 24;
        var (cx, cy) = centroPuntosEst[i];
        var x = Math.Clamp(cx - ancho / 2, MARGEN, ANCHO_PANEL - MARGEN - ancho);
        var yCaja = Math.Max(ALTO_CABECERA + 4, cy - 44);
        var g = Lienzo(hdc);
        if (g != IntPtr.Zero) { Rectangulo(g, x, yCaja, ancho, 30, 8, Argb(255, 30, 41, 59), Argb(255, 56, 189, 248)); GdipDeleteGraphics(g); }
        Escribir(hdc, texto, x, yCaja, ancho, 30, 14, 500, BLANCO, DT_CENTER | DT_SINGLELINE | DT_VCENTER);
    }
}
