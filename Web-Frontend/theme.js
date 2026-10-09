// Theme switcher (aiondps_design_pack_v1). The actual colors live entirely in style.css as
// `[data-theme]` attribute selectors - this module only ever toggles that one attribute on <html>
// and remembers the choice, so no component here needs to know a single hex value. The accent is
// a single fixed brand orange (see style.css's own Accent block), not a visitor choice.
//
// The very first paint already has the right theme applied without this module: index.html's own
// inline bootstrap script sets the attribute from localStorage before the stylesheet is even
// parsed. Importing this module just keeps that in sync (e.g. after a change in another tab) and
// wires up the visible switcher control in the header.
//
// Default is always Dark, never the OS's own light/dark preference - the whole visual identity
// (aiondps_design_pack_v1's cinematic hero art, this brand's dark-navy/orange look) is designed
// dark-first; a visitor whose OS merely prefers light shouldn't see a different first impression
// than the one the brand was actually designed around. Light stays a real, manually-picked option.
const STORAGE_THEME = "dpsmeter.theme";
const DEFAULT_THEME = "dark";

export const THEMES = [
  { id: "dark", labelKey: "theme.dark" },
  { id: "light", labelKey: "theme.light" },
];

function currentTheme() {
  const value = localStorage.getItem(STORAGE_THEME);
  return THEMES.some((th) => th.id === value) ? value : DEFAULT_THEME;
}

export function applyTheme(theme) {
  document.documentElement.setAttribute("data-theme", theme);
  localStorage.setItem(STORAGE_THEME, theme);
}

applyTheme(currentTheme());

const THEME_ICONS = {
  light:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/></svg>',
  dark:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z"/></svg>',
};

/** Wires the header's theme control (sun / moon icon pair). Call once at startup, alongside
 * setupLanguageSwitcher. */
export function initThemeSwitcher(t) {
  const container = document.getElementById("theme-switcher");
  const buttons = THEMES.map((th) => {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "theme-button";
    button.innerHTML = THEME_ICONS[th.id];
    button.setAttribute("aria-label", t(th.labelKey));
    button.title = t(th.labelKey);
    // Either icon flips the theme: clicking the sun while light is already active still goes
    // to dark (and the moon likewise), so repeated clicks on one icon just toggle back and forth.
    button.addEventListener("click", () => {
      applyTheme(currentTheme() === "dark" ? "light" : "dark");
      reflect();
    });
    return { id: th.id, button };
  });
  function reflect() {
    const active = currentTheme();
    for (const { id, button } of buttons) {
      button.classList.toggle("active", id === active);
      button.setAttribute("aria-pressed", String(id === active));
    }
  }
  container.replaceChildren(...buttons.map((b) => b.button));
  reflect();
}
