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
  await page.waitForFunction(
    () => location.hash === '#/home.html' && document.body.innerText.includes('Sign Out'),
    undefined,
    { timeout: 60000 }
  );

  const bootstrapState = await page.evaluate(() => ({
    url: location.href,
    title: document.title,
    bodyText: document.body?.innerText?.slice(0, 2000) || '',
    credentials: localStorage.getItem('jellyfin_credentials'),
    autoLogin: localStorage.getItem('enableAutoLogin')
  }));

  console.log(`PASS: authenticated Jellyfin Web home page (${bootstrapState.url})`);
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
  const standalonePage = await context.newPage();
  await standalonePage.setExtraHTTPHeaders({ 'X-Emby-Token': token });
  await standalonePage.goto('/Randomizer/Page', { waitUntil: 'domcontentloaded' });
  await waitFor(standalonePage.getByRole('heading', { name: /Jellyfin Randomizer/i }), 'standalone page loads');
  await waitFor(standalonePage.locator('#library'), 'standalone library selector');
  await waitFor(standalonePage.locator('#randomize'), 'standalone randomize button');
  const standaloneLibraries = await standalonePage.locator('#library option').allTextContents();
  if (!standaloneLibraries.includes('Allowed Movies')) {
    throw new Error(`Standalone page did not expose the restricted library: ${standaloneLibraries.join(', ')}`);
  }
  await standalonePage.close();
  console.log('PASS: standalone Randomizer page loads with the authenticated Jellyfin session');

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

  await page.locator('.jfr-go').click();
  await waitFor(page.locator('#jfr-details'), 'Movies result appears');

  await page.locator('#jfr-details').click();
  console.log('PASS: Movies result uses normal Jellyfin details route');

  await page.goto('/web/index.html#!/movies.html', { waitUntil: 'domcontentloaded' });
  await waitFor(page.locator('#moviesPage'), 'Movies page reloads for native Play test');
  await waitForSingle(page, '[data-randomizer-button]', 'one Randomize button after Details navigation');
  await page.locator('[data-randomizer-button]').click();
  await waitFor(page.locator('#jfr'), 'Movies Randomizer modal reopens');

  await page.locator('#jfr-q').fill('Allowed Movie 1');
  await page.waitForTimeout(500);
  await waitForSingle(page, '#jfr-results input[type="checkbox"]', 'movie search returns one fixture for Play');
  await page.locator('#jfr-watched').selectOption('Watched');
  await page.locator('#jfr-history').selectOption('No history avoidance');

  const playItemId = await page.locator('#jfr-results input[type="checkbox"]').first().getAttribute('value');
  if (!playItemId) {
    throw new Error('Movie Play test could not resolve the fixture item id.');
  }

  let playbackInfoRequested = false;
  const onPlaybackRequest = request => {
    if (request.method() === 'POST') {
      const pathname = new URL(request.url()).pathname;
      if (new RegExp('/Items/' + playItemId + '/PlaybackInfo$', 'i').test(pathname)) {
        playbackInfoRequested = true;
      }
    }
  };
  page.on('request', onPlaybackRequest);

  await page.evaluate(() => {
    window.__jfrNativePlayClicked = false;
    if (window.__jfrNativePlayListenerInstalled) return;
    document.addEventListener('click', event => {
      const target = event.target?.closest?.('.btnPlay:not(.hide), .btnReplay:not(.hide)');
      if (target) {
        window.__jfrNativePlayClicked = true;
      }
    }, true);
    window.__jfrNativePlayListenerInstalled = true;
  });

  await page.locator('.jfr-go').click();
  await waitFor(page.locator('#jfr-play'), 'Movies result Play button appears');
  const pageCountBeforePlay = context.pages().length;
  await page.locator('#jfr-play').click();
  try {
    await page.waitForFunction(() => window.__jfrNativePlayClicked === true, undefined, { timeout: 30000 });
  } catch (error) {
    const playDiagnostic = await page.evaluate(() => ({
      hash: location.hash,
      visiblePageIds: [...document.querySelectorAll('.page:not(.hide)')].map(page => page.id),
      detailPage: (() => {
        const page = document.querySelector('#itemDetailPage');
        if (!page) return null;
        return {
          className: page.className,
          hidden: page.classList.contains('hide'),
          playButtons: [...page.querySelectorAll('.btnPlay, .btnReplay')].map(button => ({
            className: button.className,
            hidden: button.classList.contains('hide'),
            disabled: button.disabled
          }))
        };
      })()
    }));
    console.log(`PLAY DIAGNOSTIC: ${JSON.stringify(playDiagnostic)}`);
    throw error;
  }

  if (context.pages().length !== pageCountBeforePlay) {
    throw new Error('Randomizer Play opened an unexpected additional browser page.');
  }

  const playbackDeadline = Date.now() + 30000;
  while (!playbackInfoRequested && Date.now() < playbackDeadline) {
    await page.waitForTimeout(250);
  }
  page.off('request', onPlaybackRequest);

  if (!playbackInfoRequested) {
    throw new Error(`Native Jellyfin playback did not request PlaybackInfo for ${playItemId}.`);
  }
  console.log(`PASS: Play clicked Jellyfin's native Details control and requested PlaybackInfo for ${playItemId}`);

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
  await page.waitForFunction(() => /^#\/details\?id=/.test(location.hash), undefined, { timeout: 30000 });
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
