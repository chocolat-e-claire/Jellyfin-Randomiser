import { chromium } from 'playwright';

const baseUrl = process.env.JELLYFIN_URL || 'http://127.0.0.1:8096';
const username = process.env.JELLYFIN_USER || 'ci-admin';
const password = process.env.JELLYFIN_PASSWORD || 'ci-admin-password';
const authHeader = 'MediaBrowser Client="Jellyfin Randomizer Browser CI", DeviceId="jfr-browser-ci", Device="Playwright", Version="10.10.7"';

async function authenticate() {
  const response = await fetch(new URL('/Users/AuthenticateByName', baseUrl), {
    method: 'POST',
    headers: {
      Authorization: authHeader,
      'Content-Type': 'application/json'
    },
    body: JSON.stringify({ Username: username, Pw: password })
  });

  if (!response.ok) {
    throw new Error(\`Authentication failed: HTTP \${response.status}: \${await response.text()}\`);
  }

  const result = await response.json();
  if (!result.AccessToken) {
    throw new Error('Authentication response did not contain AccessToken.');
  }

  return result.AccessToken;
}

async function waitFor(locator, label, timeout = 60000) {
  await locator.waitFor({ state: 'visible', timeout });
  console.log(\`PASS: \${label}\`);
}

async function waitForSingle(page, selector, label) {
  await page.waitForFunction(
    sel => document.querySelectorAll(sel).length === 1,
    selector,
    { timeout: 30000 }
  );
  const count = await page.locator(selector).count();
  if (count !== 1) {
    throw new Error(\`\${label}: expected exactly one element, got \${count}\`);
  }
  console.log(\`PASS: \${label}\`);
}

const token = await authenticate();
const browserInstance = await chromium.launch({ headless: true });
const context = await browserInstance.newContext({
  baseURL: baseUrl,
  extraHTTPHeaders: {
    'X-Emby-Token': token
  }
});

const page = await context.newPage();
const pluginErrors = [];

page.on('console', message => {
  if (message.type() === 'error' && /Jellyfin Randomizer/i.test(message.text())) {
    pluginErrors.push(message.text());
  }
});

page.on('pageerror', error => {
  if (/Randomizer/i.test(error.message)) {
    pluginErrors.push(error.message);
  }
});

try {
  console.log('== Standalone Randomizer page ==');
  await page.goto('/Randomizer/Page', { waitUntil: 'domcontentloaded' });
  await waitFor(page.getByRole('heading', { name: /Jellyfin Randomizer/i }), 'standalone page loads');
  await waitFor(page.locator('#library'), 'standalone library selector');
  await waitFor(page.locator('#randomize'), 'standalone randomize button');

  console.log('== Jellyfin Movies integration ==');
  await page.goto('/web/index.html#!/movies.html', { waitUntil: 'domcontentloaded' });
  await waitFor(page.locator('#moviesPage'), 'Movies page loads');
  await waitForSingle(page, '[data-randomizer-button]', 'one Randomize button on Movies');
  await page.waitForTimeout(1500);
  await waitForSingle(page, '[data-randomizer-button]', 'Randomize button remains deduplicated on Movies');

  await page.locator('[data-randomizer-button]').click();
  await waitFor(page.locator('#jfr'), 'Movies Randomizer modal opens');
  await page.locator('.jfr-go').click();
  await waitFor(page.locator('#jfr-details'), 'Movies result appears');
  await page.locator('#jfr-details').click();
  await page.waitForURL(/#!\/details\?id=/, { timeout: 30000 });
  console.log('PASS: Movies result uses normal Jellyfin details route');

  console.log('== Jellyfin TV integration ==');
  await page.goto('/web/index.html#!/tvRecommended.html', { waitUntil: 'domcontentloaded' });
  await waitFor(page.locator('#tvRecommendedPage'), 'TV Recommended page loads');
  await waitForSingle(page, '[data-randomizer-button]', 'one Randomize button on TV');
  await page.waitForTimeout(1500);
  await waitForSingle(page, '[data-randomizer-button]', 'Randomize button remains deduplicated on TV');

  await page.locator('[data-randomizer-button]').click();
  await waitFor(page.locator('#jfr'), 'TV Randomizer modal opens');
  await page.locator('input[name="jfr-mode"][value="RandomEpisode"]').check();
  await page.locator('.jfr-go').click();
  await waitFor(page.locator('#jfr-details'), 'TV episode result appears');

  const resultText = await page.locator('.jfr-result').innerText();
  if (!/Test Show A|Test Show B/i.test(resultText)) {
    throw new Error(\`TV result did not contain a fixture show name: \${resultText}\`);
  }
  if (!/S01E0[12]/.test(resultText)) {
    throw new Error(\`TV result did not identify an episode: \${resultText}\`);
  }
  console.log('PASS: TV Random Episode returns an actual fixture episode');

  await page.locator('#jfr-details').click();
  await page.waitForURL(/#!\/details\?id=/, { timeout: 30000 });
  console.log('PASS: TV result uses normal Jellyfin details route');

  if (pluginErrors.length > 0) {
    throw new Error(\`Jellyfin Randomizer browser errors:\\n\${pluginErrors.join('\\n')}\`);
  }

  console.log('== Browser acceptance passed ==');
} finally {
  await browserInstance.close();
}
