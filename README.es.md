🇬🇧 [English](README.md) · 🇩🇪 [Deutsch](README.de.md) · 🇪🇸 **Español** · 🇫🇷 [Français](README.fr.md) · 🇷🇺 [Русский](README.ru.md) · 🇨🇳 [中文](README.zh.md) · 🇹🇼 [繁體中文](README.zh-TW.md) · 🇧🇷 [Português (Brasil)](README.pt.md) · 🇯🇵 [日本語](README.ja.md) · 🇰🇷 [한국어](README.ko.md)
# Aion DPS – Daevas Power Suite

**La aplicación complementaria gratuita y de código abierto para AION 2** — daño y curación en vivo de todo tu grupo, una superposición que no intercepta clics, clasificaciones de jefes, un mapa de mascotas y recolección, un calendario de eventos y temporizadores de jefes de mundo. Para Windows, de código abierto y nunca lee la memoria del juego.

<p align="center">
  <a href="../../releases/latest"><b>⬇ Descargar para Windows</b></a> · <a href="https://aiondps.com">Sitio web</a> · <a href="https://aiondps.com/features">Todas las funciones</a> · <a href="https://aiondps.com/instances">Clasificaciones de jefes</a>
  <br><a href="https://aiondps.com">aiondps.com</a> · <a href="https://daevasuite.com">daevasuite.com</a> · <a href="https://daevaspowersuite.com">daevaspowersuite.com</a>
</p>

<p align="center">
  <a href="../../releases/latest"><img src="https://img.shields.io/github/v/release/SkeeveAN/Aion-DPS-Meter?label=release" alt="release"></a>
  <a href="../../releases"><img src="https://img.shields.io/github/downloads/SkeeveAN/Aion-DPS-Meter/total?label=downloads" alt="downloads"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue" alt="MIT"></a>
  <a href="../../actions/workflows/ci.yml"><img src="https://github.com/SkeeveAN/Aion-DPS-Meter/actions/workflows/ci.yml/badge.svg" alt="tests"></a>
  <img src="https://img.shields.io/badge/platform-Windows-lightgrey" alt="Windows">
</p>

<p align="center">
  <img src="Web-Frontend/images/features/meter@2x.webp" width="440" alt="La ventana del medidor: cada jugador con icono de clase, DPS, daño total y una barra de porcentaje">
  <img src="Web-Frontend/images/features/overlay-chips@2x.webp" width="320" alt="El overlay transparente al clic como pequeñas fichas sobre el juego">
</p>

> Los nombres, jugadores y cifras de las imágenes siguientes son datos de demostración.

## Por qué usarlo

- DPS, iDPS y HPS en vivo de cada jugador de tu grupo o banda
- Una superposición que atraviesa los clics
- Desglose de habilidades con tasas de crítico y curva de DPS
- Clasificaciones de jefes y perfiles públicos de personajes en aiondps.com (subida opcional, desactivada por defecto)
- Mapa interactivo, calendario de eventos, temporizadores de jefes de mundo y superposición de red
- Lanzamientos de jefes anunciados por voz, un registro de botín, una herramienta de atributos, una calculadora de crafteo y una lista de tareas diarias
- Pasivo: solo captura de paquetes, sin lectura de memoria, sin inyección, nada se envía al juego
- Gratuito, de código abierto (MIT) y se actualiza solo

Funciona con la versión global de AION 2: todas las regiones que NCSOFT lanzó el 5 de octubre de 2026. Corea y Taiwán van varias versiones por delante, por lo que allí el reconocimiento de habilidades puede ser incompleto.

Un medidor de daño/curación y kit de herramientas para **Aion 2**. Lee el tráfico de red del propio juego en tu equipo
mediante el controlador [Npcap](https://npcap.com) — de forma pasiva: nunca envía un paquete y no toca el proceso del
juego ni su memoria. Nada de tu partida sale de tu equipo salvo que lo subas (ver
[Subidas](#subidas)); lo único que envía además es una comprobación de actualizaciones, que pregunta a GitHub si existe
una versión más reciente y se puede desactivar; ver [Actualizaciones](#actualizaciones).


## Contenido

[Instalación](#instalación) · [Qué hace](#qué-hace) · [Uso](#uso) · [Atajos de teclado](#atajos-de-teclado) ·
[Subidas](#subidas) · [Actualizaciones](#actualizaciones) · [Idiomas](#idiomas) · [Compilar desde el código fuente](#compilar-desde-el-código-fuente)

## Instalación

1. Instala el controlador [Npcap](https://npcap.com) (el medidor lo necesita para ver el tráfico del juego; no
   forma parte del instalador).
2. Descarga `AionDpsMeter-win-Setup.exe` de la [última versión](../../releases/latest) y ejecútalo.
   Se instala en tu perfil de usuario y arranca el medidor — sin permisos de administrador, sin .NET.
3. Inicia el medidor y entra con tu personaje. El medidor lee tu personaje, equipo, habilidades y tableros de
   Daevanion del propio juego; el servidor en el que juegas se detecta automáticamente.

## Qué hace

### Daño y curación en directo de todos

Cada jugador de tu grupo o raid con nombre, daño total, DPS y una barra de porcentaje; la facción y la clase se
muestran como iconos. Filtra por clase, grupo u objetivo. El filtro **Mob/Boss** cambia entre el DPS global y el
**iDPS** real por objetivo; los jefes se reconocen por los datos del propio juego y se muestran con su nombre. Un
interruptor **JcE/JcJ** separa ambos, y un wipe contra un jefe recibe una entrada propia (una calavera marca un
intento tras el cual el jefe volvió a tener la vida completa).

### Cinco vistas, a un clic de distancia

**Daño**, **Curación**, **Daño recibido** y dos vistas **Todo en uno**. Cambia con el atajo
(por defecto **Ctrl+Alt+M**) o pulsando la etiqueta del modo en el overlay. *Todo en uno* muestra el daño recibido,
la curación y el total uno al lado del otro; *All in One Compact* mantiene las conocidas columnas de DPS y total y
coloca la curación y el daño recibido en una pequeña segunda línea bajo el nombre.

<p align="center">
  <img src="Web-Frontend/images/features/mode-all@2x.webp" width="460" alt="Vista Todo en uno: daño recibido, curación y daño total uno al lado del otro">
  <img src="Web-Frontend/images/features/overlay-chips@2x.webp" width="380" alt="El overlay transparente al clic como pequeñas fichas sobre el juego">
</p>

### Un overlay transparente al clic

**Hide UI** convierte la ventana en pequeñas fichas (o un panel compacto) que se sitúan sobre Aion 2 sin
interceptar ni un solo clic. La opacidad, las barras de porcentaje de daño, la vida del jefe y el daño recibido por
jugador son opciones. El panel compacto puede, opcionalmente, dejar pasar los clics del ratón al juego. El overlay solo se dibuja mientras el juego está en primer plano. Actívalo desde cualquier sitio con un atajo.

Opcional (**Ajustes → Interfaz → Ocultar durante los menús del juego**, desactivado por defecto): el medidor y sus overlays se apartan mientras hay un menú del juego abierto (mapa, inventario, legión, tienda, Esc ...) y vuelven al cerrarlo. Funciona por el cursor del ratón: en el modo de control que oculta el cursor mientras juegas, el juego lo muestra al abrir un menú; mantener pulsada la tecla de mostrar el cursor (Alt por defecto) mantiene visible el medidor para que puedas hacer clic en él, y **Ctrl+Alt+J** muestra los overlays durante el menú abierto. En el modo de control clásico el cursor siempre se muestra, por lo que los menús no se pueden distinguir; el medidor lo detecta mientras combates y simplemente se queda visible.

### Desglose de habilidades de cada jugador

**Doble clic** en un jugador: todas las habilidades con golpes, tasa de crítico, total, mín., máx. y media,
desglosadas por objetivo, además de una curva de DPS de todo el combate. Las tasas de crítico se leen directamente de
los datos de golpe del servidor, así que son exactas. La tabla también muestra cómo han impactado los golpes de una habilidad: **Espalda**, **Frente**, **Perfecto**, **Doble** y **Multi** (varios ataques en un solo golpe) como porcentaje de sus golpes directos, leído de los datos de golpe. Doble (aproximadamente el doble de daño), Perfecto (una pequeña bonificación) y Parada se comprobaron con el daño que causan; Espalda y Frente proceden de un byte de dirección que aún no se ha verificado en el juego, y la ventana lo indica.

<p align="center">
  <img src="Web-Frontend/images/features/details@2x.webp" width="640" alt="Desglose de habilidades de un jugador con una curva de daño a lo largo del tiempo">
</p>

### Rotación, efectos y stagger

La misma ventana tiene dos pestañas más. **Rotación** dibuja en una línea de tiempo cada habilidad que usó el jugador, en el momento en que la usó: las habilidades de apoyo en el carril superior, las de daño debajo, y una habilidad intercalada justo después de otra, una fila más abajo, de modo que una serie de intercalados se lee como una escalera. Detrás corre el DPS del jugador (el promedio de los últimos cinco segundos) y los momentos en que un monstruo quedó en stagger. Arrastra para moverte por el combate, **Ctrl + rueda** hace zoom y, al pasar el cursor sobre un icono, ves el uso (cuándo, qué uso de cuántos, el tiempo desde el anterior, el DPS en ese momento). Debajo están la **Apertura** (las primeras habilidades en orden) y la *rotación en breve*: Usos, Por min, Intervalo entre dos usos, Primero y la parte del daño. **Efectos** enumera cada efecto que tuvo el jugador con su tiempo activo, la parte que provino del propio jugador, cuántas veces se aplicó y quién lo dio; un efecto que dos jugadores mantuvieron a la vez cuenta una sola vez.

Las sesiones guardadas en un archivo con los datos de rotación (véase Sesiones) los muestran de nuevo al cargarlas.

<p align="center">
  <img src="Web-Frontend/images/features/rotation@2x.webp" width="640" alt="La pestaña Rotación de la ventana del jugador: habilidades en una línea de tiempo con el DPS detrás, la apertura y la rotación en breve">
</p>

### Repetición de un combate contra un jefe (prueba temprana)

En la ventana de jugador de un combate contra un jefe, **Repetición** reproduce el combate desde arriba: tu grupo como puntos del color de su clase con los últimos segundos de su recorrido, el jefe con la dirección hacia la que mira y su salud, las áreas de las habilidades que está preparando (ámbar mientras las prepara, rojo cuando caen; discontinuas mientras el tamaño aún es una estimación), un anillo rojo sobre a quien golpeó, los muertos tachados. Bajo el mapa, una línea de tiempo muestra los grandes ataques del jefe (ámbar), los golpes al grupo (rojo), las muertes, los stagger (sombreados) y la salud del jefe; haz clic o arrastra sobre ella para ir a ese momento. Reproducir y pausa (Espacio), velocidades de la mitad a cuatro veces, ← y → avanzan o retroceden cinco segundos, arrastra el mapa, rueda para hacer zoom, doble clic para volver a ajustarlo.

Las posiciones son las lecturas propias del juego: las envía con los movimientos y las habilidades, no a intervalos regulares, por eso un recorrido se dibuja recto entre dos de ellas. El juego nunca dice qué tamaño tiene el área de un ataque: el medidor aprende el tamaño de cada habilidad según a quién golpeó y lo recuerda entre combates. Solo se muestran tu grupo y los jefes, nunca otros jugadores. Un combate conserva su repetición en el historial local; los combates grabados con una versión anterior no tienen ninguna.

<p align="center">
  <img src="Web-Frontend/images/features/replay@2x.webp" width="640" alt="La repetición de un combate contra un jefe: el grupo alrededor del jefe, un área en preparación, la línea de tiempo con la salud del jefe">
</p>

### Todo tu personaje de un vistazo

La ventana **Personaje** (icono de persona) muestra lo que el juego envía sobre ti: equipo con nivel de objeto y
encantamiento (incluida la fase de Amplificación por encima de +15), arcanos, conocimiento de especies, todas las
habilidades con sus niveles y los tableros de Daevanion completos. Guarda tu último inicio de sesión, así que nunca
está vacía. Pasa el ratón sobre una pieza para ver sus detalles (piedras, atributos obtenidos, piedra divina).

El medidor también detecta cuál de tus **presets de habilidades** está activo y conserva todos los que ha visto: niveles de habilidad, stigmas y piedras. La pestaña de habilidades muestra cada preset con las piedras de sus habilidades, y la descripción emergente de una habilidad enumera las piedras y los efectos que desbloquea un stigma, con las palabras del propio juego.

<p align="center">
  <img src="Web-Frontend/images/features/character-gear@2x.webp" width="640" alt="La ventana Personaje con equipo, niveles de objeto y encantamiento">
</p>

### Mapa interactivo con mascotas y recolección

Un overlay de mapa muestra tu posición, los puntos de aparición de las mascotas que buscas y los puntos de
recolección que activaste — hierbas, minerales, madera, gemas y más — sin salir del juego a una página web. Solo se
listan los recursos que puede usar tu propia facción, y la última posición conocida permanece en el mapa mientras el
juego está cerrado. Los ajustes de mascotas listan cada región de Verteron, Altgard y el Abismo con sus mascotas y tu
propio nivel de mascota, además de una pestaña **Raros** para mascotas con pocos puntos de aparición. Unas flechas en
el borde señalan la mascota o el recurso recolectable más cercano. Dentro de una mazmorra o instancia de grupo, el mapa cambia a esa instancia (25 mapas a partir de las imágenes del propio juego, para las instancias que tienen monstruos de mascota) y muestra sus monstruos de mascota.

<p align="center">
  <img src="Web-Frontend/images/features/map@2x.webp" width="520" alt="El mapa interactivo con puntos de aparición de mascotas y la lista de mascotas">
  <img src="Web-Frontend/images/features/gathering@2x.webp" width="230" alt="Ajustes de recolección: activa lo que aparece en el mapa">
</p>

### Horario de eventos con recordatorios

Un overlay lista los próximos eventos — Campo de batalla, los asedios del Abismo (Asedio de artefactos), el Festival
Shugo, el Ataque aéreo de Beritra, la Grieta espaciotemporal y más. Elige un sonido y un volumen por evento y recibe
un aviso unos minutos antes de que empiece. Un interruptor activa o desactiva todos los recordatorios. Las horas siguen el reloj UTC del servidor del juego, así que son las mismas en todas las regiones y en horario de verano e invierno, y se muestran en la hora de tu PC. También figuran los jefes ejecutores del Abismo (lun, jue, sáb) y la Señora Guardiana Nahma (vie, dom), y hay diez sonidos de notificación para elegir.

<p align="center">
  <img src="Web-Frontend/images/features/timetable@2x.webp" width="380" alt="Ajustes de horarios con un sonido y un volumen por evento">
</p>

### Jefes del mundo en tu overlay

Activa un jefe de campo para ver su temporizador en el overlay y oír su sonido en cuanto aparece. Los tiempos
provienen directamente del juego; el servidor los envía para el mapa en el que estás, y los demás mapas muestran los
suyos una vez que has estado allí. El sonido y el volumen se ajustan por jefe, agrupados por región.

<p align="center">
  <img src="Web-Frontend/images/features/worldboss@2x.webp" width="380" alt="Ajustes de jefes de mundo: cada jefe de campo por mapa con su tiempo de reaparición, un interruptor, sonido y volumen">
</p>

### Overlay de red

Ping, jitter, tráfico, pérdida de paquetes y cuánto tiempo llevas conectado, medidos a partir del tráfico que tu PC
ya recibe. Los valores pasan de verde a rojo a medida que la conexión empeora. Elige las líneas que quieras,
fíjalo en su sitio y actívalo o desactívalo con un atajo.

<p align="center">
  <img src="Web-Frontend/images/features/network-overlay@2x.webp" width="340" alt="El overlay de red: ping, jitter, tráfico y pérdida de paquetes">
</p>

### Calculadora de stats (beta)

Mira qué efecto tiene un cambio de stats en tu daño antes de cambiar una pieza. Parte de tus propios stats tal como los envía el juego; indicas desde qué lado atacas sobre todo (frente, espalda, no lo sé) y tu tasa de crítico, o tomas ambos de tu último combate. Escribe un cambio en cualquier stat y lee el daño resultante frente al daño actual, además de cuánto de cada stat da un uno por ciento más. El modo experto añade buffs y la resistencia del jefe y sigue en beta. El resultado es una estimación, porque la fórmula de daño del juego no está publicada: dice «3,8 % más», nunca «tanto daño». **Iniciar grabación** anota tus stats y los golpes de un combate hasta 10 minutos; **Enviar** los sube tras mostrar exactamente qué sale - sin nombre, sin legión, sin dirección IP, solo un identificador aleatorio de tu instalación - para poder mejorar la calculadora. La idea viene del Stat Efficiency Calculator de [RATmeter](https://github.com/Kuroukihime/AIon2-Dps-Meter); el diseño, el código y la fórmula son nuestros.

### Informe de red

En **Herramientas → Red**: **Guardar informe** escribe los últimos 15 minutos de tu conexión (ping, jitter, pérdida de paquetes, tiempos de respuesta del router, internet y servidor del juego, pausas y su causa, tipo de adaptador) en un archivo JSON con formato llamado `aiondps_VERSION_FECHA_HORA.json` donde elijas; **Enviar informe** envía el mismo informe al desarrollador. No contiene ninguna dirección IP tuya ni nombres (ni del PC, la cuenta de Windows, un adaptador, un personaje o una legión); antes de enviar, un cuadro enumera qué contiene y qué no. La idea de un informe de diagnóstico guardado viene del diagnóstico de entorno del medidor Aletheia.

### Overlay de combate

Un marco transparente con hasta tres elementos, cada uno de los cuales colocas y dimensionas por separado:

- **Barra de lanzamiento del jefe:** en cuanto un jefe empieza una habilidad, ves su nombre, una barra que va bajando con el tiempo restante y a quién apunta (tú en rojo). Una habilidad que ha golpeado a varios jugadores se marca como **ÁREA**; **LETAL** significa que el golpe más alto que has recibido de ella hasta ahora es al menos igual a tu salud actual. El medidor aprende cada habilidad (si es de área o no, cuándo llega su daño) y la recuerda entre sesiones.
- **Objetivo del objetivo:** un bloque que se mueve, cambia de tamaño y se escala de una vez, con partes que también puedes colocar por separado dentro de él. Si el objetivo que seleccionaste con Tab apunta a su vez a alguien, ves a quién: un jefe con su imagen y nombre, un jugador con el emblema de clase y nombre. La **barra del sanador** (opcional) añade al bloque la barra de salud de ese objetivo y sus debuffs (o buffs).

Desbloquea el overlay en **Ajustes → Combate contra jefes** para organizarlo: arrastra el cuerpo de un elemento para moverlo, tira de sus bordes para cambiar el tamaño, tira de su esquina para escalarlo; el asa de la esquina inferior derecha de todo el marco escala todos los elementos a la vez. Bloqueado, deja pasar los clics y solo se muestra mientras el juego está en primer plano. La voz de Windows elegida también puede **anunciar por voz los lanzamientos de jefes** (con o sin el nombre de la habilidad, dirigidos a ti, a todos, letales), incluso con el overlay desactivado mientras Aion está en primer plano; los ajustes tienen frases de ejemplo para escuchar. **Ctrl+Alt+C** lo activa y desactiva, y también lo hace el atajo *Todos los overlays sí / no*.

<p align="center">
  <img src="Web-Frontend/images/features/combat-overlay@2x.webp" width="480" alt="El overlay de combate contra jefes: dos lanzamientos de jefe con barra de tiempo y el bloque «Objetivo del objetivo» con imagen, nombre, barra de vida y debuffs">
</p>

### Botín, atributos y la lista de tareas diarias

La ventana **Herramientas** los enumera a la izquierda. La herramienta **Botín** muestra lo que recoges del suelo, por sesión y mapa, como página y como overlay (**Ctrl+Alt+L**). La herramienta **Atributos** muestra el anillo de atributos con tus puntos, lo que hace cada atributo y valor de Señor (0,1 % por punto), todas las bonificaciones sumadas, y una pestaña de estadísticas que explica cada estadística con su parte procedente del anillo. La **lista de tareas** guarda tus tareas diarias y semanales, lee del juego las misiones diarias obligatorias y la compra semanal de energía de Odyle, y deja el resto para marcar a mano. Cada herramienta recuerda su propio ancho y alto.

### Calculadora de crafteo

Marca las recetas que quieres fabricar y cuántas veces (para toda la cuenta, así tus personajes secundarios también las ven, o para un solo personaje), y la **calculadora de crafteo** enumera, receta por receta, qué materiales aún te faltan. Los productos intermedios se descomponen en lo que realmente tienes que recoger: tres martillos de herrería necesitan 18 lingotes, es decir, 36 de mineral, y los lingotes que ya tienes cuentan primero. El inventario se lee del juego, del inventario, del almacén del personaje y del almacén de la cuenta, y un material que está en un almacén lo indica. Deja la página abierta en una segunda pantalla o muestra la misma lista como overlay en el juego (**Ctrl+Alt+R**). Las recetas proceden de las tablas del propio juego; el orden de tu lista decide qué receta recibe primero las existencias. Inicia sesión una vez para que se lean las existencias; lo que muevas entre el inventario y un almacén aparece tras la siguiente ordenación.

### Anuncios y Twitch Drops

Un botón con un megáfono abre los anuncios oficiales de NCSoft en aiondps.com — mantenimientos, caídas, nuevos
servidores, traslados de servidor, compensaciones — los más recientes primero, con una etiqueta de color por tipo y
un filtro. Mientras haya una campaña de Twitch Drops de AION 2 en marcha, un botón de Twitch abre una página con sus
recompensas, las horas que tienes que ver, una cuenta atrás y los streams que tienen los Drops activados ahora mismo.
Una marca *Nuevo* indica cuándo hay algo más reciente que tu última visita.

<p align="center">
  <img src="Web-Frontend/images/features/announcements@2x.webp" width="430" alt="Anuncios oficiales de AION 2 con filtro por tipo">
  <img src="Web-Frontend/images/features/twitchdrops@2x.webp" width="430" alt="Campañas de Twitch Drops con recompensas y streams en directo">
</p>

### El medidor en tu stream (OBS, Streamlabs)

Muestra el medidor en directo sobre tu juego sin grabar su ventana: activa **Ajustes → Streaming → Overlay de stream para OBS activado**, usa después **Copiar enlace para OBS** (o, en el icono de la bandeja, *Copiar enlace del overlay de stream (OBS)*), añade una fuente de **Navegador** en OBS o Streamlabs, pega el enlace y ajusta el tamaño de la fuente. La página sigue al medidor en directo: la vista actual (daño, curación, daño recibido, todo en una), los jugadores con icono de clase y barras, y la salud del jefe. Añade `?scale=1.5` o `?rows=5` al enlace para agrandarla o mostrar menos filas.

También funciona con **dos PC** (el juego en uno, OBS en el otro): copia el enlace al PC de streaming, contiene la dirección del PC del juego en tu red. Solo tu propia red puede acceder, nunca internet; el enlace lleva una clave aleatoria (**Nueva clave** invalida el enlace anterior), solo se sirve `GET`, la página no carga nada de otro sitio y no se envía nada a nadie. Windows puede preguntar una vez si permite el medidor en el firewall: permítelo para redes privadas. Los nombres de los jugadores están **desactivados por defecto** - todos aparecen con su clase, como en las subidas - y se pueden activar en la misma tarjeta de ajustes.

### Sesiones

Cada mapa e instancia se graba como una sesión propia, para volver a ver una partida anterior. Una comprobación de plausibilidad compara el daño con los puntos de vida que perdió el jefe.

<p align="center">
  <img src="Web-Frontend/images/features/sessions@2x.webp" width="760" alt="Gestor de sesiones y analizador de combate en una ventana">
</p>

El gestor de sesiones y el **analizador de combate** comparten una ventana con un separador móvil: elige una sesión a la izquierda y a la derecha se abren los detalles del jugador (habilidades, rotación, efectos, el mapa de repetición), limitados a esa sesión. Sobre la lista de jugadores cambias entre **daño, curación y daño recibido**; la lista muestra las muertes y cuánto tiempo estuvo muerto cada jugador. El medidor sigue grabando en segundo plano. **Guardar en archivo** puede llevarse la rotación, los efectos y los datos de repetición (una casilla que se recuerda; el archivo se hace mucho más grande, unos 0,5 a 5 MB en un combate de jefe de cinco minutos), de modo que un archivo cargado también los muestra. La ventana recuerda su posición y el separador.

## Uso

La grabación empieza en cuanto el medidor está en marcha. **La pausa descarta** en lugar de aplazar: los eventos
durante una pausa se pierden para siempre, así que al reanudar nunca se repite un combate que dejaste pasar.

- **Copy** pone en el portapapeles una clasificación de una línea lista para el chat (`Nombre 1.234.567 (890), …`);
  **Copy All** te da una tabla Markdown para Discord.
- La apariencia se ajusta en **Ajustes → Interfaz**: tema (oscuro/claro), tamaño de fuente, idioma, aspecto y
  opacidad del overlay, inicio con Windows.
- Tus ajustes sobreviven a un fallo: el archivo de ajustes se escribe de una sola vez y junto a él se guarda una copia del último inicio. Si el archivo llega a dañarse, se restaura esa copia (el archivo dañado se conserva al lado). **Ajustes → General → Restablecer todos los ajustes** pregunta dos veces y se puede deshacer durante 30 segundos; tus decisiones de subida y privacidad, la comprobación de actualizaciones y tu personaje se quedan como están.

<p align="center">
  <img src="Web-Frontend/images/features/settings@2x.webp" width="340" alt="Ajustes: interfaz, overlay y comportamiento">
  <img src="Web-Frontend/images/features/hotkeys@2x.webp" width="340" alt="Ajustes: atajos de teclado">
</p>

## Atajos de teclado

En todo el sistema, también mientras el juego tiene el foco. Haz clic en un campo en **Ajustes → Atajos de teclado**,
pulsa la combinación que quieras (necesita Ctrl, Alt, Mayús o Win); **×** la quita. Sustituyen a los comandos de
chat, que no pueden funcionar porque el chat de Aion 2 no se puede leer en el tráfico de red.

| Por defecto | Acción |
|---|---|
| **Ctrl+Alt+H** | Overlay (ocultar la ventana, mostrar fichas) |
| **Ctrl+Alt+P** | Pausa / reanudar |
| **Ctrl+Alt+D** | Copiar la clasificación de daño |
| **Ctrl+Alt+Shift+X** | Vaciar la sesión |
| **Ctrl+Alt+U** | Subir el daño del jefe |
| **Ctrl+Alt+M** | Siguiente modo (cinco vistas) |
| **Ctrl+Alt+T** | Overlay de horarios |
| **Ctrl+Alt+K** | Mapa interactivo sí / no |
| **Ctrl+Alt+E** / **Ctrl+Alt+G** | Flechas de borde para mascotas / recolectables |
| **Ctrl+Alt+B** | Overlay de jefes mundiales |
| **Ctrl+Alt+N** | Overlay de red |
| **Ctrl+Alt+C** | Overlay de combate |
| **Ctrl+Alt+L** | Overlay de botín |
| **Ctrl+Alt+R** | Overlay de crafteo |
| **Ctrl+Alt+J** | Mostrar overlays durante este menú |
| **Ctrl+Alt+O** | Superposición de atajos (una lista movible de los atajos) |
| **Ctrl+Alt+A** | Todos los overlays sí / no |

## Subidas

- **Los combates contra jefes** se suben al pulsar subir (botón de la barra de herramientas, menú Sesión o
  **Ctrl+Alt+U**): el jefe, los jugadores participantes, daño, curación, daño recibido y habilidades. Solo se aceptan
  jefes que el juego anunció y el catálogo conoce. La subida automática está disponible y **desactivada por defecto**.
- **Tu propio perfil de personaje** (nombre, clase, nivel, equipo, habilidades, Daevanion, legión, servidor, facción)
  puede subirse automáticamente unos segundos después de iniciar sesión, para que te encuentren en el sitio web. Está
  **desactivado por defecto**; se activa en **Ajustes**.
- Sin subida, nada sale de tu equipo.

## Actualizaciones

El medidor se actualiza solo. Pregunta a GitHub por una versión más reciente al arrancar y cada cinco minutos mientras
está en marcha, la descarga en segundo plano y la aplica en el siguiente arranque — sin instalador ni aviso de UAC.
Cuando hay una actualización lista aparece una línea verde en la fila de estado (y una insignia en el overlay
transparente); al pulsarla se ofrece reiniciar enseguida. **App → Buscar actualizaciones** hace lo mismo a petición.
Una versión antigua sigue subiendo datos durante 72 horas después de que salga su sucesora; luego debe actualizarse.

La comprobación lee una sola URL y no envía nada salvo la propia petición:

```
https://api.github.com/repos/SkeeveAN/Aion-DPS-Meter/releases
```

Desactívala en **Ajustes → Actualizaciones**; la opción de menú sigue funcionando aunque esté desactivada.

## Idiomas

Inglés, alemán, francés, español, ruso, chino (simplificado y tradicional), portugués (Brasil), japonés y
coreano — se ajusta en **Ajustes → Interfaz → Idioma**. Este README también está disponible en
[Deutsch](README.de.md), [Español](README.es.md), [Français](README.fr.md), [Русский](README.ru.md),
[中文](README.zh.md), [繁體中文](README.zh-TW.md), [Português (Brasil)](README.pt.md),
[日本語](README.ja.md) y [한국어](README.ko.md).

## Compilar desde el código fuente

```
cd Client
dotnet build
dotnet run -- selftest                              # self-checks (protocol, capture, decoding, ...)
dotnet run -- aion2-record <out.jsonl>              # record the game's traffic ("stop" ends it)
dotnet run -- aion2-replay <file.jsonl>             # replay a recording through the real decoder
dotnet run -- aion2-upload-dryrun <file.jsonl>      # build the uploads of a recording, send nothing
```

Solo Windows (WPF). `Tools/aion2-dat` lee las tablas de texto del juego (nombres en ocho idiomas); ver su README.

## Nota sobre las reglas de los servidores

NCSOFT no ha aprobado esta herramienta. Su política de operación sanciona los «programas no autorizados»: programas o dispositivos que eluden protecciones técnicas o perjudican el funcionamiento normal, por ejemplo programas que permiten acciones que los jugadores normales no pueden realizar, así como crear o distribuir esos programas o modificar el software del cliente o del servidor. Los bots se mencionan aparte, y un aviso sobre el estado de confianza de la cuenta habla de «software sospechoso» sin definirlo. NCSOFT nombra comportamientos, no programas concretos; los medidores de daño y la lectura pasiva del tráfico de red ni figuran ni están prohibidos expresamente, aunque la lista no es exhaustiva. El medidor no juega por ti, no automatiza nada, no aumenta el daño ni la velocidad, no lee la memoria del juego, no modifica ni el cliente ni el servidor y no envía ningún paquete al juego; que quede fuera de la definición es nuestra interpretación, no una declaración de NCSOFT. Consulta [las normas vigentes](https://aion2.plaync.com/de-de/board/rules/list) y úsalo bajo tu propia responsabilidad.

## Aviso legal y licencia

Aion 2 y todos los nombres, textos y gráficos relacionados son propiedad de NCSOFT. Este proyecto no está afiliado, respaldado ni conectado con NCSOFT; el contenido del juego pertenece a su propietario. El código fuente se publica bajo la [Licencia MIT](LICENSE): la licencia cubre solo el código, no el contenido del juego.
