const searchInput = document.getElementById("search-input");
const searchResults = document.getElementById("search-results");
const navToggle = document.getElementById("nav-toggle");
const sidebar = document.getElementById("sidebar");
const themeToggle = document.getElementById("theme-toggle");

function setupThemeToggle() {
  if (!themeToggle) return;

  const setTheme = (theme) => {
    document.documentElement.dataset.theme = theme;
    try {
      localStorage.setItem("lash-docs-theme", theme);
    } catch {
      // Storage can be unavailable in strict local-file contexts.
    }
    const isDark = theme === "dark";
    themeToggle.setAttribute("aria-pressed", String(isDark));
    themeToggle.querySelector(".theme-toggle-text").textContent = isDark ? "Dark" : "Light";
  };

  const current = document.documentElement.dataset.theme === "dark" ? "dark" : "light";
  setTheme(current);

  themeToggle.addEventListener("click", () => {
    const next = document.documentElement.dataset.theme === "dark" ? "light" : "dark";
    setTheme(next);
  });
}

function markActiveNav() {
  const path = window.location.pathname.split("/").pop() || "index.html";
  for (const link of document.querySelectorAll(".chapter-list a")) {
    const href = link.getAttribute("href");
    if (href === path) {
      link.classList.add("active");
      link.setAttribute("aria-current", "page");
    }
  }
}

function renderResults(results) {
  if (!searchResults) return;
  if (!results.length) {
    searchResults.innerHTML = "";
    return;
  }

  const html = results
      .slice(0, 8)
      .map((item) => `<a href="${item.url}">${item.title}</a>`)
      .join("");

  searchResults.innerHTML = `<div class="search-results-title">Matches</div>${html}`;
}

async function setupSearch() {
  if (!searchInput || !searchResults) return;

  let index = [];
  try {
    const res = await fetch("search-index.json");
    index = await res.json();
  } catch {
    return;
  }

  searchInput.addEventListener("input", () => {
    const q = searchInput.value.trim().toLowerCase();
    if (!q) {
      searchResults.innerHTML = "";
      return;
    }

    const tokens = q.split(/\s+/g).filter(Boolean);
    const scored = [];

    for (const item of index) {
      const hay = `${item.title} ${item.headings.join(" ")} ${item.text}`.toLowerCase();
      let score = 0;
      let matchedAll = true;
      for (const token of tokens) {
        if (!hay.includes(token)) {
          matchedAll = false;
          break;
        }
        if (item.title.toLowerCase().includes(token)) score += 5;
        if (item.headings.join(" ").toLowerCase().includes(token)) score += 3;
        score += 1;
      }
      if (matchedAll) scored.push({ ...item, score });
    }

    scored.sort((a, b) => b.score - a.score || a.title.localeCompare(b.title));
    renderResults(scored);
  });
}

if (navToggle && sidebar) {
  navToggle.addEventListener("click", () => {
    sidebar.classList.toggle("open");
  });
}

markActiveNav();
setupThemeToggle();
setupSearch();
