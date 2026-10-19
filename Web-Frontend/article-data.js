// The technical article at /how-it-works. One source for the page in the app (app.js renderArticle) and for the text the
// server sends to crawlers (Backend/src/seo/pages.ts articlePage). English only, like the changelog.
// Inline markup in the texts: **bold** and [label](url).

export const ARTICLE = {
  path: "/how-it-works",
  title: "Passive combat tracking with Npcap and .NET 10",
  description: "How a damage meter can read a game without touching it: packet capture with Npcap and SharpPcap, TCP reassembly, recorded sessions and the limits of the approach.",
  date: "2026-10-19",
  blocks: [
    ["lead", "Most damage meters get their numbers by reading the game's memory or by injecting a hook into the game process. That works, but it is fragile (every patch moves addresses), it can get you banned, and anti-cheat software has good reasons to dislike it. [Aion DPS Meter](/features) takes the other route for AION 2: it never touches the game process. It listens to the game's own network traffic on your machine, the way a packet analyser would, and rebuilds the combat log from that. This article describes the architecture, the pitfalls and what we would do again."],
    ["h2", "The principle"],
    ["p", "The server already tells your client everything: who hit whom, for how much, who healed, who died. If you can read those bytes, you do not need the game process at all. The meter only ever receives a copy of packets that already arrived; it never sends one."],
    ["ul", [
      "**Nothing to inject, nothing to patch around.** A game update changes the data, not the way we read it.",
      "**Safe by construction.** No memory reading, no hooks, no packet injection.",
      "**It sees what the server sends you.** That includes the combat events of everyone in your party or raid, not only your own character.",
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
    ["p", "Once you have frames, the work is reverse engineering what they mean: which message is which, how the fields are laid out, which frames are compressed, which numbers are damage and which are healing. That part is patient observation rather than clever engineering: record a session, fight one known enemy, and compare what you did with what the stream says. Two habits made it tractable:"],
    ["ul", [
      "**Record raw sessions and replay them offline.** Every protocol change can be checked against old recordings, and a bug becomes a reproducible test instead of \"it happened once in a raid\".",
      "**Cross-check against numbers the game reports itself.** One example is the boss's own hit points: the meter compares the damage it counted with the hit points the boss really lost. A mismatch points to missed packets or a changed layout. In debug recordings a health monitor also reports when a kind of message that normally flows goes silent after a game update.",
    ]],
    ["p", "We deliberately do not publish message tables or field layouts here. They change with every patch, and the interesting part is the method."],
    ["h2", "What this design cannot do"],
    ["ul", [
      "It sees only what the server sends. If the server does not tell the client something, no meter can show it.",
      "Encrypted traffic would end the approach. The combat stream of this game is readable, which is the premise of the whole design.",
      "It needs a capture driver. Npcap is a separate install, which is a real hurdle for some players.",
    ]],
    ["h2", "Why we would choose it again"],
    ["ul", [
      "**Maintenance:** patches break layouts, not hooks, and recorded sessions turn every fix into a regression test.",
      "**Trust:** players can read the source, see that nothing is injected, and run the meter next to the game without worrying about their account.",
      "**Performance:** the game never notices the meter.",
    ]],
    ["p", "The source code is MIT licensed: [github.com/SkeeveAN/Aion-DPS-Meter](https://github.com/SkeeveAN/Aion-DPS-Meter). The meter, the leaderboards and the feature tour are on [aiondps.com](/features), and [how it compares with other AION 2 meters](/aion-2-dps-meter) is written down as well."],
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
