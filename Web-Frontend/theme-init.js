(function () {
  try {
    var theme = localStorage.getItem("dpsmeter.theme") || "dark";
    document.documentElement.setAttribute("data-theme", theme);
  } catch (e) {}
})();
