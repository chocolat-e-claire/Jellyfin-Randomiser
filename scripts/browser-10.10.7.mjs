import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');

const baseUrl = process.env.JELLYFIN_URL || 'http://127.0.0.1:8096';
const username = process.env.JELLYFIN_USER || 'ci-randomizer';
const password = process.env.JELLYFIN_PASSWORD || 'ci-randomizer-password';
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
    throw new Error(`Authentication failed: HTTP ${response.status}: ${await response.text()}`);
  }

  const result = await response.json();
  if (!result.AccessToken) {
    throw new Error('Authentication response did not contain AccessToken.');
  }

  return result.AccessToken;
}

async function waitFor(locator, label, timeout = 60000) {
  await locator.waitFor({ state: 'visible', timeout });
  console.log(`PASS: ${label}`);
}

async function waitForSingle(page, selector, label) {
  await page.waitForFunction(
    sel => document.querySelectorAll(sel).length === 1,
    selector,
    { timeout: 30000 }
  );
  const count = await page.locator(selector).count();
  if (count !== 1) {
    throw new Error(`${label}: expected exactly one element, got ${count}`);
  }
  console.log(`PASS: ${label}`);
}

const token = await authenticate();
const publicInfoResponse = await fetch(new URL('/System/Info/Public', baseUrl));
if (!publicInfoResponse.ok) {
  throw new Error(`Unable to read Jellyfin public system info: HTTP ${publicInfoResponse.status}`);
}
const publicInfo = await publicInfoResponse.json();
const serverId = publicInfo.Id;
if (!serverId) {
  throw new Error('Jellyfin public system info did not contain a server Id.');
}

const meResponse = await fetch(new URL('/Users/Me', baseUrl), { headers: { 'X-Emby-Token': token } });
if (!meResponse.ok) {
  throw new Error(`Unable to resolve authenticated Jellyfin user: HTTP ${meResponse.status}`);
}
const me = await meResponse.json();
if (!me.Id) {
  throw new Error('Authenticated Jellyfin user response did not contain an Id.');
}

const browserInstance = await chromium.launch({ headless: true });
const context = await browserInstance.newContext({
  baseURL: baseUrl
});

await context.addInitScript(({ serverId, baseUrl, token, me }) => {
  const server = {
    DateLastAccessed: Date.now(),
    LastConnectionMode: 2,
    ManualAddress: baseUrl,
    manualAddressOnly: true,
    Name: 'Jellyfin Randomizer Browser CI',
    Id: serverId,
    LocalAddress: baseUrl,
    AccessToken: token,
    UserId: me.Id
  };

  localStorage.setItem('jellyfin_credentials', JSON.stringify({ Servers: [server] }));
  localStorage.setItem(
    `user-${me.Id}-${serverId}`,
    JSON.stringify({
      ...me,
      ServerId: serverId,
      EnableAutoLogin: true
    })
  );
  localStorage.setItem('enableAutoLogin', 'true');
}, { serverId, baseUrl, token, me });

const page = await context.newPage();

async function establishJellyfinWebSession() {
  await page.goto('/web/index.html', { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(5000);

  const bootstrapState = await page.evaluate(() => ({
    url: location.href,
    title: document.title,
    bodyText: document.body?.innerText?.slice(0, 2000) || '',
    credentials: localStorage.getItem('jellyfin_credentials'),
    autoLogin: localStorage.getItem('enableAutoLogin')
  }));

  if (!document.querySelector('#homePage')) {
    console.log(`BOOTSTRAP URL: ${bootstrapState.url}`);
    console.log(`BOOTSTRAP TITLE: ${bootstrapState.title}`);
    console.log(`BOOTSTRAP BODY: ${bootstrapState.bodyText.replace(/\\s+/g, ' ').trim()}`);
    console.log(`BOOTSTRAP CREDENTIALS PRESENT: ${Boolean(bootstrapState.credentials)}`);
    console.log(`BOOTSTRAP AUTO LOGIN: ${bootstrapState.autoLogin}`);
    console.log(`BOOTSTRAP RANDOMIZER ERRORS: ${pluginErrors.join(' | ')}`);
    await page.screenshot({ path: '/tmp/jellyfin-web-bootstrap.png', fullPage: true });
    throw new Error('Jellyfin Web did not reach the authenticated home page after seeded API authentication.');
  }

  console.log('PASS: authenticated Jellyfin Web session seeded from the real Jellyfin API token');
}
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
  await establishJellyfinWebSession();

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
  const libraryOptionNames = await page.locator('#jfr-lib option').allTextContents();
  if (!libraryOptionNames.includes('Allowed Movies')) {
    throw new Error(`Expected Allowed Movies library option, got: ${libraryOptionNames.join(', ')}`);
  }
  console.log('PASS: Movies library selector exposes the restricted user library');

  await page.locator('#jfr-q').fill('Allowed Movie 1');
  await page.waitForTimeout(500);
  await waitForSingle(page, '#jfr-results input[type="checkbox"]', 'movie search returns one fixture');
  await page.locator('#jfr-watched').selectOption('Watched');
  await page.locator('#jfr-history').selectOption('No history avoidance');

  await page.evaluate(() => {
    const pm = window.playbackManager;
    if (!pm?.playItems) {
      throw new Error('Jellyfin playbackManager.playItems is unavailable.');
    }
    if (window.__jfrPlayWrapped) return;
    const original = pm.playItems.bind(pm);
    pm.playItems = (...args) => {
      window.__jfrPlayCalls = args;
      return original(...args);
    };
    window.__jfrPlayWrapped = true;
  });

  await page.locator('.jfr-go').click();
  await page.locator('#jfr-play').click();
  await page.waitForFunction(() => Array.isArray(window.__jfrPlayCalls), undefined, { timeout: 30000 });
  const playCall = await page.evaluate(() => window.__jfrPlayCalls);
  if (!Array.isArray(playCall) || !playCall[0]?.[0]?.Id) {
    throw new Error(`Unexpected playbackManager.playItems arguments: ${JSON.stringify(playCall)}`);
  }
  console.log(`PASS: Play delegated to Jellyfin playbackManager for ${playCall[0][0].Id}`);

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
  await page.locator('#jfr-q').fill('Test Show');
  await page.waitForTimeout(500);
  const showSearchCount = await page.locator('#jfr-results input[type="checkbox"]').count();
  if (showSearchCount !== 2) {
    throw new Error(`Expected two fixture TV shows, got ${showSearchCount}`);
  }
  console.log('PASS: TV search returns both fixture shows');

  await page.locator('#jfr-strategy').selectOption('EqualShow');
  await page.locator('#jfr-watched').selectOption('All');

  await page.locator('input[name="jfr-mode"][value="RandomEpisode"]').check();
  await page.locator('.jfr-go').click();
  await waitFor(page.locator('#jfr-details'), 'TV episode result appears');

  const resultText = await page.locator('.jfr-result').innerText();
  if (!/Test Show A|Test Show B/i.test(resultText)) {
    throw new Error(`TV result did not contain a fixture show name: ${resultText}`);
  }
  if (!/S01E0[12]/.test(resultText)) {
    throw new Error(`TV result did not identify an episode: ${resultText}`);
  }
  console.log('PASS: TV Random Episode returns an actual fixture episode');

  await page.locator('#jfr-details').click();
  await page.waitForURL(/#!\/details\?id=/, { timeout: 30000 });
  console.log('PASS: TV result uses normal Jellyfin details route');

  await page.goto('/web/index.html#!/movies.html', { waitUntil: 'domcontentloaded' });
  await waitForSingle(page, '[data-randomizer-button]', 'one Movies button after TV navigation');
  await page.goto('/web/index.html#!/tvRecommended.html', { waitUntil: 'domcontentloaded' });
  await waitForSingle(page, '[data-randomizer-button]', 'one TV button after second navigation');
  await page.goto('/web/index.html#!/movies.html', { waitUntil: 'domcontentloaded' });
  await waitForSingle(page, '[data-randomizer-button]', 'one Movies button after repeated SPA navigation');
  console.log('PASS: Movies → TV → Movies → TV navigation never duplicates injection');

  if (pluginErrors.length > 0) {
    throw new Error(`Jellyfin Randomizer browser errors:\n${pluginErrors.join('\n')}`);
  }

  console.log('== Browser acceptance passed ==');
} finally {
  await browserInstance.close();
}
