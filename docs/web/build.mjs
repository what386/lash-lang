import fs from "node:fs/promises";
import path from "node:path";
import MarkdownIt from "markdown-it";

const ROOT = path.resolve(path.dirname(new URL(import.meta.url).pathname));
const BOOK_DIR = path.resolve(ROOT, "../book");
const SUMMARY_PATH = path.join(BOOK_DIR, "SUMMARY.md");
const SITE_DIR = path.join(ROOT, "site");
const ASSETS_DIR = path.join(SITE_DIR, "assets");

const md = new MarkdownIt({ html: false, linkify: true, typographer: false });

function chapterSlug(fileName) {
  const stem = fileName.replace(/\.md$/i, "");
  if (stem.toLowerCase() === "readme") return "index";
  return stem
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, "-")
      .replace(/^-+|-+$/g, "");
}

function chapterUrl(fileName) {
  const slug = chapterSlug(fileName);
  return `${slug}.html`;
}

function stripHtml(html) {
  return html.replace(/<[^>]*>/g, " ").replace(/\s+/g, " ").trim();
}

function escapeHtml(value) {
  return value
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;")
      .replace(/'/g, "&#39;");
}

function parseSummary(summaryText) {
  const chapters = [];
  const re = /^\s*-\s+\[(.+?)\]\(\.\/(.+?\.md)\)\s*$/;

  for (const line of summaryText.split(/\r?\n/)) {
    const m = line.match(re);
    if (!m) continue;
    const title = m[1].trim();
    const file = m[2].trim();
    chapters.push({ title, file });
  }

  if (chapters.length === 0) {
    throw new Error("No chapters found in docs/book/SUMMARY.md");
  }

  return chapters;
}

function rewriteMarkdownLinks(markdown, fileToUrl) {
  return markdown.replace(/\]\(((?:\.\/)?[^\)]+\.md)\)/g, (full, rawHref) => {
    const href = rawHref.trim();
    const normalized = href.replace(/^\.\//, "");
    if (normalized === "SUMMARY.md") return "](index.html)";
    if (normalized === "../language-spec.md") return "](../../language-spec.md)";

    const target = fileToUrl.get(normalized);
    if (!target) return full;
    return `](${target})`;
  });
}

function collectHeadings(markdown) {
  const headings = [];
  const re = /^(#{1,3})\s+(.+)$/gm;
  let m;
  while ((m = re.exec(markdown)) !== null) {
    const depth = m[1].length;
    const text = m[2].trim();
    headings.push({ depth, text });
  }
  return headings;
}

function renderTemplate({ pageTitle, siteTitle, navHtml, contentHtml, prev, next }) {
  const prevHtml = prev
      ? `<a class="pager-link" href="${prev.url}">\u2190 ${escapeHtml(prev.title)}</a>`
      : '<span class="pager-spacer"></span>';
  const nextHtml = next
      ? `<a class="pager-link" href="${next.url}">${escapeHtml(next.title)} \u2192</a>`
      : '<span class="pager-spacer"></span>';

  return `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${escapeHtml(pageTitle)} - ${escapeHtml(siteTitle)}</title>
  <script>
    (() => {
      const stored = (() => {
        try {
          return localStorage.getItem("lash-docs-theme");
        } catch {
          return null;
        }
      })();
      const prefersDark = window.matchMedia("(prefers-color-scheme: dark)").matches;
      document.documentElement.dataset.theme = stored || (prefersDark ? "dark" : "light");
    })();
  </script>
  <link rel="stylesheet" href="assets/styles.css">
</head>
<body>
  <header class="topbar">
    <button class="nav-toggle" id="nav-toggle" aria-label="Toggle navigation">Menu</button>
    <a class="brand" href="index.html">${escapeHtml(siteTitle)}</a>
    <label class="search-wrap" for="search-input">
      <span class="search-label">Search</span>
      <input id="search-input" type="search" placeholder="Find chapter text..." autocomplete="off">
    </label>
    <button class="theme-toggle" id="theme-toggle" type="button" aria-label="Switch color theme" aria-pressed="false">
      <span class="theme-toggle-icon" aria-hidden="true"></span>
      <span class="theme-toggle-text">Theme</span>
    </button>
  </header>

  <div class="layout">
    <aside class="sidebar" id="sidebar">
      <nav>
        ${navHtml}
      </nav>
      <div class="search-results" id="search-results" aria-live="polite"></div>
    </aside>

    <main class="content" id="main-content">
      ${contentHtml}
      <footer class="pager">
        ${prevHtml}
        ${nextHtml}
      </footer>
    </main>
  </div>

  <script src="assets/app.js" defer></script>
</body>
</html>`;
}

async function main() {
  const summary = await fs.readFile(SUMMARY_PATH, "utf8");
  const chapters = parseSummary(summary);

  const fileToUrl = new Map(chapters.map((c) => [c.file, chapterUrl(c.file)]));

  await fs.rm(SITE_DIR, { recursive: true, force: true });
  await fs.mkdir(ASSETS_DIR, { recursive: true });

  const searchIndex = [];

  const navHtml = `<ul class="chapter-list">${chapters
      .map((c) => `<li><a href="${chapterUrl(c.file)}">${escapeHtml(c.title)}</a></li>`)
      .join("")}</ul>`;

  for (let i = 0; i < chapters.length; i++) {
    const chapter = chapters[i];
    const chapterPath = path.join(BOOK_DIR, chapter.file);
    const originalMarkdown = await fs.readFile(chapterPath, "utf8");
    const rewritten = rewriteMarkdownLinks(originalMarkdown, fileToUrl);
    const htmlBody = md.render(rewritten);
    const headings = collectHeadings(originalMarkdown);

    const prev = i > 0 ? { title: chapters[i - 1].title, url: chapterUrl(chapters[i - 1].file) } : null;
    const next =
        i < chapters.length - 1
            ? { title: chapters[i + 1].title, url: chapterUrl(chapters[i + 1].file) }
            : null;

    const page = renderTemplate({
      pageTitle: chapter.title,
      siteTitle: "The Lash Book",
      navHtml,
      contentHtml: `<article>${htmlBody}</article>`,
      prev,
      next,
    });

    const outFile = path.join(SITE_DIR, chapterUrl(chapter.file));
    await fs.writeFile(outFile, page, "utf8");

    searchIndex.push({
      title: chapter.title,
      url: chapterUrl(chapter.file),
      headings: headings.map((h) => h.text),
      text: stripHtml(htmlBody),
    });
  }

  await fs.writeFile(path.join(SITE_DIR, "search-index.json"), JSON.stringify(searchIndex, null, 2), "utf8");

  await fs.copyFile(path.join(ROOT, "src", "styles.css"), path.join(ASSETS_DIR, "styles.css"));
  await fs.copyFile(path.join(ROOT, "src", "app.js"), path.join(ASSETS_DIR, "app.js"));

  console.log(`Generated ${chapters.length} pages in ${SITE_DIR}`);
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : error);
  process.exit(1);
});
