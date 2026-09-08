🇬🇧 [English](README.md) · 🇩🇪 [Deutsch](README.de.md) · 🇪🇸 **Español** · 🇫🇷 [Français](README.fr.md)

# Aion DPS Meter

Un medidor de DPS y botín para **AION 4.6 (OriginAion)** que funciona exclusivamente a partir del
archivo `Chat.log` del juego.

Lee un archivo de texto que el cliente escribe por su cuenta. No captura tráfico de red ni lee o
escribe en el proceso del juego. No se envía nada a ninguna parte: todo se queda en tu equipo.

## Instalación

1. Descarga `AionDpsMeter.msi` desde la [última versión](../../releases/latest) y ejecútalo. Todo
   viene incluido, no necesitas tener .NET instalado.
2. Abre **Aion DPS Meter** desde el menú Inicio (el instalador también ofrece un acceso directo en
   el escritorio).
3. Ve a **Settings → App Settings** y elige tu **carpeta de instalación de Aion**: la carpeta
   raíz, la que contiene `bin64\game.dll`. El diálogo te dice al momento si ha encontrado una
   instalación válida y si ya existe allí un `Chat.log`.

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

## Idiomas del cliente

Las líneas de chat se reconocen en **inglés, alemán, francés, español y ruso**. Dos jugadores del
mismo grupo pueden usar clientes en idiomas distintos y ambos se contabilizan correctamente: el
medidor compara cada línea con las estructuras de todos los idiomas, no solo con las de tu propio
cliente.

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
