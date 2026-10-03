🇬🇧 [English](README.md) · 🇩🇪 [Deutsch](README.de.md) · 🇪🇸 **Español** · 🇫🇷 [Français](README.fr.md) · 🇷🇺 [Русский](README.ru.md) · 🇵🇱 [Polski](README.pl.md) · 🇹🇷 [Türkçe](README.tr.md) · 🇨🇳 [中文](README.zh.md)

# Aion DPS Meter

Sitio web con clasificaciones comunitarias de jefes y perfiles de personajes: **https://aiondps.com**

Un medidor de daño/curación para **Aion 2**. Lee el tráfico de red del juego en tu equipo mediante el controlador
[Npcap](https://npcap.com) — de forma pasiva: nunca envía un paquete y no toca el proceso del juego ni su memoria.
Nada de tu partida sale de tu equipo salvo que lo subas (ver [Subidas](#subidas)); además envía la comprobación de
actualizaciones, que pregunta a GitHub si hay una versión más reciente y se puede desactivar; ver
[Actualizaciones](#actualizaciones).

> El Aion clásico (basado en Chat.log) ya no forma parte del medidor. La última versión que lo admite está en la
> rama [`aion1-included`](../../tree/aion1-included).

## Instalación

1. Instala el controlador [Npcap](https://npcap.com) (el medidor lo necesita para ver el tráfico del juego; no
   forma parte del instalador).
2. Descarga `AionDpsMeter-win-Setup.exe` de la [última versión](../../releases/latest) y ejecútalo. Se instala en
   tu perfil de usuario y arranca el medidor — sin permisos de administrador, sin .NET.
3. Inicia el medidor y entra con tu personaje. El medidor lee tu personaje, equipo, habilidades y tableros de
   Daevanion del propio juego; el servidor se detecta automáticamente.

## Uso

La grabación empieza en cuanto el medidor está en marcha. **La pausa descarta** en lugar de aplazar: los eventos
durante una pausa se pierden, y al reanudar nunca se repite un combate que dejaste pasar.

### Vistas

- **Dmg** — daño por jugador, con total y DPS, iconos de clase y lista ordenable. El filtro **Mob/Boss** cambia la
  columna entre DPS global e **iDPS** real por objetivo. Los jefes se reconocen por los datos del juego y se
  muestran con su nombre. **Doble clic** en un jugador para el desglose de habilidades.
- **Personaje** (el icono de persona) — abre una ventana con tu propio personaje: perfil, equipo con nivel de objeto
  y encantamiento, habilidades con niveles y los tableros de Daevanion. Guarda tu último inicio de sesión, así que
  nunca está vacía.

### Hide UI (overlay)

Convierte la ventana en pequeñas fichas transparentes al clic sobre el juego — una por jugador con nombre, daño y
DPS. Se alterna con **Ctrl+Alt+H**, desde cualquier sitio.

### Copiar

**Copy** pone en el portapapeles una clasificación de una línea lista para el chat (`Nombre 1.234.567 (890), …`);
**Copy All** da una tabla Markdown para Discord.

### Atajos de teclado

Valen en todas partes, también con el juego en primer plano (se cambian en **Ajustes → Interfaz → Atajos de teclado**): **Ctrl+Alt+H** overlay, **Ctrl+Alt+P** pausa / reanudar, **Ctrl+Alt+D** copiar la clasificación, **Ctrl+Alt+Mayús+X** vaciar la sesión. Sustituyen a los comandos de chat, que no pueden funcionar porque el chat de Aion 2 no se puede leer en el tráfico de red.

## Subidas

- **Los combates contra jefes** se suben al pulsar subir (botón o menú Session): jefe, jugadores participantes, daño,
  curación, daño recibido y habilidades. Solo se aceptan jefes que el juego anunció y el catálogo conoce.
- **Tu propio perfil de personaje** (nombre, clase, nivel, equipo, habilidades, Daevanion, legión, servidor) se sube
  automáticamente unos segundos después de iniciar sesión, para que te encuentren en el sitio web. Se desactiva en
  **Ajustes**.
- Sin subida, nada sale de tu equipo.

## Actualizaciones

El medidor se actualiza solo. Pregunta a GitHub por una versión más reciente al arrancar y cada cinco minutos, la
descarga en segundo plano y la aplica en el siguiente arranque — sin instalador ni UAC. Cuando hay una actualización
lista aparece una línea verde; al pulsarla se ofrece reiniciar enseguida. **App → Check for updates** hace lo mismo
a petición.

La comprobación lee una sola URL y no envía nada salvo la propia petición:

```
https://api.github.com/repos/SkeeveAN/Aion-DPS-Meter/releases
```

Se desactiva en **Ajustes → Actualizaciones**; la opción de menú sigue funcionando.

## Compilar desde el código fuente

```
cd Client
dotnet build
dotnet run -- selftest                              # autopruebas (protocolo, captura, decodificación, ...)
dotnet run -- aion2-record <out.jsonl>              # grabar el tráfico del juego ("stop" lo termina)
dotnet run -- aion2-replay <archivo.jsonl>          # reproducir una grabación con el decodificador real
dotnet run -- aion2-upload-dryrun <archivo.jsonl>   # construir las subidas de una grabación sin enviar nada
```

Solo Windows (WPF). `Tools/aion2-dat` lee las tablas de texto del juego (nombres en ocho idiomas); ver su README.

## Nota sobre las reglas de los servidores

El medidor solo observa pasivamente el tráfico de red del juego. Aun así, las editoras fijan sus propias reglas sobre
herramientas de terceros — conviene revisar las condiciones del juego antes de usarlo.

## Aviso legal y licencia

Aion 2 y todos los nombres, textos y gráficos relacionados son propiedad de NCSOFT. Este proyecto no está afiliado, respaldado ni conectado con NCSOFT; el contenido del juego pertenece a su propietario. El código fuente se publica bajo la [Licencia MIT](LICENSE): la licencia cubre solo el código, no el contenido del juego.
