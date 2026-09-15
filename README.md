# MTG Corner — puente con MTG Arena

Programa pequeño, en C#/.NET, que lee tu colección, tus mazos y tus comodines
de MTG Arena y los guarda directamente en tu cuenta de
[mtgcorner.com](https://mtgcorner.com).

## Cómo funciona

Este programa **no lee memoria de nada por su cuenta**. Arranca
[`mtga-tracker-daemon`](https://github.com/frcaton/mtga-tracker-daemon) —de
terceros, código abierto, GPLv3— que es quien de verdad hace la lectura de
memoria de Arena en modo sólo lectura, le pide la colección por su servidor
HTTP local, y la envía al sitio.

Los **mazos y los comodines** salen del `Player.log` de Arena, sin preguntarle
al usuario dónde está: Unity lo escribe siempre en
`%USERPROFILE%\AppData\LocalLow\Wizards Of The Coast\MTGA`, con el instalador
de Wizards, con Steam o con Epic. Se leen `Player-prev.log` y `Player.log`,
compartidos para no chocar con Arena abierto.

```
MtgCornerArenaBridge.exe
        │
        ├─ POST /api/mtga-device/iniciar         → un código de un solo uso
        ├─ abre el navegador en /vincular-dispositivo?codigo=...
        ├─ GET /api/mtga-device/estado            (hasta que se confirme)
        │
        │   ── sólo a partir de aquí se toca Arena ──
        │
        ├─ arranca ─▶ mtga-tracker-daemon.exe -p <puerto libre>
        ├─ GET http://localhost:<puerto>/status   (espera a que detecte Arena)
        ├─ GET http://localhost:<puerto>/cards    ({ grpId, owned }[])
        │
        ├─ lee Player-prev.log y Player.log       (sólo inicios de sesión y líneas de mazo)
        │
        ├─ POST /api/mtga-import                  (colección + trozos del log + revisar, con el código ya confirmado)
        │                                          responde { pendiente }: queda pendiente, sin tocar tus mazos
        │
        └─ abre el navegador en /importar-arena?revisar=<pendiente>   (tú eliges qué mazos se guardan)
```

Sin confirmar en el navegador, nunca se llega a leer nada de Arena — no hay
motivo para sacar datos del juego que no se van a poder guardar. No queda
ningún fichero en el ordenador, acabe bien o mal.

**El log no se analiza aquí.** De cada inicio de sesión (la respuesta de
`StartHook`) se quedan los cuatro comodines y los nombres, formatos y listas de
los mazos del usuario, sin los precon de Arena ni el resto; de la sesión, las
líneas `DeckUpsertDeckV3` y `EventSetDeckV3`. Nunca el fichero entero, que
también tiene las partidas con los nombres de los rivales. Esos trozos los
analiza el servidor con `lib/mtgaLog.ts`, el mismo código que usa la web al
arrastrar el fichero, para que un cambio de formato de Arena se arregle en un
solo sitio.

Si la colección no se puede leer (Arena cerrado, el lector no arranca), se
guardan igualmente los mazos y los comodines. Si el log no tiene datos
detallados, el programa explica cómo activar «Detailed Logs (Plugin Support)».

**Nada se guarda sin elegir.** Un log trae todos los mazos de la cuenta de
Arena, a veces decenas y de hace años, y un mazo que ya exista en MTG Corner
con el mismo nombre se reescribiría entero. Por eso el servidor deja lo leído
pendiente una hora y el programa abre la página de revisión, en el idioma de
Windows: cada mazo con su casilla y una etiqueta de «nuevo» o «ya existe», y la
colección completa con la suya. Lo no marcado se descarta.

## Compilar

Se compila solo, en GitHub Actions ([`.github/workflows/build.yml`](.github/workflows/build.yml))
en cada cambio, y el resultado se publica en
[Releases](../../releases/tag/latest) — no hace falta tener el SDK de .NET
instalado para conseguir el `.exe`, sólo para tocar el código.

Para compilarlo en local: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), luego

```
dotnet publish -c Release -r win-x64 -p:SelfContained=true -p:PublishSingleFile=true -o dist
```

## Licencia

El código de este repositorio es [MIT](LICENSE). El paquete descargable
incluye además el `.exe` de `mtga-tracker-daemon`, de terceros bajo GPLv3 —
ver `LICENCIA-TERCEROS.txt` dentro del zip.
