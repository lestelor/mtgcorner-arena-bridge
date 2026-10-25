namespace MtgCornerArenaBridge;

/// <summary>
/// SIMILARES · COMBOS · SINERGIAS, EN UNA SOLA VENTANA.
///
/// Eran tres paneles, y al pulsar uno había que esperar a que bajara todo para
/// ver algo; si no había nada, salía un aviso aparte. El usuario, el
/// 2026-10-03: «mejor que al apretar salga ya la ventana con las similares (que
/// podrían estar precargadas) y que haya arriba un selector para escoger
/// similar/combos/sinergias, y que en la ventana salga la imagen de la carta;
/// si no hay combos o similares, que salga en la propia ventana».
///
/// Así que la ventana sale EN SEGUIDA, a la derecha de la columna: arriba, la
/// carta de la que se parte en miniatura (grande al pasar el ratón), su nombre
/// y las tres pestañas; debajo, lo de la pestaña elegida. Era una columna
/// entera de la ventana y la hacía muy ancha (el usuario, 2026-10-03). Lo
/// que aún no ha bajado se carga dentro, diciéndolo; y en cuanto llega la
/// primera pestaña se piden las otras dos, para que cambiar sea inmediato y
/// las pestañas digan cuántas hay. Si no hay nada, lo dice ahí mismo; si falla
/// la red, el mensaje lleva a la web.
///
/// Usa el mismo panel de cartas (PanelCartas): la rejilla, el ratón y el pie
/// son los suyos; esto sólo añade la columna de la carta, las pestañas y el
/// mensaje, y desplaza la rejilla a su derecha y por debajo.
/// </summary>
internal static partial class PanelCartas
{
    /// <summary>
    /// Lo que enseña la ventana. <paramref name="Cargar"/> trae las secciones de
    /// una pestaña en un formato (null: todos) ya con las imágenes en disco
    /// (vacía si no hay nada; null si falló); <paramref name="Vacio"/>, el texto
    /// de «no hay»; <paramref name="AbrirWeb"/>, qué hacer al pulsar el mensaje
    /// de error; <paramref name="Formato"/>, el de la partida, si se sabe.
    /// </summary>
    public sealed record Relacionadas(
        string Nombre, string? Imagen, string? Ruta, string Pestana,
        Func<string, string?, Task<IReadOnlyList<Seccion>?>> Cargar,
        Func<string, string> Vacio, Action<string>? AbrirWeb, string? Formato = null);

    public static readonly string[] PESTANAS = ["similares", "combos", "sinergias"];
    private static readonly string[] ClavePestana = ["col_boton_similares", "col_boton_combos", "col_boton_sinergias"];

    private static Relacionadas? rel;
    private static string pestanaRel = "similares";
    /// <summary>Lo ya traído por pestaña y formato (ver Clave). Sin clave: no ha llegado; null: falló.</summary>
    private static readonly Dictionary<string, IReadOnlyList<Seccion>?> contenidoRel = new();
    private static readonly HashSet<string> cargandoRel = new();

    /// <summary>
    /// EL FORMATO, ARRIBA A LA DERECHA (el usuario, 2026-10-12: «debería poder
    /// filtrar entre los tipos de juego, porque Sram, Senior Edificer no lo
    /// puedo jugar en Standard»; «en el menú de arriba»). Unos botones con los
    /// formatos de Arena y «Todos»; vale para las tres pestañas. Sale marcado el
    /// de la partida, si es uno de ellos; si no, el último elegido (Standard la
    /// primera vez): fuera de partida no se sabe qué juegas.
    /// </summary>
    private static readonly string?[] FORMATOS_REL = ["standard", "alchemy", "historic", "timeless", "brawl", null];
    private static string? formatoRel = "standard", ultimoFormato = "standard";
    private static readonly RECT[] rectFormatos = new RECT[FORMATOS_REL.Length];
    private const int BAJO_FORMATO = -8300;
    /// <summary>Con qué formato se abre: el de la partida si es uno de los botones; si no, el último elegido.</summary>
    internal static string? FormatoInicial(string? partida) => partida is { } f && FORMATOS_REL.Contains(f) ? f : ultimoFormato;

    /// <summary>Lo de una pestaña en el formato elegido: cada formato es otra búsqueda.</summary>
    private static string Clave(string pestana) => pestana + "|" + (formatoRel ?? "*");
    /// <summary>Lo más alto que ha sido la ventana: al cambiar de pestaña no encoge (daba saltos).</summary>
    private static int altoRel;

    private static readonly RECT[] rectPestanas = new RECT[3];
    private static RECT rectOrigen, rectMensaje;
    private const int ALTO_PESTANAS = 46, BAJO_PESTANA = -8000, BAJO_ORIGEN = -8100, BAJO_MENSAJE = -8200;
    /// <summary>La miniatura de la carta en la cabecera: del alto de la cabecera y las pestañas.</summary>
    private const int ANCHO_MINI_REL = 56, ALTO_MINI_REL = 78;
    /// <summary>Mensaje propio para «rehaz y recoloca»: lo manda el hilo que trae una pestaña.</summary>
    private const uint WM_REHACER = 0x8001;

    /// <summary>Dónde empieza la rejilla: por debajo de las pestañas.</summary>
    private static int IzquierdaContenido => MARGEN;
    private static int ArribaContenido => ALTO_CABECERA + (rel is null ? 0 : ALTO_PESTANAS) + MARGEN;

    /// <summary>
    /// Abre la ventana en la pestaña pedida. Sale al momento: si lo de esa
    /// pestaña ya está (precargado), se ve; si no, se carga dentro.
    /// </summary>
    public static void MostrarRelacionadas(Relacionadas r)
    {
        Cerrar();
        lock (contenidoRel) { contenidoRel.Clear(); cargandoRel.Clear(); }
        altoRel = 0;
        rel = r;
        formatoRel = FormatoInicial(r.Formato);
        texto = null;
        titulo = r.Nombre;
        pestanaRel = PESTANAS.Contains(r.Pestana) ? r.Pestana : PESTANAS[0];
        bajoRaton = -1;
        rectOrigen = rectMensaje = default;
        for (var k = 0; k < rectPestanas.Length; k++) rectPestanas[k] = default;
        for (var k = 0; k < rectFormatos.Length; k++) rectFormatos[k] = default;
        DisponerRel();
        hilo = new Thread(Correr) { IsBackground = true, Name = "panel" };
        hilo.Start();
        CargarPestana(pestanaRel);
    }

    /// <summary>La geometría con lo que haya de la pestaña elegida: cartas tan grandes como quepan en Arena.</summary>
    private static void DisponerRel()
    {
        IReadOnlyList<Seccion> secciones;
        lock (contenidoRel) secciones = contenidoRel.TryGetValue(Clave(pestanaRel), out var s) && s is not null ? s : [];
        var (anchoArena, altoArena) = MedidasDeArena();
        // El ancho que queda a la derecha de la columna abierta, que es donde va.
        var libre = anchoArena - 40;
        var arenaV = Columna.VentanaDeArena();
        if (Columna.EsquinaAbierta() is { } esquina && arenaV != IntPtr.Zero && GetWindowRect(arenaV, out var ra)) libre = ra.Right - 20 - (esquina.X + 14);
        foreach (var ancho in ANCHOS)
        {
            (anchoCarta, altoCarta) = (ancho, ancho * 680 / 488);
            var (h, a) = Disponer(secciones);
            // Con sitio para el mensaje si la pestaña no tiene cartas.
            var minimo = ArribaContenido + 110 + ALTO_PIE + MARGEN;
            (huecos, alto) = (h, Math.Max(a, minimo));
            if (alto <= altoArena - 60 && ANCHO_PANEL <= libre) break;
        }
        alto = altoRel = Math.Min(Math.Max(alto, altoRel), Math.Max(alto, altoArena - 60));
        // Las pestañas de similares, sin rueda (ver altoRejilla): miden lo que miden.
        desplazamientoRejilla = 0; altoRejilla = 0;
    }

    /// <summary>
    /// Trae una pestaña fuera del hilo de la ventana. Al llegar, si es la que se
    /// ve, se rehace la ventana; y al llegar la primera se piden las otras dos.
    /// </summary>
    private static void CargarPestana(string p)
    {
        if (rel is not { } r) return;
        var formato = formatoRel;
        var clave = Clave(p);
        lock (contenidoRel) { if (contenidoRel.ContainsKey(clave) || !cargandoRel.Add(clave)) return; }
        _ = Task.Run(async () =>
        {
            IReadOnlyList<Seccion>? res;
            try { res = await r.Cargar(p, formato); } catch { res = null; }
            lock (contenidoRel) { contenidoRel[clave] = res; cargandoRel.Remove(clave); }
            if (rel != r || formato != formatoRel) return;
            if (p == pestanaRel) DisponerRel();
            var v = ventana;
            if (v != IntPtr.Zero) PostMessage(v, WM_REHACER, IntPtr.Zero, IntPtr.Zero);
            foreach (var otra in PESTANAS) if (otra != p) CargarPestana(otra);
        });
    }

    // ── El ratón ──────────────────────────────────────────────────────────

    private static int QueHayEnRelacionadas(int x, int y)
    {
        if (rel is null) return 0;
        static bool Dentro(RECT r, int x, int y) => r.Right > 0 && x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;
        for (var k = 0; k < rectPestanas.Length; k++) if (Dentro(rectPestanas[k], x, y)) return BAJO_PESTANA - k;
        for (var k = 0; k < rectFormatos.Length; k++) if (Dentro(rectFormatos[k], x, y)) return BAJO_FORMATO - k;
        if (Dentro(rectOrigen, x, y)) return BAJO_ORIGEN;
        if (Dentro(rectMensaje, x, y)) return BAJO_MENSAJE;
        return 0;
    }

    private static bool PulsableRelacionadas(int i) =>
        rel is { } r && ((i <= BAJO_PESTANA && i > BAJO_PESTANA - PESTANAS.Length && PESTANAS[BAJO_PESTANA - i] != pestanaRel)
            || (i <= BAJO_FORMATO && i > BAJO_FORMATO - FORMATOS_REL.Length && FORMATOS_REL[BAJO_FORMATO - i] != formatoRel)
            || (i == BAJO_ORIGEN && r.Ruta is not null)
            || (i == BAJO_MENSAJE && r.AbrirWeb is not null));

    private static bool PulsarRelacionadas(int i, IntPtr ventanaPanel)
    {
        if (rel is not { } r || !PulsableRelacionadas(i)) return false;
        if (i <= BAJO_PESTANA && i > BAJO_PESTANA - PESTANAS.Length)
        {
            pestanaRel = PESTANAS[BAJO_PESTANA - i];
            bajoRaton = -1;
            DisponerRel();
            Recolocar();
            InvalidateRect(ventanaPanel, IntPtr.Zero, false);
            CargarPestana(pestanaRel);
        }
        else if (i <= BAJO_FORMATO && i > BAJO_FORMATO - FORMATOS_REL.Length)
        {
            // Otro formato: lo de las tres pestañas es otra búsqueda (lo ya traído de otros se guarda).
            formatoRel = ultimoFormato = FORMATOS_REL[BAJO_FORMATO - i];
            bajoRaton = -1;
            DisponerRel();
            Recolocar();
            InvalidateRect(ventanaPanel, IntPtr.Zero, false);
            CargarPestana(pestanaRel);
        }
        else if (i == BAJO_ORIGEN && r.Ruta is { } ruta && AlAbrir is { } abrir)
            _ = Task.Run(() => { try { abrir(ruta); } catch { /* lo cuenta quien lo montó */ } });
        else if (i == BAJO_MENSAJE && r.AbrirWeb is { } web)
        {
            var p = pestanaRel;
            _ = Task.Run(() => { try { web(p); } catch { /* lo cuenta quien lo montó */ } });
        }
        return true;
    }

    // ── El dibujo: pestañas, la carta y el mensaje ────────────────────────

    private static void PintarRelacionadas(IntPtr hdc)
    {
        if (rel is not { } r) return;
        var g = Lienzo(hdc);

        // LAS PESTAÑAS, arriba a lo ancho, CUADRADAS y con una raya azul debajo
        // de la elegida (el usuario, 2026-10-03: «no redondos, cuadrados y con
        // una raya debajo para lo seleccionado»), sobre una línea fina que
        // recorre la fila; con cuántas hay en cuanto se sabe.
        var x0 = MARGEN + ANCHO_MINI_REL + 14;
        var x = x0;
        var y = ALTO_CABECERA + 6;
        if (g != IntPtr.Zero) Rectangulo(g, x0, y + ALTO_PESTANAS - 13, ANCHO_PANEL - MARGEN - x0, 1, 1, BORDE);
        for (var k = 0; k < PESTANAS.Length; k++)
        {
            var p = PESTANAS[k];
            int? cuantas;
            bool cargando;
            lock (contenidoRel)
            {
                cargando = cargandoRel.Contains(Clave(p));
                cuantas = contenidoRel.TryGetValue(Clave(p), out var s) && s is not null ? s.Sum(sec => p == "combos" ? 1 : sec.Cartas.Count) : null;
            }
            var rotulo = Textos.T(ClavePestana[k]) + (cuantas is { } n ? $"  {n}" : cargando ? "  …" : "");
            var w = Medir(hdc, rotulo, 16, 700) + 32;
            var activa = p == pestanaRel;
            var sobre = bajoRaton == BAJO_PESTANA - k;
            rectPestanas[k] = new RECT { Left = x, Top = y, Right = x + w, Bottom = y + ALTO_PESTANAS - 12 };
            if (g != IntPtr.Zero)
            {
                if (activa || sobre) Rectangulo(g, x, y, w, ALTO_PESTANAS - 12, 1, activa ? Argb(255, 26, 34, 54) : Argb(255, 22, 30, 48));
                if (activa) Rectangulo(g, x, y + ALTO_PESTANAS - 15, w, 3, 1, Argb(255, 56, 189, 248));
            }
            Escribir(hdc, rotulo, x, y, w, ALTO_PESTANAS - 15, 16, 700, activa ? BLANCO : sobre ? Rgb(226, 232, 240) : APAGADO, DT_CENTER | DT_SINGLELINE | DT_VCENTER);
            x += w + 2;
        }

        // LOS FORMATOS, pegados a la derecha en la misma fila: el elegido, relleno en azul.
        var altoF = ALTO_PESTANAS - 20;
        var anchos = FORMATOS_REL.Select(f => Medir(hdc, Textos.T("fmt_" + (f ?? "todos")), 14, 600) + 22).ToArray();
        var xf = ANCHO_PANEL - MARGEN - anchos.Sum() - 6 * (anchos.Length - 1);
        // Si no caben al lado de las pestañas, no se ponen: mejor sin filtro que encima de ellas.
        var caben = xf > x + 16;
        for (var k = 0; k < FORMATOS_REL.Length; k++)
        {
            if (!caben) { rectFormatos[k] = default; continue; }
            var elegido = FORMATOS_REL[k] == formatoRel;
            var sobre = bajoRaton == BAJO_FORMATO - k;
            var yf = y + (ALTO_PESTANAS - 12 - altoF) / 2;
            rectFormatos[k] = new RECT { Left = xf, Top = yf, Right = xf + anchos[k], Bottom = yf + altoF };
            if (g != IntPtr.Zero)
                Rectangulo(g, xf, yf, anchos[k], altoF, altoF / 2,
                    elegido ? Argb(255, 56, 189, 248) : sobre ? Argb(255, 22, 30, 48) : Argb(0, 0, 0, 0), elegido ? null : BORDE);
            Escribir(hdc, Textos.T("fmt_" + (FORMATOS_REL[k] ?? "todos")), xf, yf, anchos[k], altoF, 14, 600,
                elegido ? Rgb(9, 13, 24) : sobre ? BLANCO : APAGADO, DT_CENTER | DT_SINGLELINE | DT_VCENTER);
            xf += anchos[k] + 6;
        }

        // LA CARTA DE LA QUE SE PARTE, en miniatura a la izquierda de la cabecera.
        var arriba = ArribaContenido;
        rectOrigen = new RECT { Left = MARGEN, Top = 8, Right = MARGEN + ANCHO_MINI_REL, Bottom = 8 + ALTO_MINI_REL };
        var img = r.Imagen is not null ? Imagen(r.Imagen) : IntPtr.Zero;
        if (img != IntPtr.Zero && g != IntPtr.Zero) GdipDrawImageRectI(g, img, MARGEN, 8, ANCHO_MINI_REL, ALTO_MINI_REL);
        else if (g != IntPtr.Zero) Rectangulo(g, MARGEN, 8, ANCHO_MINI_REL, ALTO_MINI_REL, 6, TARJETA, BORDE);
        if (g != IntPtr.Zero) GdipDeleteGraphics(g);

        // SIN CARTAS EN LA PESTAÑA: cargando, no hay, o falló (y entonces lleva a la web).
        rectMensaje = default;
        bool hay, cargandoEsta;
        IReadOnlyList<Seccion>? esta;
        lock (contenidoRel) { hay = contenidoRel.TryGetValue(Clave(pestanaRel), out esta); cargandoEsta = cargandoRel.Contains(Clave(pestanaRel)); }
        if (hay && esta is { Count: > 0 }) return;
        var mensaje = !hay || cargandoEsta ? Textos.T("panel_cargando") : esta is null ? Textos.T("panel_fallo_web") : r.Vacio(pestanaRel);
        var izq = IzquierdaContenido;
        var der = ANCHO_PANEL - MARGEN;
        var rm = new RECT { Left = izq, Top = arriba, Right = der, Bottom = arriba + 80 };
        if (hay && esta is null && r.AbrirWeb is not null) rectMensaje = rm;
        Escribir(hdc, mensaje, izq, arriba + 20, der - izq, 60, 18, 500,
            hay && esta is null ? (bajoRaton == BAJO_MENSAJE ? BLANCO : Rgb(125, 211, 252)) : APAGADO, DT_LEFT | DT_WORDBREAK);
    }

    /// <summary>
    /// LAS QUE YA TIENES: «×2» y el icono de la colección sobre la carta, a la
    /// izquierda y justo bajo la línea del título impreso (que ocupa el 8-9 %
    /// de arriba), la misma pastilla y el mismo sitio que en la web (CartaMini)
    /// para que se reconozca sin leer (el usuario, 2026-10-12: «que salga un 1x
    /// 2x si tenemos alguna en la colección de Arena, que no tape el título,
    /// debajo a la izquierda, como en la web»).
    /// </summary>
    private static void PintarCopias(IntPtr hdc, int copias, int x, int y, int ancho, int alto)
    {
        var texto = $"×{copias}";
        const int tam = 13, tamIcono = 12, aire = 6, entre = 4, altoP = 20;
        var w = aire + Medir(hdc, texto, tam, 700) + entre + tamIcono + aire;
        var px = x + Math.Max(5, ancho * 4 / 100);
        var py = y + alto * 11 / 100;
        var g = Lienzo(hdc);
        if (g != IntPtr.Zero)
        {
            Rectangulo(g, px, py, w, altoP, altoP / 2, Argb(204, 0, 0, 0), Argb(51, 255, 255, 255));
            GdipDeleteGraphics(g);
        }
        Escribir(hdc, texto, px + aire, py, w, altoP, tam, 700, BLANCO, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
        var icono = CreateFont(tamIcono, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe MDL2 Assets");
        var anterior = SelectObject(hdc, icono);
        SetTextColor(hdc, Rgb(125, 211, 252));
        var ri = new RECT { Left = px + w - aire - tamIcono, Top = py, Right = px + w - aire, Bottom = py + altoP };
        DrawText(hdc, "\uE8F1", -1, ref ri, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
        SelectObject(hdc, anterior);
        DeleteObject(icono);
    }

    /// <summary>Modo de taller: la carta grande sin el ratón encima (para fotografiarla).</summary>
    internal static bool ZoomDePrueba;

    /// <summary>
    /// Con el ratón en la miniatura, la carta grande debajo de ella. Se pinta
    /// AL FINAL de todo (ver Pintar): pintada con la cabecera, las cartas de la
    /// rejilla la tapaban.
    /// </summary>
    private static void PintarCartaGrande(IntPtr hdc, Relacionadas r)
    {
        if ((bajoRaton != BAJO_ORIGEN && !ZoomDePrueba) || r.Imagen is null) return;
        var img = Imagen(r.Imagen);
        var g = Lienzo(hdc);
        if (img == IntPtr.Zero || g == IntPtr.Zero) { if (g != IntPtr.Zero) GdipDeleteGraphics(g); return; }
        var alto2 = Math.Min(ALTO_VISTA, Math.Max(120, alto - (8 + ALTO_MINI_REL + 6) - ALTO_PIE - MARGEN));
        var ancho2 = alto2 * 488 / 680;
        GdipDrawImageRectI(g, img, MARGEN, 8 + ALTO_MINI_REL + 6, ancho2, alto2);
        GdipDeleteGraphics(g);
    }
}
