# Historian Data Sync — Handbuch

**Version 2.1 · August 2026**

---

## Inhaltsverzeichnis

1. [Wozu dieses Programm dient](#1-wozu-dieses-programm-dient)
2. [Begriffe in diesem Handbuch](#2-begriffe-in-diesem-handbuch)
3. [Voraussetzungen](#3-voraussetzungen)
4. [Installation und erster Start](#4-installation-und-erster-start)
5. [Das Fenster, Schritt für Schritt](#5-das-fenster-schritt-für-schritt)
   - 5.1 [Verbindung zu den beiden Servern](#51-verbindung-zu-den-beiden-servern)
   - 5.2 [Zeitraum wählen](#52-zeitraum-wählen)
   - 5.3 [Alle Messstellen](#53-alle-messstellen)
   - 5.4 [Eine einzelne Messstelle](#54-eine-einzelne-messstelle)
   - 5.5 [Fehlende Messwerte wiederherstellen](#55-fehlende-messwerte-wiederherstellen)
   - 5.6 [Reparaturverlauf und Rückgängig](#56-reparaturverlauf-und-rückgängig)
   - 5.7 [Automatische Reparatur](#57-automatische-reparatur)
   - 5.8 [Die erweiterte Ansicht](#58-die-erweiterte-ansicht)
6. [Zwei Dinge, die man verstanden haben sollte](#6-zwei-dinge-die-man-verstanden-haben-sollte)
7. [Wenn etwas nicht funktioniert](#7-wenn-etwas-nicht-funktioniert)
8. [Anhang](#8-anhang)

---

## 1. Wozu dieses Programm dient

Zwei Historian-Server zeichnen dieselbe Anlage auf. Sie sollen denselben Datenbestand halten,
tun es aber nicht immer: Ein Collector bleibt stehen, eine Netzwerkverbindung fällt aus, ein
Server wird für Wartungsarbeiten neu gestartet. Dann hat ein Server Messwerte, die der andere
nie erhalten hat.

Dieses Programm findet diese Unterschiede und kopiert die fehlenden Messwerte von dem Server,
der sie hat, auf den Server, dem sie fehlen.

**Was es nicht tut.** Es erfindet keine Daten und legt keine Messstellen an. Es kopiert
ausschließlich Messwerte, die auf einem der beiden Server tatsächlich vorhanden sind. Was kein
Server aufgezeichnet hat, lässt sich nicht zurückholen.

> **Das Programm schreibt in einen Produktiv-Historian.** Alles, was geschrieben wird, wird
> protokolliert, und jede Reparatur lässt sich rückgängig machen — siehe
> [5.6](#56-reparaturverlauf-und-rückgängig). Es wird nichts geschrieben, bevor Sie es
> bestätigen.

---

## 2. Begriffe in diesem Handbuch

| Begriff | Bedeutung |
|---|---|
| **Messstelle** | Ein Messwert der Anlage — eine Temperatur, ein Druck, ein Durchfluss. Im Historian heißen diese *Tags*. |
| **Messwert** | Ein aufgezeichneter Wert einer Messstelle mit dem Zeitpunkt der Aufzeichnung. Im Historian *Samples*. |
| **Hauptserver** | Der Historian, den Sie im ersten Feld eintragen. In der Regel der primäre Anlagenserver. |
| **Spiegelserver** | Der zweite Historian mit der redundanten Kopie. |
| **Vollständigkeit** | Wie viel von allem, was für diese Messstelle aufgezeichnet wurde, *dieser* Server hält — gemessen am jeweils anderen Server. Siehe [Kapitel 6](#6-zwei-dinge-die-man-verstanden-haben-sollte). |
| **Zeitraum** | Der betrachtete Zeitabschnitt, eingestellt über **Von** und **Bis**. Alle Angaben auf dem Bildschirm beziehen sich ausschließlich darauf. |
| **Wiederherstellen** | Messwerte, die einem Server fehlen, von dem Server kopieren, der sie hat. |
| **Reparaturverlauf** | Die Aufzeichnung aller durchgeführten Wiederherstellungen — und der Weg, eine davon rückgängig zu machen. |
| **Automatische Reparatur** | Eine unbeaufsichtigte Wiederherstellung, die nach Zeitplan ohne Aufsicht läuft. |

---

## 3. Voraussetzungen

**Auf Ihrem PC**

- Windows 10 oder Windows 11
- Microsoft .NET Framework 4.8 — auf allen Bürorechnern bereits vorhanden
- Es werden **keine** Administratorrechte und keine Installation benötigt

**Zugang zur Anlage**

- Netzwerkzugriff auf beide Historian-Server über den ClientAccess-Port (normalerweise **13000**)
- Ein Historian-Benutzerkonto, das auf beiden Servern lesen und auf dem zu reparierenden Server
  **schreiben** darf

**Nicht erforderlich** ist der installierte Proficy-Historian-Client. Die eine benötigte Datei
daraus wird zusammen mit dem Programm ausgeliefert.

---

## 4. Installation und erster Start

1. Entpacken Sie die gelieferte ZIP-Datei in einen Ordner, in dem Sie Schreibrechte haben — zum
   Beispiel `C:\Tools\HistorianSyncTool`. **Nicht** nach `C:\Programme`: Das Programm legt seine
   Reparaturaufzeichnung neben sich ab, und dieser Ordner ist für normale Benutzer schreibgeschützt.
2. Starten Sie `HistorianSyncTool.exe`.
3. Tragen Sie die beiden Serveradressen ein (siehe
   [5.1](#51-verbindung-zu-den-beiden-servern)) und klicken Sie auf **Verbinden**.

Nach der ersten erfolgreichen Verbindung werden die Adressen gespeichert; das Programm verbindet
sich beim nächsten Start selbstständig.

**Der Ordner enthält**

| Datei | Zweck |
|---|---|
| `HistorianSyncTool.exe` | Das Programm |
| `HistorianSyncTool.exe.config` | Einstellungen, inkl. optionaler Anmeldedaten (siehe [8](#8-anhang)) |
| `Proficy.Historian.ClientAccess.API.dll` | Die GE-Komponente für die Historian-Kommunikation |
| `logs\` | Wird bei der ersten Nutzung angelegt — siehe [8](#8-anhang) |

---

## 5. Das Fenster, Schritt für Schritt

Das Fenster besteht aus drei Spalten: links die Einstellungen, in der Mitte der Arbeitsbereich,
rechts **Was fehlt**.

![Die Übersicht aller Messstellen](img/01-overview-en.png)

*Die Übersicht aller Messstellen. Dieses Beispiel läuft im Demonstrationsmodus — der gelbe
Balken erscheint immer dann, wenn das Programm nicht mit einem echten Historian verbunden ist.*

### 5.1 Verbindung zu den beiden Servern

Tragen Sie die Server in die Felder **Hauptserver** und **Spiegelserver** ein. Alle folgenden
Schreibweisen funktionieren:

| Eingabe | Beispiel |
|---|---|
| Ein Rechnername | `TESTSV1` |
| Rechnername mit Port | `TESTSV1:13000` |
| Eine IP-Adresse | `192.168.50.186` |
| IP-Adresse mit Port | `192.168.50.186:13000` |

Klicken Sie auf **Verbinden**. Unter jedem Feld meldet das Programm **Verbunden** oder den Grund,
warum die Verbindung nicht zustande kam. Gespeichert werden nur Adressen, mit denen die
Verbindung tatsächlich funktioniert hat — eine vertippte Adresse wird Ihnen nie wieder
angeboten.

**Wenn der Server eine Anmeldung verlangt.** Die meisten Historian-Server lassen eine anonyme
Verbindung nicht zu und antworten mit *„Der Server hat die Client-Anmeldeinformationen
abgelehnt"*. Das ist kein Problem der Adresse — der Server wurde erreicht, er hat Sie nur nicht
hereingelassen. Klicken Sie auf **Anmeldung…**, tragen Sie Historian-Benutzername und Kennwort
ein und verbinden Sie erneut. Für beide Server gilt dieselbe Anmeldung.

Mit **Auf diesem PC merken** entfällt die erneute Eingabe. Das Kennwort wird dann ausschließlich
für Ihr Windows-Konto verschlüsselt gespeichert: Es ist für andere Benutzer und auf anderen
Rechnern nicht lesbar und ist nie Bestandteil des ausgelieferten Programmordners.

Bleiben beide Felder leer, wird die Verbindung mit Ihrem Windows-Konto aufgebaut — das ist die
richtige Wahl, wenn das Programm auf dem Historian-Rechner selbst läuft.

Wird die Anmeldung abgelehnt, bietet das Programm diesen Dialog automatisch an.

### 5.2 Zeitraum wählen

**Von** und **Bis** bestimmen den untersuchten Zeitraum. Alle weiteren Angaben auf dem
Bildschirm beziehen sich auf genau diesen Zeitraum und auf nichts außerhalb davon.

Die Schaltflächen darunter springen zu gängigen Zeiträumen: **1h, 6h, 24h, 3d, 7d, 30d, 90d, 1y**.

Eine Änderung der Datumsangaben startet **keine** Prüfung von selbst — Sie entscheiden, wann
geprüft wird, über **Auf fehlende Daten prüfen**. (Bei der Eingabe eines Datums löst jedes
einzelne Feld eine Änderung aus; sonst würde bei jedem Tastendruck eine neue Prüfung starten.)

> **Die Länge des Zeitraums bestimmt, was sichtbar werden kann.** Ein kurzer Zeitraum zeigt
> kleine Lücken genau, ein langer Zeitraum umfasst mehr, kann aber keine Lücke zeigen, die
> kürzer ist als ein Abschnitt seiner eigenen Zeitleiste. Die Zeile über der Liste gibt immer
> an, wie lang ein Abschnitt aktuell ist.

### 5.3 Alle Messstellen

Klicken Sie auf **Auf fehlende Daten prüfen**. Das Programm untersucht jede Messstelle, die auf
beiden Servern vorhanden ist, und listet sie auf — **die schlechteste zuerst**.

Jede Zeile zeigt den Namen der Messstelle, je einen Balken pro Server und rechts, wie viele
Messwerte sich zwischen den beiden Servern ungefähr unterscheiden.

| Anzeige | Bedeutung |
|---|---|
| Grüner Balken | Dieser Server hat die Messwerte |
| Roter Abschnitt | Hier fehlen Messwerte, die **der andere Server hat** — diese sind wiederherstellbar |
| Grauer Abschnitt | Keiner der Server hat hier etwas aufgezeichnet — es gibt nichts zu kopieren |
| „nicht auf diesem Server eingerichtet" | Die Messstelle existiert dort überhaupt nicht. Das Programm kopiert Messwerte, es legt keine Messstellen an. |
| „konnte nicht gelesen werden" | Der Server hat für diese Messstelle nicht geantwortet. Das ist **nicht** dasselbe wie „hält nichts" — siehe [7](#7-wenn-etwas-nicht-funktioniert) |

Die Zeile über der Liste fasst den Lauf zusammen: wie viele Messstellen geprüft wurden, wie
viele Aufmerksamkeit brauchen, und wie viel Zeit ein Abschnitt der Balken darstellt.

> Die Zahl auf diesem Bildschirm ist eine **schnelle Schätzung und immer eine Untergrenze** —
> sie ist mit `~` gekennzeichnet. Öffnen Sie eine Messstelle für den exakten Wert. Die Schätzung
> entscheidet niemals darüber, was geschrieben wird.

Über das Feld **Suchen** schränken Sie die Liste auf Messstellen ein, deren Name Ihre Eingabe
enthält.

### 5.4 Eine einzelne Messstelle

Ein Klick auf eine Zeile öffnet die Messstelle. Sie sehen nun für diese Messstelle und diesen
Zeitraum:

- eine **Zeitleiste** mit beiden Servern auf einer gemeinsamen Zeitachse, sodass die
  Unterschiede optisch übereinanderliegen
- ein **Diagramm** der Messwerte, Hauptserver oben, Spiegel darunter
- die beiden **Messwerttabellen**, je eine pro Server

Die Farben entsprechen der Liste: Grün heißt, der Server hat die Daten; Rot heißt, der andere
Server hat sie und dieser nicht; Grau heißt, keiner der beiden hat sie.

**Vergrößern** öffnet das Diagramm in einem größeren Fenster mit eigener Zeitachse.
**‹ Alle Messstellen** führt zurück zur Liste.

Die rechte Spalte zeigt jetzt die **exakte** Anzahl der Messwerte, die in jede Richtung kopiert
würden — berechnet aus den Messwerten selbst, nicht geschätzt.

### 5.5 Fehlende Messwerte wiederherstellen

1. Klicken Sie auf **Fehlende Daten wiederherstellen…**
2. Das Programm vergleicht beide Server und zeigt pro Messstelle genau an, wie viele Messwerte
   es kopieren würde und über welchen Zeitraum. **Es wurde noch nichts geschrieben.**
3. Entfernen Sie die Haken bei allem, was unangetastet bleiben soll.
4. Klicken Sie auf **Starten**.
5. Ein Fortschrittsfenster zeigt die aktuell bearbeitete Messstelle. **Abbrechen** hält an der
   nächsten sicheren Stelle an; Sie werden anschließend gefragt, ob das bereits Kopierte
   erhalten bleiben oder sofort rückgängig gemacht werden soll.
6. Ein Bericht listet auf, was je Messstelle geschrieben wurde, und lässt sich als CSV oder TXT
   exportieren.

**Was das Programm nicht tut**

- Es kopiert nie in eine Messstelle, die es auf dem Zielserver nicht gibt.
- Es kopiert nie die letzten Minuten: Direkt an der Gegenwart hat ein Collector unter Umständen
  nur noch nicht geschrieben — das sind keine fehlenden Daten.
- Es meldet eine Messstelle als fehlgeschlagen, wenn die Messwerte nicht tatsächlich angekommen
  sind. Es meldet keinen Erfolg, den es nicht überprüft hat.

### 5.6 Reparaturverlauf und Rückgängig

**Reparaturverlauf / rückgängig…** listet jede durchgeführte Wiederherstellung auf: wann, in
welche Richtung, wie viele Messwerte, und ob sie inzwischen rückgängig gemacht wurde.

So machen Sie eine rückgängig:

1. Wählen Sie den Lauf aus.
2. Setzen Sie den Haken bei **Rückgängig freigeben** — die rote Schaltfläche bleibt bis dahin
   gesperrt.
3. Klicken Sie darauf und bestätigen Sie.

Rückgängig löscht **genau** die Messwerte, die dieser Lauf geschrieben hat, anhand ihrer
einzelnen Zeitstempel. Bereits vorhandene Messwerte werden nie angetastet. Wird der Vorgang
unterbrochen, bleibt der Lauf in der Liste, sodass Sie ihn gefahrlos wiederholen können.

> Die Aufzeichnung für Rückgängig wird in den Ordner `logs` neben dem Programm geschrieben. Kann
> das Programm dort nicht schreiben, meldet es das — die Wiederherstellung lässt sich dann später
> nicht rückgängig machen. Deshalb muss der Programmordner beschreibbar sein.

### 5.7 Automatische Reparatur

Über **Automatische Reparatur** in der Statusleiste konfigurieren Sie eine unbeaufsichtigte
Wiederherstellung.

| Einstellung | Bedeutung |
|---|---|
| Intervall | Wie oft der Lauf stattfindet |
| Zeitraum | Wie weit jeder Lauf zurückschaut, gerechnet ab dem Zeitpunkt des Laufs |
| Richtung | Haupt → Spiegel, Spiegel → Haupt, oder beides |
| Messstellen | Alle passend zu einem Filter, oder eine ausdrücklich angehakte Liste |
| Beim Start ausführen | Zusätzlich einmal kurz nach dem Programmstart |

Automatische Reparaturen schreiben **ohne jede Rückfrage** in die Anlage. Deshalb gilt:

- Bevor erstmals ein Lauf beim Start stattfindet, müssen Sie das einmal ausdrücklich bestätigen
- Ein Lauf wird übersprungen, solange Sie im Fenster arbeiten
- Jeder Lauf wird in `logs\schedule-YYYY-MM.log` protokolliert
- Jeder Lauf lässt sich weiterhin über den Reparaturverlauf rückgängig machen

### 5.8 Die erweiterte Ansicht

Der Schalter **Erweitert** in der Titelleiste *ergänzt* technische Details; er entfernt nie
etwas. Sichtbar werden das Aktivitätsprotokoll, richtungsbezogene Kopierschaltflächen, der
Messstellenfilter, die Serverstatistik, Blockzähler und die je Messstelle verwendete Lückenregel.

Ausschalten führt zurück zur einfachen Ansicht. Es geht nichts verloren — dieselbe Arbeit ist in
beiden Ansichten möglich.

---

## 6. Zwei Dinge, die man verstanden haben sollte

Diese beiden Punkte kommen in jedem Gespräch über die angezeigten Zahlen auf.

### 6.1 Was „vollständig" hier bedeutet

Eine Prozentangabe ist nur gegen einen Maßstab sinnvoll. Dieses Programm kann immer nur einen
Server an den anderen angleichen — der Maßstab ist deshalb **der jeweils andere Server**:

> Vollständigkeit = Wie viel von allem, was in diesem Zeitraum für diese Messstelle von
> *irgendeinem* der beiden Server aufgezeichnet wurde, hält *dieser* Server?

98,8 % heißt also: Dem Server fehlen rund 1,2 % von allem Aufgezeichneten — und da der andere
Server es hat, lässt sich dieser fehlende Teil wiederherstellen. Hat **keiner** der beiden
Server etwas aufgezeichnet, wird das keinem angelastet: Das ist ein Anlagenausfall, kein
Synchronisationsproblem, und wird grau dargestellt.

Aus demselben Grund werden die Balken *anteilig* eingefärbt: Bild und Prozentwert sind dieselbe
Größe, ein Balken kann also nicht das eine zeigen, während die Zahl etwas anderes sagt.

### 6.2 Warum es auch nach einer Reparatur nicht immer 100 % sind

Drei ehrliche Gründe:

1. **Die Lücke besteht auf beiden Servern.** Es gibt nichts zu kopieren. Wird grau dargestellt.
2. **Die Messstelle existiert nur auf einem Server.** Das Programm kopiert Messwerte; das
   Anlegen von Messstellen ist eine Konfigurationsaufgabe im Historian.
3. **Die beiden Server zeichnen unabhängig voneinander auf.** Redundante Collector tasten
   dasselbe Signal nach ihrer eigenen Uhr ab, derselbe Wert wird also häufig einige Sekunden
   versetzt gespeichert. Das sind keine fehlenden Messwerte, und ein Kopieren würde zwei
   Aufzeichnungen dauerhaft vermischen, statt etwas zu reparieren. Das Programm erkennt das und
   füllt nur echte Ausfälle.

Punkt 3 ist der Grund, warum zwei Server leicht unterschiedliche Zahlen zeigen können und
trotzdem beide völlig in Ordnung sind.

---

## 7. Wenn etwas nicht funktioniert

| Anzeige | Bedeutung | Vorgehen |
|---|---|---|
| **Verbindung nicht möglich** | Falsche Adresse oder falscher Port, oder der Server ist nicht erreichbar | Adresse prüfen, `rechner:13000` versuchen. Vom PC aus prüfen, ob der Port erreichbar ist |
| **Der Benutzername darf nicht leer sein** | Der Server lässt anonymen Zugriff nicht zu | Anmeldedaten in `HistorianSyncTool.exe.config` eintragen — siehe [8](#8-anhang) |
| **Messwerte dieses Servers konnten nicht geladen werden** | Das Lesen ist fehlgeschlagen. Das heißt *nicht* „der Server hält nichts" | Erneut versuchen. Das Programm lässt diesen Server bewusst aus dem Ergebnis heraus, statt ihn als leer zu melden |
| **nicht auf diesem Server eingerichtet** | Die Messstelle existiert dort nicht | Die Messstelle zuerst im Historian anlegen, falls sie dorthin gehört |
| Eine Messstelle zeigt 0 % | Dieser Server hat im Zeitraum nichts aufgezeichnet | Das ist ein echter, wiederherstellbarer Unterschied und steht ganz oben |
| **… konnte NICHT im Reparaturverlauf gespeichert werden** | Der Programmordner ist nicht beschreibbar | Ordner an einen beschreibbaren Ort verschieben. Die Messwerte **wurden** geschrieben, dieser Lauf lässt sich aber nicht rückgängig machen |
| Nichts wiederherzustellen, die Balken sind aber nicht voll | Der fehlende Teil ist grau (auf beiden Servern nicht vorhanden), oder die Server zeichnen unabhängig auf | Siehe [Kapitel 6](#6-zwei-dinge-die-man-verstanden-haben-sollte) |

---

## 8. Anhang

### Vom Programm angelegte Dateien

| Pfad | Inhalt |
|---|---|
| `logs\schedule-YYYY-MM.log` | Eine Zeile je automatischem Lauf |
| `logs\backfill-journal\*.json` | Die Aufzeichnung, welche Messwerte jede Wiederherstellung geschrieben hat — nur dadurch ist Rückgängig möglich. **Nicht löschen.** |

Persönliche Einstellungen — Serveradressen, Sprache, Zeitraum, Zeitplan — werden je
Windows-Benutzer gespeichert, nicht im Programmordner, und bei einem Update auf eine neuere
Version automatisch übernommen.

### Optionale Anmeldedaten

Weisen die Server einen leeren Benutzernamen zurück, öffnen Sie
`HistorianSyncTool.exe.config` in einem Texteditor und tragen ein:

```xml
<add key="HistorianUsername" value="benutzer" />
<add key="HistorianPassword" value="kennwort" />
```

Bleiben beide leer, wird die Windows-Anmeldung verwendet — das funktioniert, wenn das Programm
auf dem Historian-Rechner selbst läuft.

### Weitere Einstellungen in derselben Datei

| Schlüssel | Standard | Bedeutung |
|---|---|---|
| `LiveEdgeGraceSeconds` | 120 | Wie viel der jüngsten Vergangenheit unangetastet bleibt, weil Collector dort noch schreiben könnten |
| `BatchSizeMinutes` | 10 | Wie viele Daten je Schritt geschrieben werden |
| `MinimumGapSeconds` | 120 | Kürzere Stille gilt nie als Lücke |
| `GapThresholdMultiplier` | 2,0 | Um wie viel länger als ihr normaler Rhythmus eine Messstelle stumm sein muss, damit es als Lücke zählt |

### Demonstrationsmodus

Wird das Programm mit `--demo` gestartet, läuft es gegen ein erzeugtes Serverpaar. Es nimmt zu
keinem Historian Kontakt auf und kann nichts verändern. Ein gelber Balken macht das
unmissverständlich deutlich. Nutzen Sie ihn, um das Programm auszuprobieren oder vorzuführen,
ohne Anlagenverbindung.

### Version

Historian Data Sync **2.1** · Handbuchstand August 2026.
