import { readFile, readdir, stat } from "node:fs/promises";

const distDirectory = new URL("../dist/", import.meta.url);
const indexPath = new URL("index.html", distDirectory);
const html = await readFile(indexPath, "utf8");
const failures = [];

const assert = (condition, message) => {
  if (!condition) failures.push(message);
};

assert(!html.includes("__SITE_URL__"), "Unresolved __SITE_URL__ token remains in dist/index.html.");
assert(!html.includes('src="/src/'), "A development /src asset reference remains in the build.");
assert((html.match(/<h1\b/g) || []).length === 1, "The page must contain exactly one h1.");
assert(html.includes('class="skip-link"'), "The skip link is missing.");
assert(html.includes('href="#main-content"'), "The skip link does not target the main content.");
assert(html.includes('id="main-content"'), "The main content target is missing.");
assert(!/<meta[^>]+(?:og:image|twitter:image)/i.test(html), "Image metadata is present before a real OG image is supplied.");
assert(!/(google-analytics|googletagmanager|segment\.com|hotjar|mixpanel)/i.test(html), "An unexpected analytics or tracking script is present.");

const ids = new Set([...html.matchAll(/\sid="([^"]+)"/g)].map((match) => match[1]));
const fragmentLinks = [...html.matchAll(/\shref="#([^"]+)"/g)].map((match) => match[1]);
for (const fragment of fragmentLinks) {
  assert(ids.has(fragment), `Fragment link #${fragment} has no matching id.`);
}

for (const anchor of html.matchAll(/<a\b[^>]*target="_blank"[^>]*>/g)) {
  assert(/rel="[^"]*noreferrer[^"]*"/.test(anchor[0]), `External link is missing rel=noreferrer: ${anchor[0]}`);
}

const jsonLdMatch = html.match(/<script type="application\/ld\+json">([\s\S]*?)<\/script>/);
assert(Boolean(jsonLdMatch), "JSON-LD metadata is missing.");
if (jsonLdMatch) {
  try {
    JSON.parse(jsonLdMatch[1]);
  } catch (error) {
    failures.push(`JSON-LD is invalid JSON: ${error.message}`);
  }
}

const walk = async (directoryUrl) => {
  const files = [];
  for (const entry of await readdir(directoryUrl, { withFileTypes: true })) {
    const entryUrl = new URL(entry.name, directoryUrl);
    if (entry.isDirectory()) files.push(...(await walk(new URL(`${entry.name}/`, directoryUrl))));
    else files.push(entryUrl);
  }
  return files;
};

const authoredAssets = (await walk(distDirectory)).filter((url) => /\.(?:html|css|js)$/.test(url.pathname));
let authoredBytes = 0;
for (const asset of authoredAssets) authoredBytes += (await stat(asset)).size;
assert(authoredBytes < 100_000, `Authored HTML/CSS/JS is ${authoredBytes} bytes; expected less than 100 KB before media.`);

const assetDirectory = new URL("assets/", distDirectory);
for (const entry of await readdir(assetDirectory, { withFileTypes: true })) {
  if (!entry.isFile() || !/\.(?:css|js)$/.test(entry.name)) continue;
  const contents = await readFile(new URL(entry.name, assetDirectory), "utf8");
  assert(!contents.includes("__SITE_URL__"), `Unresolved site URL token remains in ${entry.name}.`);
}

if (failures.length > 0) {
  console.error("Portfolio build verification failed:");
  failures.forEach((failure) => console.error(`- ${failure}`));
  process.exitCode = 1;
} else {
  console.log(`Portfolio build verified: ${authoredAssets.length} authored files, ${authoredBytes} bytes before media.`);
}
