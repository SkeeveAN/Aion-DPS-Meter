// The comparison tables of /aion-2-dps-meter. One source for the page in the app (app.js renderGuide) and for the
// text the server sends to crawlers (Backend/src/seo/pages.ts guidePage). Facts come from each project's own README or
// website, checked 2026-10-19; a cell says "Not stated" when its source says nothing, never "No".
//
// A cell is either a literal ("GPL-3.0", "59") or "$name", which stands for the translated text guide.c.name.

export const GUIDE_ASOF = "2026-10-19";

export const GUIDE_ROWS = [
  "license", "stars", "price", "platform", "reads", "driver", "regions", "overlay", "skills", "cooldown",
  "history", "rankings", "obs", "update", "languages", "rdps", "extras",
];

const OURS = {
  name: "Aion DPS Meter",
  ours: true,
  cells: {
    license: "MIT", stars: "3", price: "$free", platform: "$windows", reads: "$passiveNpcap", driver: "$npcapSeparate",
    regions: "$regionsOurs", overlay: "$overlayClick", skills: "$skillsOurs", cooldown: "$cooldownOurs", history: "$historyOurs",
    rankings: "$rankOurs", obs: "$yes", update: "$updateNoAdmin", languages: "10", rdps: "$rdpsOurs", extras: "$extrasOurs",
  },
};

const NOIA2 = {
  name: "NOIA2", url: "https://github.com/ZDYoung0519/NOIA2",
  cells: {
    license: "GPL-3.0", stars: "59", price: "$free", platform: "$windows", reads: "$passiveNpcap", driver: "$npcap",
    regions: "$regionsNoia", overlay: "$yes", skills: "$na", cooldown: "$na", history: "$yes", rankings: "$rankNoia",
    obs: "$na", update: "$yes", languages: "$langNoia", rdps: "$na", extras: "$extrasNoia",
  },
};

const ALETHEIA = {
  name: "aletheia", url: "https://github.com/p62003/aletheia_AION2_DPS_Meter",
  cells: {
    license: "$licenseNotNamed", stars: "37", price: "$freeSponsor", platform: "$windows", reads: "$passiveNpcap", driver: "$npcap",
    regions: "$regionsAletheia", overlay: "$overlayModes", skills: "$skillsAletheia", cooldown: "$na", history: "$historyAletheia",
    rankings: "$rankAletheia", obs: "$na", update: "$yes", languages: "3", rdps: "$na", extras: "$extrasAletheia",
  },
};

const RATMETER = {
  name: "RATmeter", url: "https://github.com/Kuroukihime/AIon2-Dps-Meter",
  cells: {
    license: "GPL-3.0", stars: "22", price: "$free", platform: "$windows", reads: "$passiveNpcap", driver: "$npcap",
    regions: "$global", overlay: "$yes", skills: "$skillsStats", cooldown: "$yes", history: "$historyLocal",
    rankings: "$rankNone", obs: "$na", update: "$na", languages: "$na", rdps: "$na", extras: "$extrasRat",
  },
};

const AION2FLOW = {
  name: "Aion2Flow", url: "https://github.com/cloris-chan/Aion2Flow",
  cells: {
    license: "GPL-3.0", stars: "19", price: "$free", platform: "$windows", reads: "$passiveWinDivert", driver: "$driverBundled",
    regions: "$na", overlay: "$overlayBoss", skills: "$yes", cooldown: "$na", history: "$historyTen",
    rankings: "$na", obs: "$na", update: "$yes", languages: "3", rdps: "$na", extras: "$extrasFlow",
  },
};

const A2TOOLS = {
  name: "A2Tools", url: "https://github.com/taengu/A2Tools-DPS-Meter",
  cells: {
    license: "GPL-3.0", stars: "70", price: "$freeDonate", platform: "$windowsLinux", reads: "$passiveNpcap", driver: "$npcapCompat",
    regions: "$regionsA2", overlay: "$yes", skills: "$skillsTimeline", cooldown: "$na", history: "$yes",
    rankings: "$rankA2", obs: "$yes", update: "$na", languages: "10", rdps: "$na", extras: "$extrasA2",
  },
};

const DAEVALOG = {
  name: "Daevalog", url: "https://github.com/Seralth/Daevalog",
  cells: {
    license: "AGPL-3.0", stars: "3", price: "$free", platform: "$linuxProton", reads: "$readsTraffic", driver: "$na",
    regions: "$na", overlay: "$yes", skills: "$yes", cooldown: "$na", history: "$yes",
    rankings: "$rankDaeva", obs: "$na", update: "$updateNone", languages: "8", rdps: "$na", extras: "$extrasDaeva",
  },
};

const AIONFLEX = {
  name: "AionFlex", url: "https://aionflex.gg",
  cells: {
    license: "$closed", stars: "–", price: "$priceFlex", platform: "$windows", reads: "$na", driver: "$na",
    regions: "$regionsFlex", overlay: "$overlayFlex", skills: "$skillsFlex", cooldown: "$na", history: "$historyFlex",
    rankings: "$rankFlex", obs: "$na", update: "$yes", languages: "$na", rdps: "$na", extras: "$extrasFlex",
  },
};

const AION2T = {
  name: "aion2t DPS", url: "https://aion2t.com",
  cells: {
    license: "$closedAion2t", stars: "–", price: "$priceAion2t", platform: "$windows", reads: "$passiveNpcap", driver: "$npcapCompat",
    regions: "$regionsAion2t", overlay: "$yes", skills: "$skillsAion2t", cooldown: "$cooldownAion2t", history: "$yes",
    rankings: "$rankAion2t", obs: "$yes", update: "$yes", languages: "10", rdps: "$yes", extras: "$extrasAion2t",
  },
};

const LAGOFAST = {
  name: "LagoFast", url: "https://www.lagofast.com/en/blog/aion-2-dps-tracker/",
  cells: {
    license: "$closed", stars: "–", price: "$priceLago", platform: "$windows", reads: "$na", driver: "$na",
    regions: "$na", overlay: "$yes", skills: "$skillsLago", cooldown: "$na", history: "$na",
    rankings: "$na", obs: "$na", update: "$na", languages: "$na", rdps: "$na", extras: "$extrasLago",
  },
};

// Every table carries our column first, so each one compares the others with us.
export const GUIDE_TABLES = [
  { id: "open1", columns: [OURS, NOIA2, ALETHEIA, RATMETER] },
  { id: "open2", columns: [OURS, AION2FLOW, A2TOOLS, DAEVALOG] },
  { id: "closed", columns: [OURS, AIONFLEX, AION2T, LAGOFAST] },
];

export const GUIDE_WHAT = ["meter", "overlay", "analyzer", "bossfight", "assist", "character", "tools", "stream", "board"];
export const GUIDE_WEAK = ["stars", "regions", "platform", "driver", "missing", "boards", "unreleased"];
export const GUIDE_STRONG = ["analyzer", "oneTool", "boards", "update", "extras"];
export const GUIDE_OSS = ["check", "lockin", "paywall", "fixes", "licenses"];
export const GUIDE_FAQ = ["tracker", "choose", "rdps"];
