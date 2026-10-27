// The technical article at /how-it-works. One source for the page in the app (app.js renderArticle) and for the text the
// server sends to crawlers (Backend/src/seo/pages.ts articlePage). English for crawlers, German in the app when the site language is German.
// Inline markup in the texts: **bold** and [label](url).

export const ARTICLE = {
  path: "/how-it-works",
  title: "Passive combat tracking with Npcap and .NET 10",
  description: "How a damage meter can read a game without touching it: packet capture with Npcap and SharpPcap, TCP reassembly, recorded sessions and the limits of the approach.",
  date: "2026-10-19",
  blocks: [
    ["lead", "Most damage meters get their numbers by reading the game's memory or by injecting a hook into the game process. That works, but it is fragile (every patch moves addresses). [Aion DPS Meter](/features) takes the other route for AION 2: it never touches the game process. It listens to the game's own network traffic on your machine, the way a packet analyser would, and rebuilds the combat log from that. This article describes the architecture, the pitfalls and what we would do again."],
    ["h2", "The principle"],
    ["p", "The server already tells your client everything: who hit whom, for how much, who healed, who died. If you can read those bytes, you do not need the game process at all. The meter only ever receives a copy of packets that already arrived; it never sends one."],
    ["ul", [
      "**Nothing to inject, nothing to patch around.** A game update changes the data, not the way we read it.",
      "**No intervention.** No memory reading, no hooks, no packet injection.",
      "**It sees what the server sends you.** That includes the combat events of everyone in your party or raid, not only your own character.",
    ]],
    ["h2", "What NCSOFT's rules say"],
    ["p", "We read NCSOFT's published rules before writing this article. Its operating policy sanctions the use of \"unauthorized programs\", meaning programs or devices that bypass technical protection or impair normal operation, for example programs that enable actions regular players cannot perform, and the creation or distribution of such programs or changes to client or server software. Bots are listed separately, and a notice on the account trust status mentions \"suspicious software\" without defining it. The terms of service also forbid decompiling or reverse engineering the game software; we analyse the data stream only, never the game program. [The rules are public](https://aion2.plaync.com/de-de/board/rules/list)."],
    ["ul", [
      "**What the meter does not do:** it plays nothing for you, automates nothing, raises no damage or speed, reads no game memory, changes neither client nor server and sends no packet to the game.",
      "**How the rules are built:** NCSOFT names behaviors, not individual programs, because a tool could hide mechanics that are not allowed. Damage meters, packet capture and the passive reading of network traffic are neither listed nor prohibited by name.",
      "**What we cannot promise:** the list is not exhaustive, NCSOFT has not expressly approved Aion DPS, and \"suspicious software\" is not defined. That the meter falls outside the listed behaviors is our reading, not a statement from NCSOFT. Check the current rules and use it at your own responsibility.",
    ]],
    ["h2", "The capture layer: SharpPcap on Npcap"],
    ["p", "On Windows, [Npcap](https://npcap.com) is the packet capture driver, and [SharpPcap](https://github.com/dotpcap/sharppcap) is its .NET wrapper, with PacketDotNet to parse the headers. The meter targets .NET 10, and the capture loop is small:"],
    ["ol", [
      "Listen on the network adapters. By default that is every adapter, because a player may have Wi-Fi, Ethernet and a VPN at once.",
      "Set a **BPF filter in the driver**, so only the game server's TCP traffic reaches user mode. Everything else is dropped in the kernel and costs us nothing.",
      "In the callback, parse the headers, build a small segment record (stream key, TCP sequence number, payload) and hand it on.",
    ]],
    ["p", "Two lessons from production:"],
    ["p", "**Keep the callback tiny.** Our first version decoded frames inside the capture callback. During a world boss fight the burst of traffic overflowed the driver's buffer and packets were lost. Now the capture threads only parse and enqueue into a bounded queue, a single worker does the decoding, and the kernel buffer is raised to 32 MB. A packet lost at capture time is the one failure you cannot repair later."],
    ["p", "**Never let one bad packet end the capture thread.** An unhandled exception inside a capture callback can silently stop capturing. We count and skip the failures and show the last error in the interface instead."],
    ["h2", "From segments to frames: TCP reassembly"],
    ["p", "A game frame does not line up with a TCP segment. One segment can carry several frames, and one frame can span several segments. So the next layer is a small TCP reassembler:"],
    ["ul", [
      "Segments are ordered per direction by sequence number; an out-of-order segment waits for the missing piece.",
      "Retransmissions are dropped.",
      "A hole that never fills is not an error on the wire but a packet we missed while capturing. The reassembler gives up after a bound, realigns on the next segment and counts the event. Frames are length-prefixed, so a boundary is found again and at most the frames that spanned the hole are lost.",
    ]],
    ["p", "That last point matters: **design for loss on the capture side.** A meter that stops working after the first missed packet is a meter nobody trusts."],
    ["h2", "Decoding: understanding the protocol"],
    ["p", "Once you have frames, the work is working out what they mean: which message is which, how the fields are laid out, which frames are compressed, which numbers are damage and which are healing. That part is patient observation rather than clever engineering: record a session, fight one known enemy, and compare what you did with what the stream says. Two habits made it tractable:"],
    ["ul", [
      "**Record raw sessions and replay them offline.** Every protocol change can be checked against old recordings, and a bug becomes a reproducible test instead of \"it happened once in a raid\".",
      "**Cross-check against numbers the game reports itself.** One example is the boss's own hit points: the meter compares the damage it counted with the hit points the boss really lost. A mismatch points to missed packets or a changed layout. In debug recordings a health monitor also reports when a kind of message that normally flows goes silent after a game update.",
    ]],
    ["p", "We deliberately do not publish message tables or field layouts here. They change with every patch, and the interesting part is the method."],
    ["h2", "From frames to a combat log"],
    ["p", "A frame is not a fight yet. From the damage and healing events the server sends, the meter rebuilds the log you see in the window in three steps:"],
    ["ol", [
      "**Assign the events.** Each event says who hit or healed whom with which skill, and for how much. The meter attributes it to the player and counts it as damage, healing or damage taken.",
      "**Add up per player.** That gives DPS, iDPS and HPS and the breakdown by skill. The kind of hit, such as a critical hit, is already in the event, so crit rates are counted, not estimated.",
      "**Check against the boss.** The boss reports its own hit points. The meter compares the damage it counted with the hit points actually lost and warns when they differ by more than one percent.",
    ]],
    ["p", "Because every session can be recorded and replayed, each of these rules can be checked against old fights."],
    ["h2", "Upload and leaderboards"],
    ["p", "Nothing leaves your computer unless you upload it. Uploading is voluntary and off by default."],
    ["ul", [
      "**What is sent:** for a boss fight, the boss, the players who took part, damage, healing, damage taken and skills. Your character profile is only sent if you switch that on separately.",
      "**What the server accepts:** only bosses the game announced and the catalog knows. If several players report the same fight, it counts once. Fights that look implausible compared with other kills of the same boss (hit points, kill time, damage per class) are flagged and checked.",
      "**What this data is:** it describes game characters, not the person behind them. Class, gear, inventory and damage say nothing about the player. Only a character name might, in some cases, allow a conclusion about a person. The [privacy policy](/privacy) lists exactly what is stored and why.",
    ]],
    ["h2", "What this design cannot do"],
    ["ul", [
      "It sees only what the server sends. If the server does not tell the client something, no meter can show it.",
      "Encrypted traffic would end the approach. The combat stream of this game is readable, which is the premise of the whole design.",
      "It needs a capture driver. Npcap is a separate install, which is a real hurdle for some players.",
    ]],
    ["h2", "Why we would choose it again"],
    ["ul", [
      "**Maintenance:** patches break layouts, not hooks, and recorded sessions turn every fix into a regression test.",
      "**Trust:** players can read the source, see that nothing is injected, and judge for themselves what it does and does not do.",
      "**Performance:** the game never notices the meter.",
    ]],
    ["h2", "Questions and answers"],
    ["p", "**Is it allowed?** NCSOFT lists the behaviors it sanctions, such as bypassing protection, bots or tools that raise damage or speed, and does not name individual programs. Aion DPS does none of that, and damage meters are neither listed nor prohibited by name. That is our reading, not a statement from NCSOFT: check the current rules and use it at your own responsibility."],
    ["p", "**Why Npcap?** The meter needs a copy of the network traffic your own computer receives, and on Windows Npcap is the driver that provides it. It is a separate install, and the meter sends nothing through it."],
    ["p", "**What does the meter not see?** Only what the server sends your client. If the server does not tell you something, no meter can show it."],
    ["p", "**Does it slow the game down?** The meter is a separate program and never runs inside the game process. The driver drops everything except the game's traffic before the meter sees it. In a five-minute recording of normal play the game's payload came to about 2.4 KB per second on average and 30 KB per second at the peak."],
    ["p", "The source code is MIT licensed: [github.com/SkeeveAN/Aion-DPS-Meter](https://github.com/SkeeveAN/Aion-DPS-Meter). The meter, the leaderboards and the feature tour are on [aiondps.com](/features), and [how it compares with other AION 2 meters](/aion-2-dps-meter) is written down as well. Every push runs the type check and all backend tests in public and builds the client: [the test results](https://github.com/SkeeveAN/Aion-DPS-Meter/actions/workflows/ci.yml)."],
  ],
};

/** The German version of the article, shown when the site language is German (the server text for crawlers stays English). */
export const ARTICLE_DE = {
  path: "/how-it-works",
  title: "Passives Kampf-Tracking mit Npcap und .NET 10",
  description: "Wie ein Schadensmeter ein Spiel lesen kann, ohne es anzufassen: Paketmitschnitt mit Npcap und SharpPcap, TCP-Zusammensetzung, aufgezeichnete Sitzungen und die Grenzen des Ansatzes.",
  date: "2026-10-19",
  blocks: [
    ["lead", "Die meisten Schadensmeter holen ihre Zahlen aus dem Arbeitsspeicher des Spiels oder über einen Hook, der in den Spielprozess eingeschleust wird. Das funktioniert, ist aber fragil (jeder Patch verschiebt Adressen). Der [Aion DPS Meter](/features) geht für AION 2 den anderen Weg: Er fasst den Spielprozess nie an. Er hört auf deinem Rechner den eigenen Netzwerkverkehr des Spiels mit, so wie ein Paketanalysator, und baut daraus das Kampfprotokoll nach. Dieser Artikel beschreibt die Architektur, die Stolpersteine und was wir wieder so machen würden."],
    ["h2", "Das Prinzip"],
    ["p", "Der Server sagt deinem Client ohnehin alles: wer wen für wie viel getroffen hat, wer geheilt hat, wer gestorben ist. Wenn man diese Bytes lesen kann, braucht man den Spielprozess gar nicht. Der Meter bekommt nur eine Kopie von Paketen, die schon angekommen sind; er sendet nie eines."],
    ["ul", [
      "**Nichts einzuschleusen, nichts zu umgehen.** Ein Spielupdate ändert die Daten, nicht die Art, wie wir sie lesen.",
      "**Kein Eingriff.** Kein Speicherlesen, keine Hooks, keine Paketeinspeisung.",
      "**Er sieht, was der Server dir schickt.** Dazu gehören die Kampfereignisse aller aus deiner Gruppe oder deinem Schlachtzug, nicht nur die deines eigenen Charakters.",
    ]],
    ["h2", "Was die Regeln von NCSOFT sagen"],
    ["p", "Wir haben die veröffentlichten Regeln von NCSOFT gelesen, bevor wir diesen Artikel geschrieben haben. Die Betriebsrichtlinien sanktionieren die Nutzung „nicht autorisierter Programme“, also Programme oder Vorrichtungen, die technische Schutzmaßnahmen umgehen oder den ordnungsgemäßen Betrieb beeinträchtigen, etwa Programme, die Handlungen ermöglichen, zu denen reguläre Spieler nicht in der Lage sind, sowie das Erstellen oder Verbreiten solcher Programme und das Verändern von Client- oder Serverprogrammen. Bots werden gesondert genannt, und ein Hinweis zum Vertrauensstatus eines Kontos erwähnt „verdächtige Software“, ohne sie zu definieren. Die Nutzungsbedingungen verbieten außerdem, die Spielsoftware zu dekompilieren oder zurückzuentwickeln; wir analysieren nur den Datenstrom, nie das Spielprogramm. [Die Regeln sind öffentlich](https://aion2.plaync.com/de-de/board/rules/list)."],
    ["ul", [
      "**Was der Meter nicht tut:** Er spielt nichts für dich, automatisiert nichts, erhöht weder Schaden noch Geschwindigkeit, liest keinen Spielspeicher, verändert weder Client noch Server und sendet kein Paket an das Spiel.",
      "**Wie die Regeln aufgebaut sind:** NCSOFT nennt Verhaltensweisen, keine einzelnen Programme, weil ein Werkzeug unerlaubte Mechaniken verbergen könnte. Schadensmeter, Paketmitschnitt und das passive Mitlesen von Netzwerkverkehr sind weder aufgelistet noch ausdrücklich verboten.",
      "**Was wir nicht versprechen können:** Die Liste ist nicht abschließend, NCSOFT hat Aion DPS nicht ausdrücklich freigegeben, und „verdächtige Software“ ist nicht definiert. Dass der Meter außerhalb der genannten Verhaltensweisen liegt, ist unsere Lesart, keine Aussage von NCSOFT. Prüfe die aktuellen Regeln und nutze ihn auf eigene Verantwortung.",
    ]],
    ["h2", "Die Mitschnitt-Schicht: SharpPcap auf Npcap"],
    ["p", "Unter Windows ist [Npcap](https://npcap.com) der Treiber für den Paketmitschnitt und [SharpPcap](https://github.com/dotpcap/sharppcap) sein .NET-Wrapper, dazu PacketDotNet, um die Kopfdaten zu lesen. Der Meter zielt auf .NET 10, und die Mitschnitt-Schleife ist klein:"],
    ["ol", [
      "Auf den Netzwerkadaptern lauschen. Standardmäßig sind das alle, weil ein Spieler WLAN, Ethernet und ein VPN gleichzeitig haben kann.",
      "Einen **BPF-Filter im Treiber** setzen, sodass nur der TCP-Verkehr des Spielservers den Benutzermodus erreicht. Alles andere wird im Kernel verworfen und kostet uns nichts.",
      "Im Callback die Kopfdaten lesen, einen kleinen Segment-Datensatz bauen (Stream-Schlüssel, TCP-Sequenznummer, Nutzdaten) und weiterreichen.",
    ]],
    ["p", "Zwei Lehren aus dem Echtbetrieb:"],
    ["p", "**Den Callback winzig halten.** Unsere erste Version hat Frames direkt im Mitschnitt-Callback entschlüsselt. Bei einem Weltboss-Kampf lief der Treiberpuffer unter dem Datenschwall über, und Pakete gingen verloren. Jetzt lesen die Mitschnitt-Threads nur noch die Kopfdaten und stellen die Segmente in eine begrenzte Warteschlange, ein einzelner Worker dekodiert, und der Kernelpuffer wurde auf 32 MB erhöht. Ein Paket, das beim Mitschnitt verloren geht, ist der eine Fehler, den man später nicht mehr reparieren kann."],
    ["p", "**Nie zulassen, dass ein schlechtes Paket den Mitschnitt-Thread beendet.** Eine unbehandelte Ausnahme im Callback kann den Mitschnitt still anhalten. Wir zählen und überspringen die Fehler und zeigen stattdessen den letzten in der Oberfläche an."],
    ["h2", "Von Segmenten zu Frames: TCP-Zusammensetzung"],
    ["p", "Ein Spiel-Frame deckt sich nicht mit einem TCP-Segment. Ein Segment kann mehrere Frames tragen, und ein Frame kann über mehrere Segmente laufen. Die nächste Schicht ist deshalb ein kleiner TCP-Zusammensetzer:"],
    ["ul", [
      "Segmente werden pro Richtung nach Sequenznummer geordnet; ein Segment außer der Reihe wartet auf das fehlende Stück.",
      "Wiederholte Übertragungen werden verworfen.",
      "Eine Lücke, die sich nie füllt, ist kein Fehler auf der Leitung, sondern ein Paket, das wir beim Mitschnitt verpasst haben. Der Zusammensetzer gibt nach einer Grenze auf, richtet sich am nächsten Segment neu aus und zählt das Ereignis. Frames tragen ihre Länge voran, deshalb findet sich eine Grenze wieder, und höchstens die Frames, die über die Lücke liefen, gehen verloren.",
    ]],
    ["p", "Der letzte Punkt ist wichtig: **Auf der Mitschnitt-Seite mit Verlust rechnen.** Ein Meter, der nach dem ersten verpassten Paket aufhört zu funktionieren, ist ein Meter, dem niemand traut."],
    ["h2", "Dekodieren: das Protokoll verstehen"],
    ["p", "Hat man die Frames, besteht die Arbeit im Herausfinden ihrer Bedeutung: welche Nachricht welche ist, wie die Felder aufgebaut sind, welche Frames komprimiert sind, welche Zahlen Schaden und welche Heilung sind. Das ist geduldiges Beobachten statt kluger Technik: eine Sitzung aufzeichnen, einen bekannten Gegner bekämpfen und vergleichen, was man getan hat, mit dem, was der Datenstrom sagt. Zwei Gewohnheiten haben das beherrschbar gemacht:"],
    ["ul", [
      "**Rohe Sitzungen aufzeichnen und offline abspielen.** Jede Protokolländerung lässt sich an alten Aufnahmen prüfen, und aus einem Fehler wird ein reproduzierbarer Test statt „es ist einmal in einem Raid passiert“.",
      "**Gegen Zahlen prüfen, die das Spiel selbst meldet.** Ein Beispiel sind die Trefferpunkte des Bosses: Der Meter vergleicht den gezählten Schaden mit den Trefferpunkten, die der Boss tatsächlich verloren hat. Eine Abweichung weist auf verpasste Pakete oder einen geänderten Aufbau hin. In Debug-Aufnahmen meldet außerdem ein Gesundheitsmonitor, wenn eine Nachrichtenart, die sonst fließt, nach einem Spielupdate verstummt.",
    ]],
    ["p", "Nachrichtentabellen und Feldaufbauten veröffentlichen wir hier bewusst nicht. Sie ändern sich mit jedem Patch, und das Interessante ist die Methode."],
    ["h2", "Vom Frame zum Kampfprotokoll"],
    ["p", "Ein Frame ist noch kein Kampf. Aus den Schadens- und Heilungsereignissen, die der Server schickt, baut der Meter in drei Schritten das Protokoll nach, das du im Fenster siehst:"],
    ["ol", [
      "**Ereignisse zuordnen.** Jedes Ereignis nennt, wer wen mit welcher Fertigkeit für wie viel getroffen oder geheilt hat. Der Meter ordnet es dem Spieler zu und zählt es als Schaden, Heilung oder erlittenen Schaden.",
      "**Pro Spieler summieren.** Daraus entstehen DPS, iDPS und HPS sowie die Aufschlüsselung nach Fertigkeiten. Die Trefferart steht schon im Ereignis, deshalb sind Krit-Raten gezählt und nicht geschätzt.",
      "**Gegen den Boss prüfen.** Der Boss meldet seine Trefferpunkte selbst. Der Meter vergleicht den gezählten Schaden mit dem tatsächlichen Verlust und warnt bei einer Abweichung über einem Prozent.",
    ]],
    ["p", "Weil jede Sitzung aufgezeichnet und wieder abgespielt werden kann, lässt sich jede dieser Regeln an alten Kämpfen prüfen."],
    ["h2", "Upload und Bestenlisten"],
    ["p", "Nichts verlässt deinen Rechner, solange du nichts hochlädst. Das Hochladen ist freiwillig und standardmäßig aus."],
    ["ul", [
      "**Was hochgeht:** Bei einem Bosskampf der Boss, die beteiligten Spieler, Schaden, Heilung, erlittener Schaden und Fertigkeiten. Dein Charakterprofil geht nur hoch, wenn du es zusätzlich einschaltest.",
      "**Was der Server annimmt:** Nur Bosse, die das Spiel angekündigt hat und die der Katalog kennt. Melden mehrere Spieler denselben Kampf, zählt er einmal. Kämpfe, die im Vergleich mit anderen Kills desselben Bosses unplausibel wirken (Trefferpunkte, Kampfzeit, Schaden je Klasse), werden markiert und geprüft.",
      "**Was das für Daten sind:** Sie beschreiben Spielfiguren, nicht die Person dahinter. Klasse, Ausrüstung, Inventar und Schaden sagen nichts über den Spieler selbst aus. Nur der Spielname könnte unter Umständen einen Rückschluss auf eine Person erlauben. Die [Datenschutzerklärung](/privacy) nennt genau, was gespeichert wird und warum.",
    ]],
    ["h2", "Was dieser Ansatz nicht kann"],
    ["ul", [
      "Er sieht nur, was der Server schickt. Wenn der Server dem Client etwas nicht mitteilt, kann kein Meter es anzeigen.",
      "Verschlüsselter Verkehr würde den Ansatz beenden. Der Kampfstrom dieses Spiels ist lesbar, das ist die Voraussetzung des ganzen Entwurfs.",
      "Er braucht einen Mitschnitt-Treiber. Npcap ist eine eigene Installation, was für manche Spieler eine echte Hürde ist.",
    ]],
    ["h2", "Warum wir es wieder wählen würden"],
    ["ul", [
      "**Wartung:** Patches zerbrechen Aufbauten, keine Hooks, und aufgezeichnete Sitzungen machen jede Korrektur zum Regressionstest.",
      "**Vertrauen:** Spieler können den Quelltext lesen, sehen, dass nichts eingeschleust wird, und selbst beurteilen, was er tut und was nicht.",
      "**Leistung:** Das Spiel bemerkt den Meter nie.",
    ]],
    ["h2", "Fragen und Antworten"],
    ["p", "**Ist das erlaubt?** NCSOFT listet die Verhaltensweisen auf, die sanktioniert werden, etwa Schutzmaßnahmen umgehen, Bots oder Werkzeuge, die Schaden oder Geschwindigkeit erhöhen, und nennt keine einzelnen Programme. Aion DPS tut nichts davon, und Schadensmeter sind weder aufgelistet noch ausdrücklich verboten. Das ist unsere Lesart, keine Aussage von NCSOFT: Prüfe die aktuellen Regeln und nutze es auf eigene Verantwortung."],
    ["p", "**Warum Npcap?** Der Meter braucht eine Kopie des Netzwerkverkehrs, den dein eigener Rechner empfängt, und unter Windows liefert der Npcap-Treiber sie. Er ist eine eigene Installation, und der Meter sendet nichts darüber."],
    ["p", "**Was sieht der Meter nicht?** Nur das, was der Server deinem Client schickt. Was der Server nicht mitteilt, kann kein Meter anzeigen."],
    ["p", "**Bremst er das Spiel?** Der Meter ist ein eigenes Programm und läuft nie im Spielprozess. Der Treiber verwirft alles außer dem Verkehr des Spiels, bevor der Meter es sieht. In einer fünfminütigen Aufzeichnung normalen Spiels lag der Nutzdatenstrom des Spiels im Schnitt bei etwa 2,4 KB pro Sekunde und in der Spitze bei 30 KB pro Sekunde."],
    ["p", "Der Quelltext steht unter MIT-Lizenz: [github.com/SkeeveAN/Aion-DPS-Meter](https://github.com/SkeeveAN/Aion-DPS-Meter). Den Meter, die Bestenlisten und die Funktionsübersicht findest du auf [aiondps.com](/features), und [der Vergleich mit anderen AION-2-Metern](/aion-2-dps-meter) ist ebenfalls aufgeschrieben. Bei jedem Push laufen die Typprüfung und alle Backend-Tests öffentlich, außerdem wird der Client gebaut: [die Testergebnisse](https://github.com/SkeeveAN/Aion-DPS-Meter/actions/workflows/ci.yml)."],
  ],
};

/** Splits a text with **bold** and [label](url) into plain, bold and link parts. */
export function articleSegments(text) {
  const parts = [];
  const re = /\*\*([^*]+)\*\*|\[([^\]]+)\]\(([^)]+)\)/g;
  let last = 0;
  for (let m; (m = re.exec(text)); ) {
    if (m.index > last) parts.push({ text: text.slice(last, m.index) });
    parts.push(m[1] !== undefined ? { text: m[1], bold: true } : { text: m[2], href: m[3] });
    last = re.lastIndex;
  }
  if (last < text.length) parts.push({ text: text.slice(last) });
  return parts;
}
