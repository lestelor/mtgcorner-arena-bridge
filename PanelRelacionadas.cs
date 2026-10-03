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
/// Así que la ventana sale EN SEGUIDA: a la izquierda la carta de la que se
/// parte, arriba las tres pestañas y a la derecha lo de la pestaña elegida. Lo
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
    /// una pestaña ya con las imágenes en disco (vacía si no hay nada; null si
    /// falló); <paramref name="Vacio"/>, el texto de «no hay»; <paramref name="AbrirWeb"/>,
    /// qué hacer al pulsar el mensaje de error.
    /// </summary>
    public sealed record Relacionadas(
        string Nombre, string? Imagen, string? Ruta, string Pestana,
        Func<string, Task<IReadOnlyList<Seccion>?>> Cargar,
        Func<string, string> Vacio, Action<string>? AbrirWeb);

    public static readonly string[] PESTANAS = ["similares", "combos", "sinergias"];
    private static readonly string[] ClavePestana = ["col_boton_similares", "col_boton_combos", "col_boton_sinergias"];

    private static Relacionadas? rel;
    private static string pestanaRel = "similares";
    /// <summary>Lo ya traído por pestaña. Sin clave: no ha llegado; null: falló.</summary>
    private static readonly Dictionary<string, IReadOnlyList<Seccion>?> contenidoRel = new();
    private static readonly HashSet<string> cargandoRel = new();
    /// <summary>Lo más alto que ha sido la ventana: al cambiar de pestaña no encoge (daba saltos).</summary>
    private static int altoRel;

    private static readonly RECT[] rectPestanas = new RECT[3];
    private static RECT rectOrigen, rectMensaje;
    private const int ALTO_PESTANAS = 46, BAJO_PESTANA = -8000, BAJO_ORIGEN = -8100, BAJO_MENSAJE = -8200;
    /// <summary>Mensaje propio para «rehaz y recoloca»: lo manda el hilo que trae una pestaña.</summary>
    private const uint WM_REHACER = 0x8001;

    /// <summary>Dónde empieza la rejilla: a la derecha de la carta y por debajo de las pestañas.</summary>
    private static int IzquierdaContenido => rel is null ? MARGEN : MARGEN + anchoCarta + AIRE * 2;
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
        texto = null;
        titulo = r.Nombre;
        pestanaRel = PESTANAS.Contains(r.Pestana) ? r.Pestana : PESTANAS[0];
        bajoRaton = -1;
        rectOrigen = rectMensaje = default;
        for (var k = 0; k < rectPestanas.Length; k++) rectPestanas[k] = default;
        DisponerRel();
        hilo = new Thread(Correr) { IsBackground = true, Name = "panel" };
        hilo.Start();
        CargarPestana(pestanaRel);
    }

    /// <summary>La geometría con lo que haya de la pestaña elegida: cartas tan grandes como quepan en Arena.</summary>
    private static void DisponerRel()
    {
        IReadOnlyList<Seccion> secciones;
        lock (contenidoRel) secciones = contenidoRel.TryGetValue(pestanaRel, out var s) && s is not null ? s : [];
        var (anchoArena, altoArena) = MedidasDeArena();
        foreach (var ancho in ANCHOS)
        {
            (anchoCarta, altoCarta) = (ancho, ancho * 680 / 488);
            var (h, a) = Disponer(secciones);
            // Nunca más bajo que la carta de la izquierda con su nombre debajo.
            var minimo = ArribaContenido + altoCarta + ALTO_ROTULO + MARGEN + ALTO_PIE + MARGEN / 2;
            (huecos, alto) = (h, Math.Max(a, minimo));
            if (alto <= altoArena - 60 && ANCHO_PANEL <= anchoArena - 40) break;
        }
        alto = altoRel = Math.Min(Math.Max(alto, altoRel), Math.Max(alto, altoArena - 60));
    }

    /// <summary>
    /// Trae una pestaña fuera del hilo de la ventana. Al llegar, si es la que se
    /// ve, se rehace la ventana; y al llegar la primera se piden las otras dos.
    /// </summary>
    private static void CargarPestana(string p)
    {
        if (rel is not { } r) return;
        lock (contenidoRel) { if (contenidoRel.ContainsKey(p) || !cargandoRel.Add(p)) return; }
        _ = Task.Run(async () =>
        {
            IReadOnlyList<Seccion>? res;
            try { res = await r.Cargar(p); } catch { res = null; }
            lock (contenidoRel) { contenidoRel[p] = res; cargandoRel.Remove(p); }
            if (rel != r) return;
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
        if (Dentro(rectOrigen, x, y)) return BAJO_ORIGEN;
        if (Dentro(rectMensaje, x, y)) return BAJO_MENSAJE;
        return 0;
    }

    private static bool PulsableRelacionadas(int i) =>
        rel is { } r && ((i <= BAJO_PESTANA && i > BAJO_PESTANA - PESTANAS.Length && PESTANAS[BAJO_PESTANA - i] != pestanaRel)
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
        var x = MARGEN;
        var y = ALTO_CABECERA + 6;
        if (g != IntPtr.Zero) Rectangulo(g, MARGEN, y + ALTO_PESTANAS - 13, ANCHO_PANEL - MARGEN * 2, 1, 1, BORDE);
        for (var k = 0; k < PESTANAS.Length; k++)
        {
            var p = PESTANAS[k];
            int? cuantas;
            bool cargando;
            lock (contenidoRel)
            {
                cargando = cargandoRel.Contains(p);
                cuantas = contenidoRel.TryGetValue(p, out var s) && s is not null ? s.Sum(sec => p == "combos" ? 1 : sec.Cartas.Count) : null;
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

        // LA CARTA DE LA QUE SE PARTE, a la izquierda, con su nombre debajo.
        var arriba = ArribaContenido;
        rectOrigen = new RECT { Left = MARGEN, Top = arriba, Right = MARGEN + anchoCarta, Bottom = arriba + altoCarta };
        var img = r.Imagen is not null ? Imagen(r.Imagen) : IntPtr.Zero;
        if (img != IntPtr.Zero && g != IntPtr.Zero) GdipDrawImageRectI(g, img, MARGEN, arriba, anchoCarta, altoCarta);
        else if (g != IntPtr.Zero) Rectangulo(g, MARGEN, arriba, anchoCarta, altoCarta, 10, TARJETA, BORDE);
        Escribir(hdc, r.Nombre, MARGEN, arriba + altoCarta + 4, anchoCarta, ALTO_ROTULO, 15, 600, bajoRaton == BAJO_ORIGEN ? BLANCO : APAGADO, DT_CENTER | DT_SINGLELINE | DT_VCENTER | DT_END_ELLIPSIS);
        // La raya que separa la carta de lo suyo.
        var xRaya = MARGEN + anchoCarta + AIRE;
        if (g != IntPtr.Zero) Rectangulo(g, xRaya, arriba, 1, alto - arriba - ALTO_PIE - MARGEN, 1, BORDE);
        if (g != IntPtr.Zero) GdipDeleteGraphics(g);

        // SIN CARTAS EN LA PESTAÑA: cargando, no hay, o falló (y entonces lleva a la web).
        rectMensaje = default;
        bool hay, cargandoEsta;
        IReadOnlyList<Seccion>? esta;
        lock (contenidoRel) { hay = contenidoRel.TryGetValue(pestanaRel, out esta); cargandoEsta = cargandoRel.Contains(pestanaRel); }
        if (hay && esta is { Count: > 0 }) return;
        var mensaje = !hay || cargandoEsta ? Textos.T("panel_cargando") : esta is null ? Textos.T("panel_fallo_web") : r.Vacio(pestanaRel);
        var izq = IzquierdaContenido;
        var der = ANCHO_PANEL - MARGEN;
        var rm = new RECT { Left = izq, Top = arriba, Right = der, Bottom = arriba + 80 };
        if (hay && esta is null && r.AbrirWeb is not null) rectMensaje = rm;
        Escribir(hdc, mensaje, izq, arriba + 20, der - izq, 60, 18, 500,
            hay && esta is null ? (bajoRaton == BAJO_MENSAJE ? BLANCO : Rgb(125, 211, 252)) : APAGADO, DT_LEFT | DT_WORDBREAK);
    }
}
