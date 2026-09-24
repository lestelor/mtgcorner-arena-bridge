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
        // La pregunta de la actualización. {0} es la versión nueva y {1} la de
        // ahora. Por consola se lee en la ventana; en el modo de fondo es el
        // texto del cuadro de Windows, y por eso se aguanta solo, sin la línea
        // de teclas de debajo.
        ["actualizar_pregunta"] =
        [
            "Version {0} is out (you have {1}). Install it now? The program will restart on its own.",
            "Está la versión {0} (tienes la {1}). ¿La instalo ahora? El programa se reinicia solo.",
            "Version {0} ist da (du hast {1}). Jetzt installieren? Das Programm startet von selbst neu.",
            "La version {0} est disponible (tu as la {1}). L'installer maintenant ? Le programme redémarre tout seul.",
            "Saiu a versão {0} (tens a {1}). Instalo agora? O programa reinicia sozinho.",
            "È uscita la versione {0} (hai la {1}). La installo adesso? Il programma si riavvia da solo.",
            "バージョン {0} が公開されました（お使いのバージョンは {1}）。今すぐ入れますか？ プログラムは自動で再起動します。",
            "已发布版本 {0}（你当前是 {1}）。现在安装吗？程序会自动重启。",
        ],
        ["actualizar_teclas"] =
        [
            "Press Enter to install, N to skip (it carries on by itself in 30 seconds).",
            "Pulsa Intro para instalarla o N para dejarlo (en 30 segundos sigue sin instalar).",
            "Enter zum Installieren, N zum Überspringen (nach 30 Sekunden geht es ohne weiter).",
            "Entrée pour installer, N pour passer (au bout de 30 secondes, ça continue sans).",
            "Carrega em Enter para instalar ou N para deixar (ao fim de 30 segundos segue sem instalar).",
            "Premi Invio per installarla o N per lasciar perdere (dopo 30 secondi prosegue senza).",
            "インストールするには Enter、やめるには N を押してください（30秒で先に進みます）。",
            "按 Enter 安装，按 N 跳过（30 秒后自动继续）。",
        ],
        ["actualizando"] =
        [
            "Downloading the new version…",
            "Descargando la versión nueva…",
            "Die neue Version wird heruntergeladen…",
            "Téléchargement de la nouvelle version…",
            "A descarregar a versão nova…",
            "Sto scaricando la nuova versione…",
            "新しいバージョンをダウンロードしています…",
            "正在下载新版本…",
        ],
        // Lo descargado no coincide con lo que el release dice que es. No se
        // instala: se dice y se sigue con la de ahora, que funciona.
        ["actualizar_huella"] =
        [
            "The download doesn't match what GitHub published, so it wasn't installed.",
            "Lo descargado no coincide con lo que GitHub publicó, así que no se ha instalado.",
            "Der Download stimmt nicht mit dem überein, was GitHub veröffentlicht hat, also wurde er nicht installiert.",
            "Le fichier téléchargé ne correspond pas à ce que GitHub a publié : il n'a pas été installé.",
            "O ficheiro descarregado não corresponde ao que o GitHub publicou, por isso não foi instalado.",
            "Il file scaricato non corrisponde a quello pubblicato su GitHub, quindi non è stato installato.",
            "ダウンロードしたファイルが GitHub の公開内容と一致しないため、インストールしませんでした。",
            "下载的文件与 GitHub 发布的内容不一致，因此没有安装。",
        ],
        ["actualizar_fallo"] =
        [
            "Couldn't install it ({0}). Carrying on with this version.",
            "No se ha podido instalar ({0}). Se sigue con esta versión.",
            "Installation nicht möglich ({0}). Es geht mit dieser Version weiter.",
            "Installation impossible ({0}). On continue avec cette version.",
            "Não foi possível instalar ({0}). Continua-se com esta versão.",
            "Non è stato possibile installarla ({0}). Si prosegue con questa versione.",
            "インストールできませんでした（{0}）。このバージョンのまま続けます。",
            "无法安装（{0}）。继续使用当前版本。",
        ],
        // EL AVISO QUE SE PINTA ENCIMA DE ARENA (ver Superposicion.cs). Frases
        // cortas a propósito: se leen de reojo, en mitad de una partida y sin
        // poder pulsarlas.
        ["sup_titulo"] =
        [
            "Read from your Arena",
            "Leído de tu Arena",
            "Aus deinem Arena gelesen",
            "Lu depuis votre Arena",
            "Lido do teu Arena",
            "Letto dal tuo Arena",
            "Arena から読み取りました",
            "已从你的 Arena 读取",
        ],
        ["sup_pendiente"] =
        [
            "{0} decks are waiting for your OK in the browser.",
            "{0} mazos esperan tu visto bueno en el navegador.",
            "{0} Decks warten im Browser auf dein OK.",
            "{0} decks attendent votre feu vert dans le navigateur.",
            "{0} baralhos esperam o teu OK no navegador.",
            "{0} mazzi aspettano il tuo via libera nel browser.",
            "{0} 個のデッキがブラウザーで確認を待っています。",
            "{0} 个套牌正在浏览器中等待你确认。",
        ],
        ["sup_guardado"] =
        [
            "{0} cards saved to your account.",
            "{0} cartas guardadas en tu cuenta.",
            "{0} Karten in deinem Konto gespeichert.",
            "{0} cartes enregistrées dans votre compte.",
            "{0} cartas guardadas na tua conta.",
            "{0} carte salvate nel tuo account.",
            "{0} 枚のカードをアカウントに保存しました。",
            "已将 {0} 张牌保存到你的账号。",
        ],
        // EL MODO DE FONDO. Se ofrece al terminar una importación, cuando ya se
        // ha visto para qué sirve el programa.
        ["residente_ofrecer"] =
        [
            "Want it to run by itself whenever you open Arena? It syncs in the background and the site tells you when there's something new.",
            "¿Quieres que se ejecute solo cada vez que abras Arena? Sincroniza de fondo y la web te avisa cuando haya algo nuevo.",
            "Soll es künftig von selbst starten, wenn du Arena öffnest? Es synchronisiert im Hintergrund und die Website sagt dir, wenn es etwas Neues gibt.",
            "Voulez-vous qu'il se lance tout seul quand vous ouvrez Arena ? Il synchronise en arrière-plan et le site vous prévient quand il y a du nouveau.",
            "Queres que se execute sozinho sempre que abrires o Arena? Sincroniza em segundo plano e o site avisa-te quando houver novidades.",
            "Vuoi che parta da solo ogni volta che apri Arena? Sincronizza in background e il sito ti avvisa quando c'è qualcosa di nuovo.",
            "Arena を開くたびに自動で実行しますか？ バックグラウンドで同期し、新しいデータがあるとサイトが知らせます。",
            "要让它在你每次打开 Arena 时自动运行吗？它会在后台同步，有新数据时网站会提醒你。",
        ],
        ["residente_teclas"] =
        [
            "Enter for yes, N for no (it carries on by itself in 30 seconds).",
            "Intro para sí, N para no (en 30 segundos sigue sin hacer nada).",
            "Enter für ja, N für nein (nach 30 Sekunden geht es ohne weiter).",
            "Entrée pour oui, N pour non (au bout de 30 secondes, ça continue sans).",
            "Enter para sim, N para não (ao fim de 30 segundos segue sem fazer nada).",
            "Invio per sì, N per no (dopo 30 secondi prosegue senza).",
            "はいは Enter、いいえは N（30秒で先に進みます）。",
            "是按 Enter，否按 N（30 秒后自动继续）。",
        ],
        ["residente_puesto"] =
        [
            "Done. It'll start with Windows and wait for Arena, quietly. To undo it, run this program with --no-arrancar-solo.",
            "Hecho. Arrancará con Windows y esperará a Arena, sin molestar. Para deshacerlo, ejecuta este programa con --no-arrancar-solo.",
            "Erledigt. Es startet mit Windows und wartet unauffällig auf Arena. Zum Rückgängigmachen dieses Programm mit --no-arrancar-solo starten.",
            "C'est fait. Il démarrera avec Windows et attendra Arena, sans déranger. Pour annuler, lancez ce programme avec --no-arrancar-solo.",
            "Feito. Vai arrancar com o Windows e esperar pelo Arena, sem incomodar. Para desfazer, executa este programa com --no-arrancar-solo.",
            "Fatto. Partirà con Windows e aspetterà Arena, senza disturbare. Per annullare, esegui questo programma con --no-arrancar-solo.",
            "完了です。Windows の起動時に始まり、静かに Arena を待ちます。解除するには --no-arrancar-solo を付けて実行してください。",
            "完成。它会随 Windows 启动并安静地等待 Arena。要取消，请用 --no-arrancar-solo 运行本程序。",
        ],
        ["residente_quitado"] =
        [
            "It won't start with Windows any more.",
            "Ya no arrancará con Windows.",
            "Es startet nicht mehr mit Windows.",
            "Il ne démarrera plus avec Windows.",
            "Já não arranca com o Windows.",
            "Non partirà più con Windows.",
            "Windows 起動時には実行されなくなりました。",
            "已不再随 Windows 启动。",
        ],
        ["residente_fallo"] =
        [
            "Couldn't change the Windows startup setting ({0}).",
            "No se ha podido cambiar el arranque con Windows ({0}).",
            "Der Windows-Autostart konnte nicht geändert werden ({0}).",
            "Impossible de modifier le démarrage avec Windows ({0}).",
            "Não foi possível alterar o arranque com o Windows ({0}).",
            "Non è stato possibile cambiare l'avvio con Windows ({0}).",
            "Windows の自動起動設定を変更できませんでした（{0}）。",
            "无法修改随 Windows 启动的设置（{0}）。",
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
        // Al confirmar por primera vez, cuando el código se cambia por el token
        // que se queda en este ordenador (ver Vinculo.cs).
        ["vinculo_guardado"] =
        [
            "Linked. This computer won't ask again: you can unlink it from the site whenever you want.",
            "Vinculado. Este ordenador ya no vuelve a preguntar: puedes desvincularlo desde la web cuando quieras.",
            "Verknüpft. Dieser Computer fragt nicht mehr: Du kannst die Verknüpfung jederzeit auf der Website aufheben.",
            "Lié. Cet ordinateur ne redemandera plus : vous pouvez le délier depuis le site quand vous voulez.",
            "Ligado. Este computador não volta a perguntar: podes desligá-lo no site quando quiseres.",
            "Collegato. Questo computer non chiede più: puoi scollegarlo dal sito quando vuoi.",
            "連携しました。今後このパソコンでは確認を求めません。サイトからいつでも解除できます。",
            "已绑定。这台电脑不再询问，你随时可以在网站上解除绑定。",
        ],
        // Y en las ejecuciones siguientes, en lugar de abrir el navegador.
        ["ya_vinculado"] =
        [
            "This computer is already linked to your MTG Corner account.",
            "Este ordenador ya está vinculado a tu cuenta de MTG Corner.",
            "Dieser Computer ist bereits mit deinem MTG-Corner-Konto verknüpft.",
            "Cet ordinateur est déjà lié à votre compte MTG Corner.",
            "Este computador já está ligado à tua conta do MTG Corner.",
            "Questo computer è già collegato al tuo account MTG Corner.",
            "このパソコンはすでに MTG Corner のアカウントと連携済みです。",
            "这台电脑已经和你的 MTG Corner 账号绑定。",
        ],
        // Cuando el servidor rechaza el token: revocado desde la web, o de una
        // cuenta que ya no existe. El fichero se borra, así que "vuelve a
        // ejecutar" basta de verdad.
        ["vinculo_caducado"] =
        [
            "This computer is no longer linked to your account. Run the program again to link it.",
            "Este ordenador ya no está vinculado a tu cuenta. Vuelve a ejecutar el programa para vincularlo.",
            "Dieser Computer ist nicht mehr mit deinem Konto verknüpft. Starte das Programm erneut, um ihn zu verknüpfen.",
            "Cet ordinateur n'est plus lié à votre compte. Relancez le programme pour le lier.",
            "Este computador já não está ligado à tua conta. Executa o programa outra vez para o ligar.",
            "Questo computer non è più collegato al tuo account. Esegui di nuovo il programma per collegarlo.",
            "このパソコンの連携は解除されています。もう一度プログラムを実行して連携してください。",
            "这台电脑的绑定已解除。请重新运行程序进行绑定。",
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
        // La columna de iconos sobre el juego (Columna.cs): un nombre por icono.
        ["col_importar"] =
        [
            "Import now", "Importar ahora", "Jetzt importieren", "Importer maintenant",
            "Importar agora", "Importa adesso", "今すぐ取り込む", "立即导入",
        ],
        ["col_coleccion"] =
        [
            "My collection", "Mi colección", "Meine Sammlung", "Ma collection",
            "A minha coleção", "La mia collezione", "マイコレクション", "我的收藏",
        ],
        ["col_constructor"] =
        [
            "Deck builder", "Constructor de mazos", "Deckbuilder", "Constructeur de deck",
            "Construtor de baralhos", "Costruttore di mazzi", "デッキビルダー", "套牌构筑器",
        ],
        ["col_arranque_si"] =
        [
            "Starts with Windows: on", "Arranque automático: sí", "Autostart: an", "Démarrage auto : oui",
            "Arranque automático: sim", "Avvio automatico: sì", "自動起動：オン", "自动启动：开",
        ],
        ["col_arranque_no"] =
        [
            "Starts with Windows: off", "Arranque automático: no", "Autostart: aus", "Démarrage auto : non",
            "Arranque automático: não", "Avvio automatico: no", "自動起動：オフ", "自动启动：关",
        ],
        ["col_salir"] =
        [
            "Close MTG Corner", "Cerrar MTG Corner", "MTG Corner schließen", "Fermer MTG Corner",
            "Fechar o MTG Corner", "Chiudi MTG Corner", "MTG Corner を終了", "关闭 MTG Corner",
        ],
        // Tras poner el arranque automático: ya está funcionando, no hace falta reiniciar.
        ["residente_lanzado"] =
        [
            "It's already running in the background: no need to sign in again or restart.",
            "Ya está funcionando de fondo: no hace falta reiniciar ni volver a entrar.",
            "Es läuft schon im Hintergrund: kein Neustart und keine neue Anmeldung nötig.",
            "Il tourne déjà en arrière-plan : pas besoin de redémarrer ni de vous reconnecter.",
            "Já está a funcionar em segundo plano: não é preciso reiniciar nem voltar a entrar.",
            "È già in esecuzione in background: non serve riavviare né rientrare.",
            "すでにバックグラウンドで動いています。再起動や再ログインは不要です。",
            "已经在后台运行：无需重启或重新登录。",
        ],
        // Al acabar una ejecución normal: el programa se queda de fondo y la
        // columna aparece sobre Arena. Dice DÓNDE mirar, que es lo que faltaba.
        ["columna_puesta"] =
        [
            "MTG Corner stays in the background: with Arena in front you'll see its column on the right edge. To remove it, use \"Close MTG Corner\" there.",
            "MTG Corner se queda de fondo: con Arena delante verás su columna en el borde derecho. Para quitarla, pulsa «Cerrar MTG Corner» ahí mismo.",
            "MTG Corner läuft im Hintergrund weiter: Wenn Arena im Vordergrund ist, siehst du seine Leiste am rechten Rand. Zum Entfernen dort „MTG Corner schließen“ wählen.",
            "MTG Corner reste en arrière-plan : avec Arena au premier plan, sa colonne apparaît sur le bord droit. Pour l'enlever, choisissez « Fermer MTG Corner » dessus.",
            "O MTG Corner fica em segundo plano: com o Arena à frente vês a coluna na margem direita. Para a tirar, usa «Fechar o MTG Corner» aí.",
            "MTG Corner resta in background: con Arena in primo piano vedrai la sua colonna sul bordo destro. Per toglierla, usa «Chiudi MTG Corner» lì.",
            "MTG Corner はバックグラウンドに残ります。Arena を前面にすると右端に列が表示されます。消すには、そこで「MTG Corner を終了」を選んでください。",
            "MTG Corner 会留在后台：当 Arena 在前台时，右侧边缘会出现它的图标列。要移除，请在那里选择“关闭 MTG Corner”。",
        ],
        // Las filas que pone el juego en la columna (Contexto.cs). {0} = mazo o carta.
        ["col_mejorar"] =
        [
            "Improve “{0}”", "Mejorar «{0}»", "„{0}“ verbessern", "Améliorer « {0} »",
            "Melhorar «{0}»", "Migliorare «{0}»", "「{0}」を改善", "改进“{0}”",
        ],
        ["col_similares"] =
        [
            "Cards similar to {0}", "Cartas similares a {0}", "Karten ähnlich wie {0}", "Cartes similaires à {0}",
            "Cartas semelhantes a {0}", "Carte simili a {0}", "{0} に似たカード", "与 {0} 相似的牌",
        ],
        ["col_combos"] =
        [
            "Combos with {0}", "Combos con {0}", "Combos mit {0}", "Combos avec {0}",
            "Combos com {0}", "Combo con {0}", "{0} のコンボ", "{0} 的组合技",
        ],
        ["col_esta_carta"] =
        [
            "this card", "esta carta", "diese Karte", "cette carte",
            "esta carta", "questa carta", "このカード", "这张牌",
        ],
        // El panel de cartas parecidas encima del juego (PanelCartas.cs).
        ["panel_buscando"] =
        [
            "Looking for similar cards...", "Buscando cartas parecidas...", "Ähnliche Karten werden gesucht...", "Recherche de cartes similaires...",
            "À procura de cartas parecidas...", "Ricerca di carte simili...", "似たカードを探しています...", "正在查找相似的牌...",
        ],
        ["panel_nada"] =
        [
            "No similar cards found for this one.", "No he encontrado cartas parecidas a ésta.", "Zu dieser Karte wurde nichts Ähnliches gefunden.", "Aucune carte similaire trouvée pour celle-ci.",
            "Não encontrei cartas parecidas com esta.", "Nessuna carta simile a questa.", "このカードに似たカードは見つかりませんでした。", "没有找到与这张牌相似的牌。",
        ],
        ["panel_pie"] =
        [
            "Click a card to open it on the site", "Pulsa una carta para abrirla en la web", "Klicke eine Karte an, um sie auf der Website zu öffnen", "Cliquez sur une carte pour l'ouvrir sur le site",
            "Clica numa carta para a abrires no site", "Clicca una carta per aprirla sul sito", "カードをクリックするとサイトで開きます", "点击一张牌即可在网站上打开",
        ],
        // Lo que dice el aviso al pulsar «Importar ahora» en la columna.
        ["col_subiendo"] =
        [
            "Reading Arena and uploading…", "Leyendo Arena y subiendo…", "Arena wird gelesen und hochgeladen…", "Lecture d'Arena et envoi…",
            "A ler o Arena e a enviar…", "Lettura di Arena e invio…", "Arena を読み取ってアップロード中…", "正在读取 Arena 并上传…",
        ],
        ["col_al_dia"] =
        [
            "Everything was already up to date on the site.", "Todo estaba ya al día en la web.", "Auf der Website war schon alles aktuell.", "Tout était déjà à jour sur le site.",
            "Já estava tudo em dia no site.", "Era già tutto aggiornato sul sito.", "サイトはすでに最新でした。", "网站上的内容已是最新。",
        ],
        ["col_sin_datos"] =
        [
            "Nothing to upload: Arena's collection and log weren't readable.", "Nada que subir: no se pudo leer la colección ni el registro de Arena.", "Nichts hochzuladen: Sammlung und Protokoll von Arena waren nicht lesbar.", "Rien à envoyer : la collection et le journal d'Arena n'étaient pas lisibles.",
            "Nada para enviar: não foi possível ler a coleção nem o registo do Arena.", "Niente da inviare: collezione e registro di Arena non erano leggibili.", "アップロードするものがありません。Arena のコレクションとログを読み取れませんでした。", "没有可上传的内容：无法读取 Arena 的收藏和日志。",
        ],
        ["col_fallo"] =
        [
            "The upload failed. Try again in a moment.", "La subida falló. Prueba otra vez en un momento.", "Das Hochladen ist fehlgeschlagen. Versuch es gleich noch einmal.", "L'envoi a échoué. Réessayez dans un instant.",
            "O envio falhou. Tenta outra vez daqui a um momento.", "L'invio non è riuscito. Riprova tra un momento.", "アップロードに失敗しました。しばらくしてからもう一度お試しください。", "上传失败。请稍后再试。",
        ],
        // El arranque automático apuntaba a una descarga anterior: se corrige.
        ["arranque_actualizado"] =
        [
            "Windows was still starting an older copy: it now starts this one.",
            "Windows seguía arrancando una copia anterior: ahora arranca ésta.",
            "Windows startete noch eine ältere Kopie: Jetzt startet es diese.",
            "Windows lançait encore une copie plus ancienne : il lance désormais celle-ci.",
            "O Windows ainda arrancava uma cópia anterior: agora arranca esta.",
            "Windows avviava ancora una copia precedente: ora avvia questa.",
            "Windows は以前のコピーを起動していました。これからはこちらを起動します。",
            "Windows 之前启动的是旧副本：现在会启动这一个。",
        ],
        // El aviso que se aguantó durante la partida, al cerrar Arena.
        ["sup_actualizado"] =
        [
            "Your decks were updated while you played. Review them on the site.",
            "Tus mazos se han actualizado mientras jugabas. Revísalos en la web.",
            "Deine Decks wurden während des Spielens aktualisiert. Sieh sie dir auf der Website an.",
            "Vos decks ont été mis à jour pendant la partie. Vérifiez-les sur le site.",
            "Os teus baralhos foram atualizados enquanto jogavas. Revê-os no site.",
            "I tuoi mazzi sono stati aggiornati mentre giocavi. Controllali sul sito.",
            "プレイ中にデッキが更新されました。サイトで確認してください。",
            "你的套牌在游戏期间已更新。请到网站查看。",
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
