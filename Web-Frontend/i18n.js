import { GAME_NAME_TRANSLATIONS } from "./game-data.js";
// Supported languages match what the Aion client itself ships with. Flags are shown in the
// language switcher in the header; `intl` is the tag handed to toLocaleString() for numbers/dates.
export const LOCALES = [
  { code: "de", flag: "🇩🇪", label: "Deutsch", intl: "de-DE" },
  { code: "en", flag: "🇬🇧", label: "English", intl: "en-US" },
  { code: "fr", flag: "🇫🇷", label: "Français", intl: "fr-FR" },
  { code: "es", flag: "🇪🇸", label: "Español", intl: "es-ES" },
  { code: "ru", flag: "🇷🇺", label: "Русский", intl: "ru-RU" },
  { code: "pl", flag: "🇵🇱", label: "Polski", intl: "pl-PL" },
  { code: "tr", flag: "🇹🇷", label: "Türkçe", intl: "tr-TR" },
  { code: "zh", flag: "🇨🇳", label: "中文", intl: "zh-CN" },
];

const SUPPORTED_CODES = LOCALES.map((l) => l.code);

// Strings live in locales/<code>.js, one file per language, so a visitor only downloads the one he reads (plus English as the fallback).
const TRANSLATIONS = {};

async function loadLocale(code) {
  if (!TRANSLATIONS[code]) {
    TRANSLATIONS[code] = (await import(`./locales/${code}.js`)).default;
  }
}

// A stored choice (dpsmeter.locale, written by the switcher) wins; otherwise the first
// supported language from the browser's preference list; otherwise English.
function detectLocale() {
  try {
    const stored = localStorage.getItem("dpsmeter.locale");
    if (stored && SUPPORTED_CODES.includes(stored)) {
      return stored;
    }
  } catch {
    // storage blocked - fall through to the browser language
  }
  const preferred = navigator.languages?.length ? navigator.languages : [navigator.language];
  for (const tag of preferred) {
    const base = String(tag || "").toLowerCase().split("-")[0];
    if (SUPPORTED_CODES.includes(base)) {
      return base;
    }
  }
  return "en";
}

let currentLocale = detectLocale();
// Top-level await: the module (and so every t() call) only runs once the strings are there.
await Promise.all([loadLocale("en"), loadLocale(currentLocale)]);

export function getLocale() {
  return currentLocale;
}

export async function setLocale(code) {
  if (!SUPPORTED_CODES.includes(code)) {
    return;
  }
  await loadLocale(code);
  currentLocale = code;
  try {
    localStorage.setItem("dpsmeter.locale", code);
  } catch {
    // storage blocked - the choice just won't survive the visit
  }
  document.documentElement.lang = code;
}

document.documentElement.lang = currentLocale;

function intlTag() {
  return LOCALES.find((l) => l.code === currentLocale)?.intl ?? "en-US";
}

export function t(key, params) {
  const raw = TRANSLATIONS[currentLocale]?.[key] ?? TRANSLATIONS.en[key] ?? key;
  if (!params) {
    return raw;
  }
  return raw.replace(/\{(\w+)\}/g, (match, name) => (name in params ? params[name] : match));
}

export function formatNumber(n) {
  return Math.round(n).toLocaleString(intlTag());
}

export function formatDate(date) {
  return date.toLocaleString(intlTag());
}


export function translateGameName(rawName) {
  const entry = GAME_NAME_TRANSLATIONS[rawName];
  if (!entry) {
    return rawName;
  }
  return entry[currentLocale] ?? entry.en ?? rawName;
}
