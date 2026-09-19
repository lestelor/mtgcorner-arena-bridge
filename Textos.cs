namespace MtgCornerArenaBridge;

/// <summary>
/// LO QUE ESTE PROGRAMA ESCRIBE, EN LOS OCHO IDIOMAS DEL SITIO.
///
/// EL IDIOMA LO MANDA LA WEB, NO WINDOWS. Al principio no hay más remedio que
/// adivinarlo —se mira el idioma del sistema, que es lo único que se sabe antes
/// de hablar con nadie—, pero en cuanto confirmas en el navegador, la web dice
/// en qué idioma lo has hecho (/api/mtga-device/estado) y el resto del proceso
/// —esta consola y la página de revisión— va en ése. Antes había dos reglas
/// distintas en el mismo proceso: la página de vincular salía en inglés y la de
/// revisión en el idioma de Windows, y se notaba.
///
/// SIN RECURSOS .resx NI CULTURAS DE .NET, a propósito: el ejecutable se publica
/// con InvariantGlobalization (un solo fichero, sin datos de ICU) y los satélites
/// de traducción habrían traído justo lo que se quiso quitar. Ocho columnas en
/// una tabla son más fáciles de leer y de mantener alineadas que ocho ficheros.
///
/// LOS MODOS DE PRUEBA (--probar-log, --probar-coleccion, --licencia) y el
/// diagnóstico siguen SÓLO EN INGLÉS: no son para quien importa su colección,
/// son para quien arregla esto, y sus líneas acaban pegadas en un informe.
/// </summary>
internal static class Textos
{
    /// <summary>El orden de las columnas de <see cref="Frases"/>.</summary>
    private static readonly string[] Idiomas = ["en", "es", "de", "fr", "pt", "it", "ja", "zh"];

    private static int columna;

    /// <summary>El idioma elegido, en el código corto del sitio ("es", "ja"…).</summary>
    public static string Idioma => Idiomas[columna];

    /// <summary>
    /// Fija el idioma por su código. Lo que no se reconoce no cambia nada: así
    /// una respuesta rara del servidor deja la conversación como estaba en vez
    /// de saltar al inglés a mitad.
    /// </summary>
    public static void Escoger(string? codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo)) return;
        var i = Array.IndexOf(Idiomas, codigo.Trim().ToLowerInvariant());
        if (i >= 0) columna = i;
    }

    /// <summary>
    /// El idioma de Windows, como PRIMERA suposición mientras no se sabe el del
    /// sitio. Con InvariantGlobalization la cultura de .NET no dice nada, así
    /// que se pregunta a Windows por su identificador de idioma primario.
    /// </summary>
    public static void EscogerElDeWindows(int idiomaPrimario)
    {
        Escoger(idiomaPrimario switch
        {
            0x0A => "es",
            0x07 => "de",
            0x0C => "fr",
            0x16 => "pt",
            0x10 => "it",
            0x11 => "ja",
            0x04 => "zh",
            _ => "en",
        });
    }

    /// <summary>La frase en el idioma de ahora, con sus huecos rellenos.</summary>
    public static string T(string clave, params object?[] args)
    {
        if (!Frases.TryGetValue(clave, out var fila)) return clave;
        var texto = fila[columna];
        // Un hueco sin su dato imprime la frase cruda antes que reventar: un
        // fallo de traducción no puede tumbar una importación.
        try { return args.Length == 0 ? texto : string.Format(texto, args); }
        catch { return texto; }
    }

    /// <summary>Lo mismo, pero directo a la consola.</summary>
    public static void Linea(string clave, params object?[] args) => Console.WriteLine(T(clave, args));

    private static readonly Dictionary<string, string[]> Frases = new()
    {
        ["titulo"] =
        [
            "MTG Corner — bridge to MTG Arena",
            "MTG Corner — puente con MTG Arena",
            "MTG Corner — Brücke zu MTG Arena",
            "MTG Corner — passerelle avec MTG Arena",
            "MTG Corner — ponte com o MTG Arena",
            "MTG Corner — ponte con MTG Arena",
            "MTG Corner — MTG Arena 連携ツール",
            "MTG Corner — MTG Arena 连接程序",
        ],
        // Al arrancar, SÓLO si en GitHub hay una versión más nueva que ésta. Si
        // es la misma no se dice nada: quien ya está al día no necesita una
        // línea recordándoselo cada vez que abre el programa.
        ["version_nueva"] =
        [
            "There's a newer version: v{0} (you have v{1}). Download it here: {2}",
            "Hay una versión más nueva: v{0} (tienes la v{1}). Descárgala aquí: {2}",
            "Es gibt eine neuere Version: v{0} (du hast v{1}). Hier herunterladen: {2}",
            "Une version plus récente existe : v{0} (tu as la v{1}). À télécharger ici : {2}",
            "Há uma versão mais recente: v{0} (tens a v{1}). Descarrega aqui: {2}",
            "C'è una versione più recente: v{0} (hai la v{1}). Scaricala qui: {2}",
            "新しいバージョンがあります: v{0}（お使いのバージョンは v{1}）。こちらからダウンロード: {2}",
            "有更新的版本：v{0}（你当前是 v{1}）。在此下载：{2}",
        ],
        ["sin_conexion"] =
        [
            "Couldn't reach MTG Corner: {0}",
            "No se pudo conectar con MTG Corner: {0}",
            "MTG Corner ist nicht erreichbar: {0}",
            "Impossible de joindre MTG Corner : {0}",
            "Não foi possível ligar ao MTG Corner: {0}",
            "Impossibile raggiungere MTG Corner: {0}",
            "MTG Corner に接続できませんでした: {0}",
            "无法连接 MTG Corner：{0}",
        ],
        ["abriendo_navegador"] =
        [
            "Opening your browser to confirm it's you…",
            "Abriendo tu navegador para confirmar que eres tú…",
            "Browser wird geöffnet, damit du bestätigst, dass du es bist…",
            "Ouverture de votre navigateur pour confirmer que c'est bien vous…",
            "A abrir o teu navegador para confirmares que és tu…",
            "Apertura del browser per confermare che sei tu…",
            "本人確認のためブラウザーを開いています…",
            "正在打开浏览器以确认是你本人…",
        ],
        ["si_no_abre"] =
        [
            "If it doesn't open on its own, go to: {0}",
            "Si no se abre solo, entra en: {0}",
            "Falls er sich nicht von selbst öffnet: {0}",
            "S'il ne s'ouvre pas tout seul, allez sur : {0}",
            "Se não abrir sozinho, vai a: {0}",
            "Se non si apre da solo, vai su: {0}",
            "自動で開かない場合はこちら: {0}",
            "如果没有自动打开，请访问：{0}",
        ],
        ["esperando_confirmacion"] =
        [
            "Waiting for confirmation (up to 3 minutes)…",
            "Esperando la confirmación (hasta 3 minutos)…",
            "Warte auf die Bestätigung (bis zu 3 Minuten)…",
            "En attente de la confirmation (jusqu'à 3 minutes)…",
            "À espera da confirmação (até 3 minutos)…",
            "In attesa della conferma (fino a 3 minuti)…",
            "確認を待っています（最大3分）…",
            "正在等待确认（最多 3 分钟）…",
        ],
        ["no_confirmado"] =
        [
            "Not confirmed in time. Nothing was read or saved.",
            "No se confirmó a tiempo. No se ha leído ni guardado nada.",
            "Nicht rechtzeitig bestätigt. Es wurde nichts gelesen und nichts gespeichert.",
            "Pas de confirmation à temps. Rien n'a été lu ni enregistré.",
            "Não foi confirmado a tempo. Nada foi lido nem guardado.",
            "Nessuna conferma in tempo. Non è stato letto né salvato nulla.",
            "時間内に確認されませんでした。何も読み取らず、何も保存していません。",
            "未在时限内确认。没有读取也没有保存任何内容。",
        ],
        ["confirmado"] =
        [
            "Confirmed.",
            "Confirmado.",
            "Bestätigt.",
            "Confirmé.",
            "Confirmado.",
            "Confermato.",
            "確認できました。",
            "已确认。",
        ],
        ["leyendo_log"] =
        [
            "Reading your decks and wildcards from Arena's log…",
            "Leyendo tus mazos y comodines del registro de Arena…",
            "Lese deine Decks und Wildcards aus dem Arena-Log…",
            "Lecture de vos decks et jokers dans le journal d'Arena…",
            "A ler os teus baralhos e curingas do registo do Arena…",
            "Lettura dei tuoi mazzi e jolly dal registro di Arena…",
            "Arena のログからデッキとワイルドカードを読み取っています…",
            "正在从 Arena 日志读取你的套牌和万能牌…",
        ],
        ["sin_log"] =
        [
            "Couldn't find Arena's Player.log in {0}.",
            "No se encontró el Player.log de Arena en {0}.",
            "Arenas Player.log wurde in {0} nicht gefunden.",
            "Le Player.log d'Arena est introuvable dans {0}.",
            "Não foi encontrado o Player.log do Arena em {0}.",
            "Non si è trovato il Player.log di Arena in {0}.",
            "Arena の Player.log が {0} に見つかりませんでした。",
            "在 {0} 找不到 Arena 的 Player.log。",
        ],
        ["sin_log_2"] =
        [
            "Decks and wildcards won't be updated this time.",
            "Esta vez no se actualizarán los mazos ni los comodines.",
            "Decks und Wildcards werden diesmal nicht aktualisiert.",
            "Les decks et les jokers ne seront pas mis à jour cette fois-ci.",
            "Desta vez os baralhos e os curingas não serão atualizados.",
            "Questa volta mazzi e jolly non si aggiornano.",
            "今回はデッキとワイルドカードは更新されません。",
            "本次不会更新套牌和万能牌。",
        ],
        ["sin_detalle_1"] =
        [
            "Arena's log doesn't have the detailed data needed for your decks.",
            "El registro de Arena no trae los datos detallados que hacen falta para tus mazos.",
            "Im Arena-Log fehlen die ausführlichen Daten, die für deine Decks nötig sind.",
            "Le journal d'Arena ne contient pas les données détaillées nécessaires à vos decks.",
            "O registo do Arena não traz os dados detalhados necessários para os teus baralhos.",
            "Il registro di Arena non ha i dati dettagliati che servono per i tuoi mazzi.",
            "Arena のログに、デッキに必要な詳細データがありません。",
            "Arena 日志里没有套牌所需的详细数据。",
        ],
        ["sin_detalle_2"] =
        [
            "In Arena: gear icon → Account → check \"Detailed Logs (Plugin Support)\",",
            "En Arena: icono del engranaje → Cuenta → marca \"Detailed Logs (Plugin Support)\",",
            "In Arena: Zahnrad → Account → \"Detailed Logs (Plugin Support)\" ankreuzen,",
            "Dans Arena : icône d'engrenage → Compte → cochez \"Detailed Logs (Plugin Support)\",",
            "No Arena: ícone da engrenagem → Conta → marca \"Detailed Logs (Plugin Support)\",",
            "In Arena: icona dell'ingranaggio → Account → spunta \"Detailed Logs (Plugin Support)\",",
            "Arena で歯車アイコン → アカウント → \"Detailed Logs (Plugin Support)\" にチェックを入れ、",
            "在 Arena 中：齿轮图标 → 账户 → 勾选 \"Detailed Logs (Plugin Support)\"，",
        ],
        ["sin_detalle_3"] =
        [
            "restart Arena, and run this program again to import your decks.",
            "reinicia Arena y vuelve a ejecutar este programa para importar tus mazos.",
            "starte Arena neu und führe dieses Programm noch einmal aus, um deine Decks zu importieren.",
            "redémarrez Arena, puis relancez ce programme pour importer vos decks.",
            "reinicia o Arena e volta a executar este programa para importares os teus baralhos.",
            "riavvia Arena ed esegui di nuovo questo programma per importare i tuoi mazzi.",
            "Arena を再起動してから、このプログラムをもう一度実行してデッキを取り込んでください。",
            "重启 Arena，然后重新运行本程序来导入你的套牌。",
        ],
        ["log_encontrado"] =
        [
            "Found {0} login(s) and {1} deck change(s) in the log.",
            "En el registro hay {0} inicio(s) de sesión y {1} cambio(s) de mazo.",
            "Im Log stehen {0} Anmeldung(en) und {1} Deck-Änderung(en).",
            "Le journal contient {0} connexion(s) et {1} modification(s) de deck.",
            "No registo há {0} início(s) de sessão e {1} alteração(ões) de baralho.",
            "Nel registro ci sono {0} accesso/i e {1} modifica/che ai mazzi.",
            "ログにログイン {0} 件、デッキの変更 {1} 件が見つかりました。",
            "日志中有 {0} 次登录和 {1} 次套牌变更。",
        ],
        ["nada_que_guardar"] =
        [
            "There's nothing to save — neither the collection nor the log could be read.",
            "No hay nada que guardar: no se pudo leer ni la colección ni el registro.",
            "Es gibt nichts zu speichern — weder die Sammlung noch das Log konnten gelesen werden.",
            "Il n'y a rien à enregistrer : ni la collection ni le journal n'ont pu être lus.",
            "Não há nada para guardar: não foi possível ler nem a coleção nem o registo.",
            "Non c'è niente da salvare: non si è potuto leggere né la collezione né il registro.",
            "保存するものがありません。コレクションもログも読み取れませんでした。",
            "没有可保存的内容：收藏和日志都无法读取。",
        ],
        ["enviando"] =
        [
            "Sending your decks and wildcards to MTG Corner…",
            "Enviando tus mazos y comodines a MTG Corner…",
            "Sende deine Decks und Wildcards an MTG Corner…",
            "Envoi de vos decks et jokers à MTG Corner…",
            "A enviar os teus baralhos e curingas para o MTG Corner…",
            "Invio dei tuoi mazzi e jolly a MTG Corner…",
            "デッキとワイルドカードを MTG Corner に送信しています…",
            "正在把你的套牌和万能牌发送到 MTG Corner…",
        ],
        ["enviando_con_cartas"] =
        [
            "Sending {0} cards, your decks and wildcards to MTG Corner…",
            "Enviando {0} cartas, tus mazos y tus comodines a MTG Corner…",
            "Sende {0} Karten, deine Decks und Wildcards an MTG Corner…",
            "Envoi de {0} cartes, de vos decks et de vos jokers à MTG Corner…",
            "A enviar {0} cartas, os teus baralhos e os teus curingas para o MTG Corner…",
            "Invio di {0} carte, dei tuoi mazzi e dei tuoi jolly a MTG Corner…",
            "{0} 枚のカードとデッキ、ワイルドカードを MTG Corner に送信しています…",
            "正在把 {0} 张牌、你的套牌和万能牌发送到 MTG Corner…",
        ],
        ["sin_conexion_guardar"] =
        [
            "Couldn't reach MTG Corner to save: {0}. Nothing was saved.",
            "No se pudo conectar con MTG Corner para guardar: {0}. No se guardó nada.",
            "MTG Corner war zum Speichern nicht erreichbar: {0}. Es wurde nichts gespeichert.",
            "Impossible de joindre MTG Corner pour enregistrer : {0}. Rien n'a été enregistré.",
            "Não foi possível ligar ao MTG Corner para guardar: {0}. Nada foi guardado.",
            "Impossibile raggiungere MTG Corner per salvare: {0}. Non è stato salvato nulla.",
            "保存のために MTG Corner に接続できませんでした: {0}。何も保存していません。",
            "保存时无法连接 MTG Corner：{0}。没有保存任何内容。",
        ],
        ["rechazado"] =
        [
            "MTG Corner didn't accept the import (code {0}). Nothing was saved.",
            "MTG Corner no aceptó la importación (código {0}). No se guardó nada.",
            "MTG Corner hat den Import nicht angenommen (Code {0}). Es wurde nichts gespeichert.",
            "MTG Corner a refusé l'import (code {0}). Rien n'a été enregistré.",
            "O MTG Corner não aceitou a importação (código {0}). Nada foi guardado.",
            "MTG Corner non ha accettato l'importazione (codice {0}). Non è stato salvato nulla.",
            "MTG Corner が取り込みを受け付けませんでした（コード {0}）。何も保存していません。",
            "MTG Corner 未接受此次导入（代码 {0}）。没有保存任何内容。",
        ],
        ["leidos_mazos"] =
        [
            "Read {0} deck(s){1}.",
            "Se han leído {0} mazo(s){1}.",
            "{0} Deck(s){1} gelesen.",
            "{0} deck(s){1} lus.",
            "Foram lidos {0} baralho(s){1}.",
            "Letti {0} mazzo/i{1}.",
            "デッキ {0} 個{1}を読み取りました。",
            "已读取 {0} 个套牌{1}。",
        ],
        ["y_coleccion"] =
        [
            " and {0} distinct cards of your collection",
            " y {0} cartas distintas de tu colección",
            " und {0} verschiedene Karten deiner Sammlung",
            " et {0} cartes différentes de votre collection",
            " e {0} cartas diferentes da tua coleção",
            " e {0} carte diverse della tua collezione",
            "、コレクションの {0} 種類のカード",
            "，以及你收藏中的 {0} 张不同的牌",
        ],
        ["abriendo_revision"] =
        [
            "Opening your browser so you can choose what to save…",
            "Abriendo tu navegador para que elijas qué se guarda…",
            "Browser wird geöffnet, damit du auswählst, was gespeichert wird…",
            "Ouverture de votre navigateur pour que vous choisissiez ce qui sera enregistré…",
            "A abrir o teu navegador para escolheres o que se guarda…",
            "Apertura del browser per farti scegliere che cosa salvare…",
            "保存する内容を選べるよう、ブラウザーを開いています…",
            "正在打开浏览器，让你选择要保存的内容…",
        ],
        ["nada_hasta_confirmar"] =
        [
            "Nothing is saved until you confirm there. The link expires in an hour.",
            "No se guarda nada hasta que lo confirmes ahí. El enlace caduca en una hora.",
            "Es wird nichts gespeichert, bis du dort bestätigst. Der Link läuft in einer Stunde ab.",
            "Rien n'est enregistré tant que vous ne confirmez pas là-bas. Le lien expire dans une heure.",
            "Nada é guardado até confirmares aí. A ligação expira dentro de uma hora.",
            "Non si salva nulla finché non confermi lì. Il link scade tra un'ora.",
            "そこで確認するまで何も保存されません。リンクは1時間で切れます。",
            "在那里确认之前不会保存任何内容。该链接一小时后失效。",
        ],
        ["hecho"] =
        [
            "Done.",
            "Hecho.",
            "Fertig.",
            "Terminé.",
            "Pronto.",
            "Fatto.",
            "完了しました。",
            "完成。",
        ],
        ["hecho_coleccion"] =
        [
            "  Collection: saved as \"Arena\".",
            "  Colección: guardada como \"Arena\".",
            "  Sammlung: als \"Arena\" gespeichert.",
            "  Collection : enregistrée sous \"Arena\".",
            "  Coleção: guardada como \"Arena\".",
            "  Collezione: salvata come \"Arena\".",
            "  コレクション: \"Arena\" として保存しました。",
            "  收藏：已保存为 \"Arena\"。",
        ],
        ["hecho_mazos"] =
        [
            "  Decks: {0} saved ({1}).",
            "  Mazos: {0} guardados ({1}).",
            "  Decks: {0} gespeichert ({1}).",
            "  Decks : {0} enregistrés ({1}).",
            "  Baralhos: {0} guardados ({1}).",
            "  Mazzi: {0} salvati ({1}).",
            "  デッキ: {0} 個を保存しました（{1}）。",
            "  套牌：已保存 {0} 个（{1}）。",
        ],
        ["hecho_comodines"] =
        [
            "  Wildcards: updated.",
            "  Comodines: actualizados.",
            "  Wildcards: aktualisiert.",
            "  Jokers : mis à jour.",
            "  Curingas: atualizados.",
            "  Jolly: aggiornati.",
            "  ワイルドカード: 更新しました。",
            "  万能牌：已更新。",
        ],
        ["hecho_sin_traducir"] =
        [
            "  ({0} cards weren't recognized — they might be very new.)",
            "  ({0} cartas no se reconocieron; puede que sean muy nuevas.)",
            "  ({0} Karten wurden nicht erkannt — vielleicht sind sie ganz neu.)",
            "  ({0} cartes non reconnues — elles sont peut-être toutes récentes.)",
            "  ({0} cartas não foram reconhecidas; podem ser muito recentes.)",
            "  ({0} carte non riconosciute: forse sono molto recenti.)",
            "  （{0} 枚のカードは認識できませんでした。ごく新しいカードかもしれません。）",
            "  （有 {0} 张牌无法识别，可能是非常新的牌。）",
        ],
        ["arena_cerrado"] =
        [
            "MTG Arena isn't open. Open it now and wait until it has fully loaded…",
            "MTG Arena no está abierto. Ábrelo ahora y espera a que cargue del todo…",
            "MTG Arena ist nicht geöffnet. Öffne es jetzt und warte, bis es vollständig geladen ist…",
            "MTG Arena n'est pas ouvert. Ouvrez-le maintenant et attendez qu'il soit complètement chargé…",
            "O MTG Arena não está aberto. Abre-o agora e espera que carregue por completo…",
            "MTG Arena non è aperto. Aprilo adesso e aspetta che carichi del tutto…",
            "MTG Arena が開いていません。今すぐ起動して、完全に読み込まれるまで待ってください…",
            "MTG Arena 没有打开。现在启动它，并等待完全加载…",
        ],
        ["arena_cerrado_2"] =
        [
            "(up to 3 minutes; your decks and wildcards will be read either way)",
            "(hasta 3 minutos; tus mazos y comodines se leerán igualmente)",
            "(bis zu 3 Minuten; deine Decks und Wildcards werden so oder so gelesen)",
            "(jusqu'à 3 minutes ; vos decks et jokers seront lus dans tous les cas)",
            "(até 3 minutos; os teus baralhos e curingas serão lidos de qualquer forma)",
            "(fino a 3 minuti; i tuoi mazzi e jolly si leggono comunque)",
            "（最大3分。デッキとワイルドカードはどちらにせよ読み取ります）",
            "（最多 3 分钟；无论如何都会读取你的套牌和万能牌）",
        ],
        ["arena_no_abrio"] =
        [
            "Arena wasn't open in time, so your full collection can't be read this time.",
            "Arena no llegó a abrirse a tiempo, así que esta vez no se puede leer tu colección completa.",
            "Arena war nicht rechtzeitig offen, daher lässt sich deine ganze Sammlung diesmal nicht lesen.",
            "Arena n'était pas ouvert à temps : votre collection complète ne peut pas être lue cette fois-ci.",
            "O Arena não abriu a tempo, por isso desta vez não é possível ler a tua coleção completa.",
            "Arena non si è aperto in tempo, quindi questa volta non si può leggere la tua collezione completa.",
            "Arena が間に合わなかったため、今回はコレクション全体を読み取れません。",
            "Arena 未能及时打开，这次无法读取你的完整收藏。",
        ],
        ["arena_detectado"] =
        [
            "Arena detected. Reading your collection…",
            "Arena detectado. Leyendo tu colección…",
            "Arena erkannt. Lese deine Sammlung…",
            "Arena détecté. Lecture de votre collection…",
            "Arena detetado. A ler a tua coleção…",
            "Arena rilevato. Lettura della tua collezione…",
            "Arena を検出しました。コレクションを読み取っています…",
            "已检测到 Arena。正在读取你的收藏…",
        ],
        ["coleccion_leida"] =
        [
            "Read {0} cards from your collection.",
            "Se han leído {0} cartas de tu colección.",
            "{0} Karten aus deiner Sammlung gelesen.",
            "{0} cartes lues dans votre collection.",
            "Foram lidas {0} cartas da tua coleção.",
            "Lette {0} carte della tua collezione.",
            "コレクションから {0} 枚のカードを読み取りました。",
            "已从你的收藏中读取 {0} 张牌。",
        ],
        ["fallo_coleccion"] =
        [
            "Couldn't read the collection (attempt {0} of 3): {1}",
            "No se pudo leer la colección (intento {0} de 3): {1}",
            "Die Sammlung ließ sich nicht lesen (Versuch {0} von 3): {1}",
            "Impossible de lire la collection (tentative {0} sur 3) : {1}",
            "Não foi possível ler a coleção (tentativa {0} de 3): {1}",
            "Non si è potuta leggere la collezione (tentativo {0} di 3): {1}",
            "コレクションを読み取れませんでした（3回中 {0} 回目）: {1}",
            "无法读取收藏（第 {0} 次，共 3 次）：{1}",
        ],
        ["aviso_sin_coleccion"] =
        [
            "!! Your full collection (\"Arena\") will NOT be updated this time.",
            "!! Tu colección completa (\"Arena\") NO se actualizará esta vez.",
            "!! Deine vollständige Sammlung (\"Arena\") wird diesmal NICHT aktualisiert.",
            "!! Votre collection complète (\"Arena\") ne sera PAS mise à jour cette fois-ci.",
            "!! A tua coleção completa (\"Arena\") NÃO será atualizada desta vez.",
            "!! La tua collezione completa (\"Arena\") NON si aggiorna questa volta.",
            "!! 今回はコレクション全体（\"Arena\"）を更新しません。",
            "!! 本次不会更新你的完整收藏（\"Arena\"）。",
        ],
        ["aviso_sin_coleccion_2"] =
        [
            "   Your decks and wildcards can still be saved now.",
            "   Tus mazos y tus comodines sí se pueden guardar ahora.",
            "   Deine Decks und Wildcards lassen sich jetzt trotzdem speichern.",
            "   Vos decks et vos jokers peuvent tout de même être enregistrés maintenant.",
            "   Os teus baralhos e curingas podem na mesma ser guardados agora.",
            "   I tuoi mazzi e i tuoi jolly si possono comunque salvare adesso.",
            "   デッキとワイルドカードは今このまま保存できます。",
            "   你的套牌和万能牌现在仍然可以保存。",
        ],
        ["no_pude_leer"] =
        [
            "Couldn't read {0}: {1}",
            "No se pudo leer {0}: {1}",
            "{0} ließ sich nicht lesen: {1}",
            "Impossible de lire {0} : {1}",
            "Não foi possível ler {0}: {1}",
            "Non si è potuto leggere {0}: {1}",
            "{0} を読み取れませんでした: {1}",
            "无法读取 {0}：{1}",
        ],
        ["cerrando"] =
        [
            "Closing in {0} s… (any key closes it now)",
            "Se cierra en {0} s… (cualquier tecla lo cierra ya)",
            "Schließt in {0} s… (beliebige Taste schließt sofort)",
            "Fermeture dans {0} s… (une touche ferme tout de suite)",
            "Fecha em {0} s… (qualquer tecla fecha já)",
            "Si chiude tra {0} s… (un tasto qualsiasi chiude subito)",
            "{0} 秒後に閉じます…（キーを押すとすぐ閉じます）",
            "{0} 秒后关闭…（按任意键立即关闭）",
        ],
        ["pulsa_tecla"] =
        [
            "Press a key to close…",
            "Pulsa una tecla para cerrar…",
            "Zum Schließen eine Taste drücken…",
            "Appuyez sur une touche pour fermer…",
            "Carrega numa tecla para fechar…",
            "Premi un tasto per chiudere…",
            "キーを押すと閉じます…",
            "按任意键关闭…",
        ],
    };
}
