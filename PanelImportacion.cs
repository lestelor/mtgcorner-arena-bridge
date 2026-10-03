namespace MtgCornerArenaBridge;

/// <summary>
/// «IMPORTAR»: ELEGIR LOS MAZOS ENCIMA DEL JUEGO.
///
/// Un registro de Arena trae TODOS los mazos de la cuenta, y uno que ya exista
/// en la web con el mismo nombre se reescribe entero, así que nada se guarda
/// sin que alguien marque qué quiere. Eso se hacía en la página de revisión del
/// navegador, y desde la columna del juego no se entendía: pulsabas «Importar»,
/// se abría la web y, si no marcabas nada allí, no pasaba nada. El usuario, el
/// 2026-10-03: «lo ideal es que saliese un diálogo preguntando qué mazos se
/// quieren importar, directamente en Arena».
///
/// Es ese diálogo: el mismo panel que el resumen y Stats, con una casilla por
/// mazo, la de la colección y el botón de guardar en el pie. Los textos y la
/// selección de salida vienen de /api/mtga-device/importacion, con LA MISMA
/// regla que la página (marcados los nuevos jugados en el último mes); aquí
/// sólo se pinta y se cuenta lo que cambia al pulsar.
///
/// LOS QUE NO HAN CAMBIADO NO SON FILAS. El residente sube cada vez que abres
/// Arena, y lo normal es que casi todos sean idénticos a lo guardado: en la web
/// salen apagados al final; aquí, en una línea con sus nombres, porque una
/// lista de cuarenta casillas de las que sólo dos se pueden marcar no se lee.
/// </summary>
internal static partial class PanelCartas
{
    /// <summary>Un mazo de la lectura: sus textos ya traducidos por la web y si va marcado de salida.</summary>
    public sealed record MazoImp(string ArenaId, string Nombre, string Estado, string Etiqueta, string Detalle, bool Marcado);

    /// <summary>
    /// Lo que pinta el diálogo y qué hacer con la elección. <paramref name="Guardar"/>
    /// recibe los mazos marcados y si va la colección, y dice si se guardó.
    /// </summary>
    public sealed record Importacion(
        string Intro, string Recientes, string Todos, string Ninguno,
        int Coleccion, string? ColeccionTexto, MazoImp[] Mazos,
        Func<string[], bool, Task<bool>> Guardar, Action AbrirWeb);

    private static Importacion? imp;
    /// <summary>Los que se pueden elegir (nuevos o cambiados) y los idénticos a lo guardado.</summary>
    private static MazoImp[] cambiadosImp = [], igualesImp = [];
    private static readonly HashSet<string> marcadosImp = [];
    private static bool coleccionImp;
    /// <summary>0 eligiendo, 1 guardando, 2 falló al guardar.</summary>
    private static volatile int estadoImp;

    // Dónde está cada cosa en la ventana, para el ratón (se rehacen al pintar).
    private static RECT[] rectMazosImp = [];
    private static readonly RECT[] rectAtajosImp = new RECT[3];
    private static RECT rectColeccionImp, rectGuardarImp, rectWebImp;

    private const int BAJO_IMP_GUARDAR = -5001, BAJO_IMP_WEB = -5002, BAJO_IMP_COLECCION = -5003, BAJO_IMP_ATAJO = -5010, BAJO_IMP_MAZO = -6000;
    private const int ALTO_FILA_IMP = 56, ALTO_BARRA_IMP = 42, ALTO_COLECCION_IMP = 44, LADO_CASILLA = 20;
    /// <summary>Con más mazos que estos, a dos columnas: cabe el doble sin rueda.</summary>
    private const int FILAS_UNA_COLUMNA = 6;

    private static readonly uint AMBAR = Argb(255, 251, 191, 36), SOBRE = Argb(255, 26, 34, 54);

    /// <summary>Lo pone MostrarTexto: la elección de salida es la de la web.</summary>
    private static void PrepararImportacion(Importacion? i)
    {
        imp = i;
        marcadosImp.Clear();
        estadoImp = 0;
        rectColeccionImp = rectGuardarImp = rectWebImp = default;
        for (var k = 0; k < rectAtajosImp.Length; k++) rectAtajosImp[k] = default;
        if (i is null) { cambiadosImp = igualesImp = []; rectMazosImp = []; return; }
        cambiadosImp = i.Mazos.Where(m => m.Estado != "igual").ToArray();
        igualesImp = i.Mazos.Where(m => m.Estado == "igual").ToArray();
        foreach (var m in cambiadosImp) if (m.Marcado) marcadosImp.Add(m.ArenaId);
        coleccionImp = i.Coleccion > 0;
        rectMazosImp = new RECT[cambiadosImp.Length];
    }

    private static string TextoIgualesImp() =>
        $"{Textos.T("imp_iguales", igualesImp.Length)}: {string.Join(", ", igualesImp.Select(m => m.Nombre))}";

    // ── La disposición ────────────────────────────────────────────────────

    private static void AnadirBloquesImportacion(List<Bloque> lista, Importacion i, int anchoTodo, Func<string, int, bool, int> medir)
    {
        if (i.Intro.Length > 0) lista.Add(new Bloque(i.Intro, false, false, medir(i.Intro, anchoTodo, false), Col: 0));
        if (i.Coleccion > 0) lista.Add(new Bloque("", false, false, ALTO_COLECCION_IMP, Especial: "imp_coleccion", Col: 0));
        if (cambiadosImp.Length > 0) lista.Add(new Bloque("", false, false, ALTO_BARRA_IMP, Especial: "imp_barra", Col: 0));
        // A dos columnas, la primera mitad a la izquierda: se lee de arriba
        // abajo y luego la otra, en el orden de la web (lo último jugado antes).
        var dos = cambiadosImp.Length > FILAS_UNA_COLUMNA;
        var mitad = (cambiadosImp.Length + 1) / 2;
        for (var k = 0; k < cambiadosImp.Length; k++)
            lista.Add(new Bloque("", false, false, ALTO_FILA_IMP, Especial: $"imp_mazo:{k}", Col: !dos ? 0 : k < mitad ? 1 : 2));
        if (igualesImp.Length > 0)
        {
            var t = TextoIgualesImp();
            lista.Add(new Bloque(t, false, false, medir(t, anchoTodo, false) + 6, Especial: "imp_iguales", Col: 0));
        }
    }

    // ── El pintado de cada bloque (en la zona de texto, que se desplaza) ──

    private static void PintarImportacion(IntPtr zona, string especial, Importacion i, int y, int alto, int arriba, int bx, int bw, int altoZona)
    {
        // El rectángulo en la VENTANA, recortado a lo que se ve: una fila que
        // la rueda ha metido bajo la cabecera no puede llevarse el clic de la X.
        RECT EnVentana(int l, int t, int r, int b)
        {
            int top = Math.Max(arriba, arriba + t), bottom = Math.Min(arriba + altoZona, arriba + b);
            return bottom > top ? new RECT { Left = l, Top = top, Right = r, Bottom = bottom } : default;
        }
        var visible = y + alto >= 0 && y < altoZona;
        var habil = estadoImp != 1;

        if (especial.StartsWith("imp_mazo:", StringComparison.Ordinal))
        {
            if (!int.TryParse(especial.AsSpan(9), out var k) || k < 0 || k >= cambiadosImp.Length) return;
            rectMazosImp[k] = EnVentana(bx - 8, y, bx + bw + 8, y + alto - 4);
            if (!visible) return;
            var m = cambiadosImp[k];
            var sobre = habil && bajoRaton == BAJO_IMP_MAZO - k;
            var marcado = marcadosImp.Contains(m.ArenaId);
            var g = Lienzo(zona);
            if (g != IntPtr.Zero)
            {
                if (sobre) Rectangulo(g, bx - 8, y, bw + 16, alto - 4, 8, SOBRE);
                Casilla(g, bx, y + 8, marcado, sobre);
            }
            var x0 = bx + LADO_CASILLA + 12;
            Escribir(zona, m.Nombre, x0, y + 4, bx + bw - x0, 24, 18, 600, marcado || sobre ? BLANCO : Rgb(203, 213, 225), DT_LEFT | DT_SINGLELINE | DT_VCENTER | DT_END_ELLIPSIS);
            // Debajo, la etiqueta (nuevo / ya existe y se reescribe) y lo de la web: formato, cartas y cuándo se jugó.
            var anchoEtiqueta = Math.Min(bx + bw - x0, Medir(zona, m.Etiqueta, 12, 700) + 16);
            var (fondo, letra) = m.Estado == "existe" ? (Argb(40, 251, 191, 36), Rgb(253, 230, 138)) : (Argb(30, 52, 211, 153), Rgb(110, 231, 183));
            if (g != IntPtr.Zero) Rectangulo(g, x0, y + 32, anchoEtiqueta, 19, 9, fondo);
            Escribir(zona, m.Etiqueta, x0 + 8, y + 32, anchoEtiqueta - 16, 19, 12, 700, letra, DT_LEFT | DT_SINGLELINE | DT_VCENTER | DT_END_ELLIPSIS);
            var xd = x0 + anchoEtiqueta + 10;
            Escribir(zona, m.Detalle, xd, y + 31, bx + bw - xd, 21, 15, 400, APAGADO, DT_LEFT | DT_SINGLELINE | DT_VCENTER | DT_END_ELLIPSIS);
            if (g != IntPtr.Zero) GdipDeleteGraphics(g);
            return;
        }

        switch (especial)
        {
            case "imp_coleccion":
            {
                rectColeccionImp = EnVentana(bx - 8, y, bx + bw + 8, y + alto - 6);
                if (!visible) return;
                var sobre = habil && bajoRaton == BAJO_IMP_COLECCION;
                var g = Lienzo(zona);
                if (g != IntPtr.Zero)
                {
                    Rectangulo(g, bx - 8, y, bw + 16, alto - 6, 10, sobre ? Argb(255, 30, 41, 64) : TARJETA, BORDE);
                    Casilla(g, bx + 4, y + (alto - 6 - LADO_CASILLA) / 2, coleccionImp, sobre);
                    GdipDeleteGraphics(g);
                }
                var x0 = bx + 4 + LADO_CASILLA + 12;
                Escribir(zona, i.ColeccionTexto ?? "", x0, y, bx + bw - x0, alto - 6, 16, 500, coleccionImp || sobre ? BLANCO : Rgb(203, 213, 225), DT_LEFT | DT_SINGLELINE | DT_VCENTER | DT_END_ELLIPSIS);
                return;
            }
            case "imp_barra":
            {
                // A la izquierda, cuántos hay y cuántos van marcados; a la
                // derecha, los atajos de la web: recientes, todos, ninguno.
                string[] atajos = [i.Recientes, i.Todos, i.Ninguno];
                var anchos = atajos.Select(a => Medir(zona, a, 14, 600) + 26).ToArray();
                var xa = bx + bw - (anchos.Sum() + 8 * (atajos.Length - 1));
                var g = visible ? Lienzo(zona) : IntPtr.Zero;
                for (var k = 0; k < atajos.Length; k++)
                {
                    rectAtajosImp[k] = EnVentana(xa, y + 8, xa + anchos[k], y + 36);
                    if (visible)
                    {
                        var sobre = habil && bajoRaton == BAJO_IMP_ATAJO - k;
                        if (g != IntPtr.Zero) Rectangulo(g, xa, y + 8, anchos[k], 28, 14, sobre ? Argb(255, 40, 52, 78) : TARJETA, BORDE);
                        Escribir(zona, atajos[k], xa, y + 8, anchos[k], 28, 14, 600, sobre ? BLANCO : APAGADO, DT_CENTER | DT_SINGLELINE | DT_VCENTER);
                    }
                    xa += anchos[k] + 8;
                }
                if (g != IntPtr.Zero) GdipDeleteGraphics(g);
                if (visible) Ceja(zona, Textos.T("imp_contador", cambiadosImp.Length, marcadosImp.Count), null, bx, y + 13, bw / 2);
                return;
            }
            case "imp_iguales":
                if (visible) Escribir(zona, TextoIgualesImp(), bx, y + 6, bw, alto - 6, TAM_TEXTO, 400, TENUE, DT_LEFT | DT_WORDBREAK);
                return;
        }
    }

    /// <summary>Una casilla redondeada: ámbar con su marca si va elegida (el accent-amber de la web).</summary>
    private static void Casilla(IntPtr g, int x, int y, bool marcada, bool sobre)
    {
        if (marcada)
        {
            Rectangulo(g, x, y, LADO_CASILLA, LADO_CASILLA, 5, sobre ? Argb(255, 252, 211, 77) : AMBAR);
            var p = Pluma(Argb(255, 9, 13, 24), 2.6f);
            GdipDrawLinesI(g, p, [new PuntoG { X = x + 5, Y = y + 10 }, new PuntoG { X = x + 9, Y = y + 14 }, new PuntoG { X = x + 15, Y = y + 6 }], 3);
            GdipDeletePen(p);
        }
        else Rectangulo(g, x, y, LADO_CASILLA, LADO_CASILLA, 5, Argb(255, 16, 23, 40), sobre ? AMBAR : Argb(255, 100, 116, 139));
    }

    // ── El pie: guardar, revisar en la web, y si algo falló ───────────────

    private static string TextoGuardarImp() =>
        estadoImp == 1 ? Textos.T("imp_guardando")
        : marcadosImp.Count > 0 ? Textos.T("imp_guardar_n", marcadosImp.Count)
        : coleccionImp && imp?.Coleccion > 0 ? Textos.T("imp_guardar_coleccion")
        : Textos.T("imp_guardar");

    private static void PintarPieImportacion(IntPtr hdc, int altoZona)
    {
        var rb = RectBotonCopiar();
        var arriba = rb.Top - 2; var alto2 = ALTO_BOTON + 4;
        var texto = TextoGuardarImp();
        var anchoGuardar = Math.Max(150, Medir(hdc, texto, 15, 700) + 36);
        rectGuardarImp = new RECT { Left = ANCHO_PANEL - MARGEN - anchoGuardar, Top = arriba, Right = ANCHO_PANEL - MARGEN, Bottom = arriba + alto2 };
        var web = Textos.T("imp_web");
        var anchoWeb = Medir(hdc, web, 15, 600) + 28;
        rectWebImp = new RECT { Left = rectGuardarImp.Left - 10 - anchoWeb, Top = arriba, Right = rectGuardarImp.Left - 10, Bottom = arriba + alto2 };

        var guardando = estadoImp == 1;
        var sobreGuardar = !guardando && bajoRaton == BAJO_IMP_GUARDAR;
        var sobreWeb = !guardando && bajoRaton == BAJO_IMP_WEB;
        var g = Lienzo(hdc);
        if (g != IntPtr.Zero)
        {
            Rectangulo(g, rectGuardarImp.Left, arriba, anchoGuardar, alto2, 8, guardando ? Argb(255, 71, 85, 105) : sobreGuardar ? Argb(255, 252, 211, 77) : AMBAR);
            Rectangulo(g, rectWebImp.Left, arriba, anchoWeb, alto2, 8, sobreWeb ? Argb(255, 40, 52, 78) : TARJETA, BORDE);
            GdipDeleteGraphics(g);
        }
        Escribir(hdc, texto, rectGuardarImp.Left, arriba, anchoGuardar, alto2, 15, 700, guardando ? Rgb(226, 232, 240) : Rgb(9, 13, 24), DT_CENTER | DT_SINGLELINE | DT_VCENTER);
        Escribir(hdc, web, rectWebImp.Left, arriba, anchoWeb, alto2, 15, 600, sobreWeb ? BLANCO : Rgb(203, 213, 225), DT_CENTER | DT_SINGLELINE | DT_VCENTER);

        // A la izquierda: el fallo si lo hubo, y si no, que hay más con la rueda.
        var aviso = estadoImp == 2 ? Textos.T("imp_fallo") : altoTexto > altoZona ? Textos.T("panel_rueda") : null;
        if (aviso is not null)
            Escribir(hdc, aviso, MARGEN, arriba, rectWebImp.Left - AIRE - MARGEN, alto2, 15, 500, estadoImp == 2 ? Rgb(248, 113, 113) : APAGADO, DT_LEFT | DT_SINGLELINE | DT_VCENTER | DT_END_ELLIPSIS);
    }

    // ── El ratón ──────────────────────────────────────────────────────────

    private static int QueHayEnImportacion(int x, int y)
    {
        if (imp is null) return 0;
        static bool Dentro(RECT r, int x, int y) => r.Right > 0 && x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;
        if (Dentro(rectGuardarImp, x, y)) return BAJO_IMP_GUARDAR;
        if (Dentro(rectWebImp, x, y)) return BAJO_IMP_WEB;
        if (Dentro(rectColeccionImp, x, y)) return BAJO_IMP_COLECCION;
        for (var k = 0; k < rectAtajosImp.Length; k++) if (Dentro(rectAtajosImp[k], x, y)) return BAJO_IMP_ATAJO - k;
        var rm = rectMazosImp;
        for (var k = 0; k < rm.Length; k++) if (Dentro(rm[k], x, y)) return BAJO_IMP_MAZO - k;
        return 0;
    }

    private static bool PulsableImportacion(int i) =>
        imp is not null && estadoImp != 1 && (i == BAJO_IMP_GUARDAR || i == BAJO_IMP_WEB || i == BAJO_IMP_COLECCION
            || (i <= BAJO_IMP_ATAJO && i > BAJO_IMP_ATAJO - rectAtajosImp.Length)
            || (i <= BAJO_IMP_MAZO && i > BAJO_IMP_MAZO - cambiadosImp.Length));

    private static bool PulsarImportacion(int i, IntPtr ventana)
    {
        if (imp is not { } im || !PulsableImportacion(i)) return false;
        if (i <= BAJO_IMP_MAZO)
        {
            var id = cambiadosImp[BAJO_IMP_MAZO - i].ArenaId;
            if (!marcadosImp.Remove(id)) marcadosImp.Add(id);
        }
        else if (i <= BAJO_IMP_ATAJO)
        {
            marcadosImp.Clear();
            switch (BAJO_IMP_ATAJO - i)
            {
                case 0: foreach (var m in cambiadosImp) if (m.Marcado) marcadosImp.Add(m.ArenaId); break;   // los recientes: la selección de salida
                case 1: foreach (var m in cambiadosImp) marcadosImp.Add(m.ArenaId); break;
            }
        }
        else if (i == BAJO_IMP_COLECCION) coleccionImp = !coleccionImp;
        else if (i == BAJO_IMP_WEB)
        {
            _ = Task.Run(() => { try { im.AbrirWeb(); } catch { /* lo cuenta quien lo montó */ } });
            PostMessage(ventana, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            return true;
        }
        else if (i == BAJO_IMP_GUARDAR)
        {
            // Se guarda fuera del hilo de la ventana (puede tardar: la colección
            // se traduce carta a carta en el servidor) y, mientras, el botón dice
            // «Guardando…» y nada más se puede pulsar. Bien: se cierra; mal: se
            // queda abierto con el aviso, para reintentar o ir a la web.
            estadoImp = 1;
            var elegidos = marcadosImp.ToArray();
            var conColeccion = coleccionImp && im.Coleccion > 0;
            _ = Task.Run(async () =>
            {
                bool bien;
                try { bien = await im.Guardar(elegidos, conColeccion); } catch { bien = false; }
                if (bien) PostMessage(ventana, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                else { estadoImp = 2; InvalidateRect(ventana, IntPtr.Zero, false); }
            });
        }
        InvalidateRect(ventana, IntPtr.Zero, false);
        return true;
    }
}
