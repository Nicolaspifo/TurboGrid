# Turbo Grid — Arquitectura técnica

Documento técnico breve del proyecto (entregable del rol Programador).
Reglas del juego: ver `turbo-grid-preparacion-desarrollo.md`.

## 1. Primer paso después de actualizar el proyecto

Después de hacer *Pull* de estos cambios, en Unity:

1. Menú **TurboGrid → Configurar escenas y prefabs**.
2. Si pregunta por guardar escenas abiertas, guarda.
3. Al terminar aparece un resumen de todo lo que configuró.

El script (`Assets/Scripts/Editor/TurboGridSetup.cs`) monta en escenas y prefabs lo que el código necesita:
HUD, controles táctiles, mini-mapa, pantalla de resultados, panel de opciones del lobby, parrilla de salida,
prefabs de red nuevos, circuitos 2 y 3 y Build Settings.
Se puede ejecutar varias veces sin romper nada: los objetos que genera empiezan con `TG_` y se regeneran;
los objetos originales del equipo **solo se desactivan**, no se borran.

> Si modificas a mano un objeto `TG_...` y vuelves a ejecutar el script, ese cambio se pierde.
> Para personalizarlo de forma permanente, renómbralo (quítale el prefijo `TG_`).

## 2. Cómo probar rápido (sin compilar para celular)

1. Abre la escena **MainMenu** para probar la red (la app instalada arranca en *Pantalla Inicio*, pero la red vive en MainMenu).
2. **Window → Multiplayer → Multiplayer Play Mode**: activa *Player 2* (y 3 y 4 si quieres).
3. Play. En el editor: **Crear partida** → copia el código. En la ventana de Player 2: pega el código → **Unirse**.
4. En el editor: **Jugar**.

Teclado para probar en el editor: **flechas / WASD** para manejar, **E o Espacio** para DRS, **Q** para Pinchazo.

## 3. Red

| Pieza | Qué hace |
|---|---|
| **Netcode for GameObjects 2.x + Unity Relay** | Un celular es *Host* (servidor + jugador); los demás se unen con un código de 6 letras. Va por internet, así que iOS y Android juegan juntos. |
| `RelayManager` | Inicia sesión anónima en Unity Services, crea o se une a la sala de Relay. En el editor usa un perfil distinto por proceso para que las ventanas de Multiplayer Play Mode no compartan el mismo usuario. |
| `GameNetworkManager` | Aprobación de conexiones (máx. 4; no se entra con la carrera empezada), envío del nombre del piloto en el payload de conexión, arranque de la carrera y regreso al menú cuando se cae la conexión. |
| `SessionState` (prefab) | Lista de pilotos de la sala (nombre + color/slot 0-3) y mapa elegido. Lo crea el host y sobrevive a los cambios de escena. |

**Autoridad:** el servidor decide todo lo importante (puntos, gasolina, power-ups, fin de carrera).
El único dato que manda el cliente es la posición de **su propio auto** (`NetworkTransform` en modo *Owner*), para que el manejo se sienta inmediato en el celular.

## 4. Flujo de una partida

```
Pantalla Inicio (Jugar / Salir) -> Pantalla Instrucciones (Siguiente / Atras)
        │
MainMenu
  Nombre del piloto (obligatorio) → Crear partida / Unirse con código
        │
Sala de carrera (panel dentro de MainMenu)
  Código en grande + 4 puestos: auto, nombre y estado
  (Anfitrión / Conectando... / En la sala / ¡Listo! / Esperando...)
  Invitados: ¡ESTOY LISTO!   ·   Opciones: controles (cada uno), circuito (host)
  Host: COMENZAR (2+ pilotos y todos listos) → NetworkSceneManager carga el circuito en todos
        │
Circuito (Game / Circuito2 / Circuito3)
  RaceManager espera a que todos carguen
  → crea un auto por jugador en una casilla ALEATORIA de la parrilla (4 fijas)
  → FuelSpawner reparte 50 bidones; PowerUpSpawner pone 1 DRS + 1 Pinchazo
  → cuenta regresiva 3 s → carrera 3:00
  → fin: alguien llega a 1000 pts (victoria instantánea) o se acaba el tiempo (gana el de más puntos)
  → Resultados: REVANCHA (host, mismo circuito) o SALIR (vuelve al menú)
```

## 5. Mecánicas (dónde está cada una)

| Mecánica | Script | Detalle |
|---|---|---|
| Parrilla aleatoria | `SpawnGridManager`, `RaceManager.SpawnCarsServer` | 4 casillas fijas (`StartGrid/Posicion1-4`), barajadas cada carrera. La rotación de la casilla define hacia dónde mira el auto. |
| Gasolina | `FuelSpawner`, `FuelToken` | 50 bidones en puntos de `TrackWaypoints`. El servidor detecta la recogida por distancia, suma **20 pts** y el bidón **reaparece a los 10 s** en el mismo lugar. |
| Power-ups en pista | `PowerUpSpawner`, `PowerUpPickup` | Siempre hay 1 DRS y 1 Pinchazo en pista. Al recogerlo queda guardado (botón encendido); tras recogerlo reaparece en otro punto a los 8 s. |
| DRS | `PlayerNetworkData.UsePowerUpRpc`, `CarController` | ×1.6 de velocidad máxima durante **3 s**; estela azul. |
| Pinchazo | `PlayerNetworkData.PickRandomRivalServer` | Bloquea **2 s** a un rival **al azar** (nunca a quien lo lanza); el auto parpadea en morado. |
| Puntaje y victoria | `PlayerNetworkData.AddFuelServer`, `RaceManager` | 1000 pts = victoria instantánea; si no, al acabar 3:00 gana el de más puntos (desempate: más gasolina). |
| Controles por jugador | `LocalPlayerSettings`, `ControlsUI`, `CarInput` | Flechas (girar + gas/freno), Timón (arrastrar + gas/freno) o Joystick. Se guarda en el celular (PlayerPrefs). |
| Cámara | `CameraFollow` | Sigue al auto local con un pequeño adelanto y zoom según la velocidad. |
| HUD | `HUD_RaceUI`, `MinimapUI`, `AbilityButtonUI` | Puntos + barra hacia 1000, posición, tiempo, tabla de posiciones en vivo, mini-mapa, avisos, botones de power-up. |

Todos los números de balance están en **`Assets/Scripts/Utils/GameConstants.cs`**.

## 6. Mapas

`Assets/Scripts/Utils/MapCatalog.cs` lista los circuitos: `Game` (Circuito 1), `Circuito2`, `Circuito3`.
Solo se ofrecen los que estén en Build Settings.

**Circuitos 2 y 3 son provisionales**: copias del Circuito 1 con otro color. Para hacer un circuito nuevo, dentro de su escena:

1. Cambiar el sprite de `Pista` y sus colliders (`Square` = muros exteriores; `Pista` tiene los colliders de la isla central).
2. Mover los hijos de `TrackWaypoints` (60-80 puntos **dentro de la pista**: ahí salen gasolina y power-ups).
3. Mover `StartGrid/Posicion1-4` y rotarlas hacia donde deben mirar los autos (el auto mira hacia su eje Y).
4. No borrar `RaceManager`, `FuelSpawner`, `EventSystem` ni `Canvas`.

## 7. Estructura de scripts

```
Assets/Scripts/
  Network/   RelayManager, GameNetworkManager, SessionState, PlayerNetworkData
  Gameplay/  RaceManager, SpawnGridManager, FuelSpawner, FuelToken, PowerUpSpawner, PowerUpPickup,
             CarController, CarInput, CarVisuals, CameraFollow, Abilities/*
  UI/        LobbyUI, HUD_RaceUI, MinimapUI, EndRaceUI, ControlsUI, HoldButton, SteeringWheel,
             VirtualJoystick, AbilityButtonUI
  Utils/     GameConstants, LocalPlayerSettings, MapCatalog, TurboGridNote
  Editor/    TurboGridSetup (menú TurboGrid)
```

## 8. Builds

- Guardar los builds **fuera** de la carpeta del repositorio o en una carpeta que empiece por `Build` (está en `.gitignore`).
- Al guardar, no escribir `/` en el nombre de la carpeta: macOS lo convierte en `:` y eso rompe IL2CPP en iOS.
- iOS: Bundle ID y Team se configuran en *Player Settings* de Unity, no en Xcode (Xcode se sobrescribe en cada build).
- iOS y Android deben compilarse desde **el mismo commit** para poder jugar juntos.

## 9. Audio

Todo el audio es **generado por código** (sin archivos de internet, sin derechos de autor).

| Pieza | Qué hace |
|---|---|
| `Tools/generar_audio.py` | Sintetiza los efectos y la música en `Assets/Resources/Audio/SFX` y `/Musica`. Se vuelve a generar con `python3 Tools/generar_audio.py` (requiere numpy). |
| `AudioManager` | Se crea solo al arrancar. Música con fundido cruzado (menú / carrera / jingle de victoria), efectos con paneo según la posición en pantalla, clic en todos los botones, cuenta regresiva, avisos de power-ups, sala (alguien entra / alguien está listo) y opción *Sonido* en Opciones. |
| `CarAudio` | Motor y derrape **procedurales en tiempo real** por cada auto: el tono sube con la velocidad, simula cambios de marcha, el DRS agrega silbido de turbo y los derrapes suenan al girar fuerte o frenar. |

**Reemplazar un sonido:** poner un archivo con el mismo nombre (`.wav`, `.ogg` o `.mp3`) en la misma carpeta y borrar el generado. Nombres: `gasolina`, `recoger_drs`, `activar_drs`, `recoger_pinchazo`, `lanzar_pinchazo`, `pinchado`, `choque`, `frenazo`, `cuenta_beep`, `cuenta_go`, `boton`, `jugador_listo`, `jugador_entra`, `aplausos`, `musica_menu`, `musica_carrera`, `musica_victoria`.

La música de fondo es un loop perfecto: se genera con un número exacto de compases y la cola de las últimas notas se suma al inicio, así el corte no se nota.
