# Items

Items findest du in **Kisten**. Zerschlage eine Kiste mit der linken Maustaste oder <kbd>F</kbd> (Reichweite 2,2 m),
und das Item fällt als Pickup heraus. Lauf darüber, um es einzusammeln.

## Unbegrenztes Inventar

Du kannst von **jedem Item beliebig viele** mitnehmen. Es gibt kein Limit pro Slot mehr; intern wird nur bei
999 999 gesättigt (`InventoryHardCap`, reiner Überlaufschutz). Ein Pickup wird deshalb immer eingesammelt.

Das Inventar hat vier Slots, einen pro Item-Typ (`enum ItemType`):

| Slot | Taste | Item |
|---|---|---|
| 1 | <kbd>1</kbd> | Brücke |
| 2 | <kbd>2</kbd> | Trampolin |
| 3 | <kbd>3</kbd> | Enterhaken |
| 4 | <kbd>4</kbd> | Helikopter |

Mit dem Mausrad wechselst du zyklisch den gewählten Slot, mit <kbd>E</kbd> oder der rechten Maustaste benutzt du ihn.
Die Zifferntasten wählen und benutzen in einem Schritt.

Brücken und Trampoline bleiben als Objekte in der Welt stehen. Es gibt davon höchstens **3 gleichzeitig**
(`MaxPlaceables`); beim vierten verschwindet das älteste.

## Kisten

- Jede Kiste hat 1 Trefferpunkt und gibt **25 Bonuspunkte**.
- Zufällige Kisten würfeln ihren Inhalt aus der Drop-Tabelle:

| Ergebnis | Wahrscheinlichkeit |
|---|---|
| Brücke | 27 % |
| Trampolin | 27 % |
| Enterhaken | 22 % |
| Helikopter | 12 % |
| nichts | 12 % |

- Kisten mit **festem Inhalt** sorgen dafür, dass jedes Hindernis lösbar ist: drei Tutorial-Kisten auf der Startfläche,
  eine Kiste im Chunk vor jedem Item-Chunk und **Versorgungskisten** im Item-Chunk selbst. Versorgungskisten werden
  nach einem Respawn wieder aufgefüllt, wenn dir das passende Item fehlt.

## Brücke

Legt eine 16 m lange, 2 m breite Planke in Blickrichtung. Schaust du höchstens 20° neben die Laufrichtung, rastet die
Brücke gerade nach vorn ein, damit die volle Länge zählt. Sie verschwindet nach 20 s.

- Nur **am Boden** einsetzbar („Nur am Boden möglich“).
- Löst `BigGap`: Lücken sind höchstens 13,5 m breit, also mindestens 2,5 m kürzer als die Brücke.

## Trampolin

Stellt ein Trampolin vor dir ab. Springst oder fällst du darauf, katapultiert es dich mit 22 m/s nach oben. Es
verschwindet nach 30 s.

- Nur am Boden und nicht auf beweglichen oder drehenden Plattformen einsetzbar.
- Braucht freien Platz („Kein Platz für das Trampolin“).
- Löst `TallWall`.

## Enterhaken

Zielt auf den nächsten **Anker** (goldene Kugel) im Blickkegel der Kamera (25°) in bis zu **28 m** Entfernung mit freier
Sichtlinie und zieht dich mit 22 m/s hin. Nah am Anker (1,5 m) löst er sich von selbst; mit der Leertaste oder erneutem
<kbd>3</kbd> lässt du vorher los. Ohne gültiges Ziel: „Kein Anker in Reichweite“.

- Funktioniert am Boden und in der Luft.
- Löst `BigGap` (Anker über der Lücke) und bei höherer Schwierigkeit auch `TallWall` (Anker über der Wand).

## Helikopter

Ein Rotor erscheint über dem Kopf deines Charakters, und du fliegst **3,5 s** lang:

| Eigenschaft | Wert |
|---|---|
| Flugzeit pro Ladung | 3,5 s (Tankanzeige im HUD) |
| Horizontal | 9 m/s, kamerarelativ mit WASD |
| Steigen | 6 m/s bei gehaltener Leertaste |
| Sinken | 1,2 m/s ohne Leertaste |
| Maximale Steighöhe | 14 m über dem Startpunkt des Flugs, höchstens 18 m über dem zuletzt betretenen Boden |

- Einsetzbar **am Boden und in der Luft**, auch während des Enterhakens (der Haken wird dann gelöst) – per
  <kbd>4</kbd> oder mit <kbd>E</kbd>, wenn der Helikopter gewählt ist.
- Mehrere Flüge hintereinander in der Luft steigen nicht immer höher: Die Decke bezieht sich auf den zuletzt
  betretenen Boden (+ 4 m Reserve für einen Sprung).
- Überfliegst du einen Abschnitt, wird sein Checkpoint trotzdem aktiv. Stürzt du nach dem Flug ab, startest du am
  Anfang des Abschnitts, in dem du zuletzt warst.
- Erneutes <kbd>4</kbd> bzw. <kbd>E</kbd> beendet den Flug vorzeitig, ohne eine weitere Ladung zu verbrauchen.
- Der Flug endet außerdem bei leerem Tank, beim Respawn, auf einem Trampolin und beim Start des Enterhakens. Danach
  setzt die normale Schwerkraft ein.
- Der Helikopter löst **beide** Item-Hindernisse.

## Welches Item löst was?

| Hindernis | Brücke | Trampolin | Enterhaken | Helikopter |
|---|:-:|:-:|:-:|:-:|
| `BigGap` (Riesenlücke) | ✔ | – | ✔ | ✔ |
| `TallWall` (hohe Wand) | – | ✔ | (✔ ab höherer Schwierigkeit) | ✔ |
