# MTG Corner — puente con MTG Arena

Programa pequeño, en C#/.NET, que lee tu colección, tus mazos y tus comodines
de MTG Arena y los sube a tu cuenta de [mtgcorner.com](https://mtgcorner.com),
donde eliges qué se guarda.

**Un único `.exe`**, sin instalar nada y sin ficheros al lado: lleva dentro el
runtime de .NET, el lector de memoria y el texto de la licencia.

## Cómo funciona

```
MtgCornerArenaBridge.exe
        │
        ├─ POST /api/mtga-device/iniciar         → un código de un solo uso
        ├─ abre el navegador en /vincular-dispositivo?codigo=...
        ├─ GET /api/mtga-device/estado            (hasta que se confirme)
        │
        │   ── sólo a partir de aquí se toca Arena ──
        │
        ├─ lee la COLECCIÓN de la memoria de Arena   (LectorArena.cs)
        ├─ lee MAZOS y COMODINES del Player.log      (LogArena, en Program.cs)
        │
        ├─ POST /api/mtga-import                  responde { pendiente }
        └─ abre /importar-arena?revisar=<pendiente>   (tú eliges qué se guarda)
```

Sin confirmar en el navegador no se lee nada de Arena: no hay motivo para sacar
datos del juego que no se van a poder guardar. No queda ningún fichero en el
ordenador, acabe bien o mal, y sólo se LEE — nunca se escribe en el juego.

**El idioma lo manda la web.** El programa habla los ocho idiomas del sitio
(`Textos.cs`). Mientras no sabe nada, usa el de Windows; al confirmar, la web le
dice en qué idioma estás jugando (`/api/mtga-device/estado`) y con ése escribe el
resto y abre la página de revisión. Antes había dos reglas en el mismo proceso
—la página de vincular salía en inglés y la de revisión en el idioma del
sistema— y se notaba a mitad de camino. Los modos de prueba siguen en inglés:
sus líneas acaban pegadas en un informe.

**La colección** sólo existe en la memoria del proceso de Arena, así que hace
falta tenerlo abierto. La lee `vendor/HackF5.UnitySpy`, la librería del proyecto
[`mtga-tracker-daemon`](https://github.com/frcaton/mtga-tracker-daemon): localiza
las estructuras de Mono en el proceso y desde ahí llega a
`WrapperController.Instance → InventoryManager → InventoryServiceWrapper → Cards`.
Si una actualización de Arena cambia esa ruta, el programa imprime un
diagnóstico que la recorre paso a paso y dice dónde se rompe.

**Los mazos y los comodines** salen del `Player.log`, sin preguntar dónde está:
Unity lo escribe siempre en
`%USERPROFILE%\AppData\LocalLow\Wizards Of The Coast\MTGA`, con el instalador de
Wizards, con Steam o con Epic. Se leen `Player-prev.log` y `Player.log`,
compartidos para no chocar con Arena abierto, y se envían sólo los trozos que
importan: comodines, nombres, formatos, fechas y listas de tus mazos. Nunca el
fichero entero, que también tiene las partidas con los nombres de los rivales.
Ese recorte lo analiza el servidor con el mismo código que la web usa al
arrastrar el fichero, para que un cambio de formato se arregle en un solo sitio.

Si la colección no se puede leer, se suben igualmente los mazos y los comodines.

## Modos de prueba

```
MtgCornerArenaBridge.exe --probar-coleccion            lee la colección y dice cuántas cartas salen
MtgCornerArenaBridge.exe --probar-log [rutas] [--salida f]   recorta un log y dice qué enviaría
MtgCornerArenaBridge.exe --licencia                    deja la GPLv3 al lado del programa
```

Ninguno conecta con mtgcorner.com ni guarda nada.

## Compilar

Se compila solo, en GitHub Actions ([`.github/workflows/build.yml`](.github/workflows/build.yml))
en cada cambio, y el resultado se publica en
[Releases](../../releases/tag/latest) — no hace falta el SDK de .NET para
conseguir el `.exe`, sólo para tocar el código.

Para compilarlo en local: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), luego

```
dotnet publish -c Release -r win-x64 -p:SelfContained=true -p:PublishSingleFile=true -o dist
```

Aviso: algunos antivirus ponen en cuarentena `HackF5.UnitySpy.dll` mientras se
compila, por ser una librería que lee memoria de otros procesos. Si la
compilación falla copiando esa DLL, es eso.

## Licencia

**GPLv3** ([LICENSE](LICENSE)), porque incluye en `vendor/HackF5.UnitySpy` el
lector de memoria de [`mtga-tracker-daemon`](https://github.com/frcaton/mtga-tracker-daemon)
(frcaton), que es GPLv3. El resto del código es de MTG Corner y va bajo la misma
licencia. El `.exe` publicado lleva el texto de la licencia dentro.
