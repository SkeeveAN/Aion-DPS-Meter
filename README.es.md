🇬🇧 [English](README.md) · 🇩🇪 [Deutsch](README.de.md) · 🇪🇸 **Español** · 🇫🇷 [Français](README.fr.md)

# Aion DPS Meter

Un medidor de DPS y botín para **AION 4.6 (OriginAion)** que funciona exclusivamente a partir del
archivo `Chat.log` del juego.

Lee un archivo de texto que el cliente escribe por su cuenta. No captura tráfico de red ni lee o
escribe en el proceso del juego. Nada de tu partida sale de tu equipo: ni daño, ni botín, ni
nombres. Lo único que envía es una comprobación de actualizaciones, que pregunta a GitHub si
existe una versión más nueva y se puede desactivar; consulta [Actualizaciones](#actualizaciones).

## Instalación

1. Descarga `AionDpsMeter-win-Setup.exe` desde la [última versión](../../releases/latest) y
   ejecútalo. No hay nada que confirmar: se instala en tu perfil de usuario y abre el medidor. Sin
   permisos de administrador y sin necesidad de .NET.
2. Ve a **Settings → App Settings** y elige tu **carpeta de instalación de Aion**: la carpeta
   raíz, la que contiene `bin64\game.dll`. El diálogo te dice al momento si ha encontrado una
   instalación válida y si ya existe allí un `Chat.log`.

> **¿Vienes de la 0.5.2 o anterior?** Desinstala primero la versión antigua (Configuración de
> Windows → Aplicaciones → *Aion DPS Meter*) y ejecuta después el nuevo instalador. Aquellas
> versiones se instalaban en `Program Files`, y por eso nunca pudieron actualizarse solas. Es un
> paso único; a partir de ahí las actualizaciones llegan por su cuenta.

### Requisito previo: el registro de chat del cliente debe estar activado

Aion solo escribe `Chat.log` si la opción interna `g_chatlog` está activada. Ese interruptor está
en el cliente del juego, no en esta herramienta: actívalo como lo harías normalmente (por ejemplo
con [ShugoConsole](https://github.com/grenadium/ShugoConsole)). **Aion DPS Meter nunca toca el
proceso del juego para esto**; si el archivo no se escribe, el medidor no tiene nada que leer.

## Uso

La grabación empieza en cuanto el medidor está abierto y hay una carpeta de Aion válida
configurada.

**Nunca se lee tu historial de chat.** Al arrancar, el medidor salta al final *actual* del
`Chat.log` y solo procesa las líneas escritas a partir de ese momento, como una grabadora recién
encendida y no como un escáner de archivos. **La pausa descarta** en lugar de aplazar: las líneas
escritas durante la pausa se omiten definitivamente, así que reanudar nunca reproduce un combate
que dejaste fuera a propósito. Tus conversaciones privadas pasadas, el chat de legión y los
susurros no se miran nunca.

### Vistas

- **Dmg** — daño por jugador, con total y DPS, iconos de clase y lista ordenable. El filtro
  **Mob/Boss** cambia la columna entre el DPS global y el **iDPS** real por objetivo (daño a un
  objetivo dividido entre el tiempo de combate compartido del grupo con él).
- **Loot** — qué ha caído y para quién: persona, objeto, cantidad y grado de rareza. Las reliquias
  cuentan además para los Puntos de Abismo de cada persona.

### Hide UI (superposición)

Convierte la ventana en pequeñas etiquetas que dejan pasar los clics y puedes dejar sobre el
juego: una por jugador, con nombre, daño y DPS. Se alterna con **Ctrl+Alt+H**, desde cualquier
sitio, para que nunca sea un viaje de ida.

### Copy

**Copy** deja en el portapapeles una clasificación de una sola línea, lista para el chat
(`Nombre 1.234.567 (890), …`). En la vista Loot genera en su lugar un resumen de botín para el
chat de Aion, y **Copy All** te da una tabla Markdown para Discord.

### Comandos dentro del juego

Escríbelos como líneas de chat normales para manejar el medidor sin salir del juego:

| Comando | Efecto |
|---|---|
| `.ui` | alternar la superposición Hide UI |
| `.pause` / `.resume` | detener / continuar la grabación |
| `.dmg` | copiar la clasificación de daño al portapapeles |
| `.cleardmg` | borrar la sesión actual |
| `.loot` | copiar el resumen de botín al portapapeles |

Solo los personajes que hayas registrado en los ajustes pueden lanzarlos, de modo que un
`.cleardmg` escrito por un desconocido en un canal que ni siquiera estás leyendo no puede borrar
tu sesión.

## Actualizaciones

El medidor se actualiza solo. Pregunta a GitHub al arrancar y cada cinco minutos, descarga la
nueva versión en segundo plano y la deja lista para el siguiente arranque. Sin instalador que
ejecutar, sin aviso de UAC, sin nada que pulsar. Funciona porque vive en tu perfil de usuario y no
en `Program Files`: ahí sí puede reemplazar sus propios archivos.

Cuando una actualización está lista aparece una línea verde en la barra de estado inferior; al
pulsarla se te ofrece reiniciar en ese momento. Rechazar no cuesta nada: la versión ya está
descargada y se aplicará en el siguiente arranque normal. No hay ventanas emergentes a propósito:
el programa está encima de un juego en marcha y un diálogo que roba el foco en mitad de un jefe es
peor que una actualización tardía.

**App → Check for updates** hace lo mismo cuando tú lo pides y también te dice si ya estás al día.

La comprobación lee una sola URL y no envía nada más que la propia petición:

```
https://api.github.com/repos/SkeeveAN/Aion-DPS-Meter/releases
```

Se desactiva en **Settings → App Settings → Updates**. La entrada de menú sigue funcionando: esa
la pides tú, no la decide el programa.

## ¿Cuánta precisión tiene?

Validado contra tres archivos `Chat.log` de la *misma* incursión a la Base de Suministros de
Sauro, grabados en tres PC distintos (uno de ellos un cliente alemán), tomando como referencia los
PV reales de los jefes. El daño infligido a cada jefe queda dentro del **0,02 % – 2,8 %** de sus
PV reales:

| Jefe | PV reales | Medido | Desviación |
|---|---|---|---|
| Guard Captain Ahuradim | 1.736.993 | 1.737.299 | +0,02 % |
| Derakanak the Reaver | 1.343.657 | 1.347.258 | +0,27 % |
| Archmagus Sayahum | 1.377.644 | 1.370.734 | −0,50 % |
| Commander Ranodim | 489.332 | 503.244 | +2,84 % |

El resto es el exceso del golpe mortal, que ningún medidor basado en registros puede ver. El total
de un jugador salió idéntico hasta la unidad en las tres máquinas.

Dos desviaciones conocidas e inofensivas: el daño que absorbe el escudo de un jefe se registra
igualmente (por eso parece superar sus PV), y varios enemigos que comparten nombre se suman
juntos.

## Compilar desde el código fuente

```
dotnet build
dotnet run -- selftest                      # autocomprobaciones del parser y del cálculo de DPS
dotnet run -- chatlog <ruta-al-Chat.log>    # analizar un archivo e imprimir el resumen
```

Solo Windows (WPF). Las autocomprobaciones ejecutan líneas literales de registros reales en todos
los idiomas admitidos y son la forma más rápida de ver si un cambio en el parser ha roto algo.

## Nota sobre las reglas del servidor

Esta herramienta solo lee un archivo de registro que genera el propio juego. Aun así, los
servidores privados fijan sus propias reglas sobre herramientas de terceros y addons: conviene
revisar las de OriginAion antes de usarla.
