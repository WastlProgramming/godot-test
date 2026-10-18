# Spielprinzip & endlose Welt

## Ziel

Lauf so weit wie möglich. Die Strecke wird vor dir laufend neu erzeugt, sie endet nie. Deine **Punkte** setzen sich
aus der zurückgelegten Strecke in Metern und **25 Bonuspunkten pro zerschlagener Kiste** zusammen. Der Highscore wird
gespeichert (`user://highscore.json`).

Fällst du mehr als **20 m** unter die Höhe deines aktiven Checkpoints (`FallDeathDepth`), startest du am
**letzten Checkpoint** neu. Jeder Abschnitt beginnt mit einem 4 × 4 m großen Checkpoint-Pad mit Fahne; die Fahne
leuchtet grün, sobald der Checkpoint aktiv ist. Im Helikopterflug wird der Checkpoint jedes überflogenen Abschnitts
automatisch aktiviert.

## Chunks

Die Welt besteht aus **Chunks** (Abschnitten) mit nominal 40 m Länge. Jeder Chunk folgt einem
[Sprungmuster](./plattformen) und endet an einem Ausgangspunkt, an dem der nächste Chunk ansetzt.

![Lebenszyklus eines Chunks](/chunk-lifecycle.svg)

### Spawn (vorne)

Der `WorldGenerator` hält immer **6 Chunks vor dem Spieler** bereit (`GameConstants.ChunksAhead`). Pro Physik-Frame
wird höchstens **ein** Chunk gebaut, damit es keine Ruckler gibt. Chunk 0 (die große Startfläche) entsteht sofort beim
Start. Neue Chunks erscheinen etwa 240 m voraus im Tiefennebel und „blenden“ beim Näherkommen ein.

### Despawn (hinten)

Alle 10 Physik-Frames prüft der Generator die Chunks hinter dir. Ein Chunk wird entfernt, wenn **beide** Bedingungen
gelten:

1. Sein Ausgang liegt mehr als **30 m** hinter dir (`DespawnDistance`).
2. Seine Bounding-Box liegt **außerhalb des Kamera-Frustums** (`FrustumUtil.AabbIntersectsFrustum`).

Geschützt sind alle Chunks ab `min(Checkpoint-Chunk, Spieler-Chunk)` – der Weg vom Checkpoint bis zu dir bleibt also
immer erhalten. Als Speicherschutz gilt ein hartes Limit von **12 Chunks** (`MaxAliveChunks`); wird es überschritten,
fällt der älteste ungeschützte Chunk notfalls auch dann weg, wenn er noch sichtbar ist. Im Normalbetrieb
(6 vorne + 1–2 hinten) passiert das nicht.

### Floating Origin

Sobald du mehr als **1000 m** vom Ursprung entfernt bist, verschiebt der Generator alle Chunks, platzierten Objekte
und den Spieler um eine ganze Zahl von Metern zurück. Das hält die Float-Genauigkeit der Physik hoch. Während du auf
einer beweglichen oder drehenden Plattform stehst, wird nicht verschoben. Der `GameManager` merkt sich den
Gesamtversatz (`OriginOffsetZ`), damit Strecke und Punkte stetig bleiben.

## Seed und Determinismus

Jeder Chunk bekommt einen eigenen Zufallsgenerator aus `seed * 7919 + index`. Gleicher Seed ergibt deshalb dieselbe
Strecke, unabhängig davon, wann ein Chunk gebaut wird. Die Musterwahl ist immer der erste Verbraucher dieses
Zufallsgenerators; Formen, Kisten usw. werden erst danach gewürfelt.

## Schwierigkeit

Die Schwierigkeit `d` steigt linear von 0 auf 1 über die ersten **1500 m** (`DifficultyForIndex`). Mit ihr wachsen
Lückenbreiten, Plattformabstände, die Drehgeschwindigkeit drehender Scheiben und der Anteil anspruchsvoller Muster.

## Item-Chunks

Zwei Muster lassen sich **nicht** mit einem normalen Sprung lösen:

| Muster | Hindernis | Lösung |
|---|---|---|
| `BigGap` | Lücke von 10–13,5 m (Sprungweite ≤ 6 m), Anker darüber | Brücke, Enterhaken oder Helikopter |
| `TallWall` | Wand 5,5–7 m hoch | Trampolin oder Helikopter (bei höherer Schwierigkeit auch Enterhaken) |

Regeln dafür:

- Item-Chunks kommen frühestens ab Chunk 3 und nie zweimal hintereinander. Ihre Wahrscheinlichkeit steigt mit der
  Schwierigkeit von 15 % auf 35 %.
- Dasselbe Muster kommt höchstens zweimal in Folge.
- **Lösbarkeitsgarantie:** Der Generator sagt das nächste Muster voraus. Ist es ein Item-Chunk, enthält der aktuelle
  Chunk eine Kiste mit dem passenden Item (Brücke vor `BigGap`, Trampolin vor `TallWall`).
- Item-Chunks haben zusätzlich eine Versorgungsplattform mit mehreren **Versorgungskisten**.
- Stirbst du in einem Item-Chunk und hast keines der passenden Items mehr, werden die Versorgungskisten beim Respawn
  wieder aufgefüllt (`Chunk.RestockSupply`). Nachgelieferte Kisten geben keinen Punktebonus.
