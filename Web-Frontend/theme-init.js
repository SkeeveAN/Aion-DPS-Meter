(function () {
  document.documentElement.classList.add("js");
  try {
    var theme = localStorage.getItem("dpsmeter.theme") || "dark";
    document.documentElement.setAttribute("data-theme", theme);
  } catch (e) {}
})();
