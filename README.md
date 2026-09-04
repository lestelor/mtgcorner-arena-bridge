# MTG Corner — puente con MTG Arena

Programa pequeño, en C#/.NET, que lee tu colección de MTG Arena mientras el
juego está abierto y escribe un fichero para importarla en
[mtgcorner.com/importar-arena](https://mtgcorner.com/importar-arena).

## Cómo funciona

Este programa **no lee memoria de nada por su cuenta**. Arranca
[`mtga-tracker-daemon`](https://github.com/frcaton/mtga-tracker-daemon) —de
terceros, código abierto, GPLv3— que es quien de verdad hace la lectura de
memoria de Arena en modo sólo lectura, le pide la colección por su servidor
HTTP local, y traduce la respuesta al formato que espera la web.

```
MtgCornerArenaBridge.exe
        │
        ├─ arranca ─▶ mtga-tracker-daemon.exe -p 9000
        │
        ├─ GET http://127.0.0.1:9000/status   (espera a que detecte Arena)
        ├─ GET http://127.0.0.1:9000/cards    ({ grpId, owned }[])
        │
        └─ escribe  ~/Desktop/mtgcorner-arena-AAAA-MM-DD-HHmm.json
```

Los comodines y los mazos ya se leen bien directamente del `Player.log` de
Arena — ese análisis vive en el código del propio sitio web, no aquí. Este programa cubre sólo
lo que el log no siempre trae: la colección completa.

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
