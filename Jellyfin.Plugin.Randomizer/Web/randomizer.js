(function () {
    'use strict';

    const CONFIG_ID = '4e1a3b62-3d7f-4d8f-a0a9-2f2f3c9d7c41';
    const BUTTON_MARK = 'data-randomizer-button';
    let API = null;
    let started = false;
    const C = window.JellyfinRandomizerConfig || {};
    let type = null;

    function esc(value) {
        return String(value ?? '').replace(/[&<>"']/g, c => ({
            '&': '&amp;',
            '<': '&lt;',
            '>': '&gt;',
            '"': '&quot;',
            "'": '&#39;'
        }[c]));
    }

    function apiUrl(path) {
        return API.getUrl(path.replace(/^\//, ''));
    }

    function getJson(path) {
        return API.getJSON(apiUrl(path), true);
    }

    function postJson(path, body) {
        return API.ajax({
            type: 'POST',
            url: apiUrl(path),
            data: JSON.stringify(body),
            dataType: 'json',
            contentType: 'application/json'
        });
    }

    function activePage() {
        return document.querySelector('.page:not(.hide)');
    }

    function pageType() {
        const page = activePage();
        if (!page) {
            return null;
        }

        if (page.id === 'moviesPage') {
            return 'Movie';
        }

        if (page.id === 'tvRecommendedPage') {
            return 'Series';
        }

        return null;
    }

    function host() {
        const page = activePage();
        if (!page) {
            return null;
        }

        if (page.id === 'moviesPage') {
            return page.querySelector('#moviesTab .focuscontainer-x');
        }

        if (page.id === 'tvRecommendedPage') {
            return page.querySelector('#seriesTab .focuscontainer-x');
        }

        return null;
    }

    function addButton() {
        type = pageType();

        const existing = document.querySelector('[' + BUTTON_MARK + ']');
        if (type === null) {
            existing?.remove();
            return;
        }

        if (C.enabled === false || existing) {
            return;
        }

        const container = host();
        if (!container) {
            return;
        }

        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'randomizerButton autoSize';
        button.setAttribute(BUTTON_MARK, '');
        button.title = 'Randomize';
        button.textContent = '🎲 Randomize';
        button.addEventListener('click', openModal);
        container.appendChild(button);
    }

    function openModal() {
        const old = document.getElementById('jfr');
        old?.remove();

        const dialog = document.createElement('div');
        dialog.id = 'jfr';
        dialog.className = 'jfr-backdrop';
        dialog.innerHTML = '<div class="jfr-modal" role="dialog" aria-modal="true" aria-labelledby="jfr-title">' +
            '<div class="jfr-head"><h2 id="jfr-title">🎲 Randomize ' +
            (type === 'Movie' ? 'Movies' : 'TV Shows') +
            '</h2><button class="jfr-close" aria-label="Close">×</button></div>' +
            '<select id="jfr-lib" aria-label="Library"></select>' +
            '<input id="jfr-q" class="jfr-search" placeholder="Search titles" aria-label="Search titles">' +
            '<div id="jfr-results" class="jfr-results"></div>' +
            '<div class="jfr-controls">' +
            (type === 'Series'
                ? '<label><input type="radio" name="jfr-mode" value="RandomShow" checked> Random Show</label>' +
                  '<label><input type="radio" name="jfr-mode" value="RandomEpisode"> Random Episode</label>' +
                  '<select id="jfr-strategy" aria-label="Episode strategy">' +
                  '<option value="EqualEpisode">Equal per episode</option>' +
                  '<option value="EqualShow">Equal per show</option></select>'
                : '<strong>Random Movie</strong>') +
            '<select id="jfr-watched" aria-label="Watched filter">' +
            '<option value="All">All</option><option value="Unwatched">Unwatched</option><option value="Watched">Watched</option></select>' +
            '<select id="jfr-history" aria-label="Recent result avoidance">' +
            '<option value="0">No history avoidance</option><option value="1">Avoid 1 recent</option>' +
            '<option value="5">Avoid 5 recent</option><option value="10">Avoid 10 recent</option>' +
            '<option value="20">Avoid 20 recent</option></select>' +
            '</div><div class="jfr-actions">' +
            '<button class="jfr-close2">Cancel</button><button class="jfr-go">Randomize</button>' +
            '</div></div>';

        document.body.appendChild(dialog);

        dialog.querySelectorAll('.jfr-close,.jfr-close2').forEach(button => {
            button.addEventListener('click', () => dialog.remove());
        });

        dialog.addEventListener('keydown', event => {
            if (event.key === 'Escape') {
                dialog.remove();
            }
        });
        dialog.tabIndex = -1;
        dialog.focus();

        const watched = dialog.querySelector('#jfr-watched');
        watched.value = C.DefaultWatchedFilter || 'All';

        const strategy = dialog.querySelector('#jfr-strategy');
        if (strategy) {
            strategy.value = C.DefaultEpisodeStrategy || 'EqualEpisode';
        }

        const history = dialog.querySelector('#jfr-history');
        history.value = String(C.HistorySize || 0);

        dialog.querySelector('#jfr-q').addEventListener('input', () => {
            clearTimeout(window.__jfrSearchTimer);
            window.__jfrSearchTimer = setTimeout(() => search(dialog), 250);
        });

        dialog.querySelector('#jfr-lib').addEventListener('change', () => search(dialog));
        dialog.querySelector('[name="jfr-mode"]')?.addEventListener('change', () => search(dialog));
        dialog.querySelector('.jfr-go').addEventListener('click', () => randomize(dialog));

        loadLibraries(dialog);
    }

    async function loadLibraries(dialog) {
        const select = dialog.querySelector('#jfr-lib');

        try {
            const libraries = await getJson('/Randomizer/Libraries');
            select.innerHTML = '<option value="">Entire accessible library</option>' +
                libraries.map(x => '<option value="' + esc(x.id) + '">' + esc(x.name) + '</option>').join('');
            await search(dialog);
        } catch (error) {
            console.error('Jellyfin Randomizer libraries failed', error);
            dialog.querySelector('#jfr-results').textContent = 'Unable to load accessible libraries.';
        }
    }

    async function search(dialog) {
        const query = new URLSearchParams({
            itemType: type,
            search: dialog.querySelector('#jfr-q').value || '',
            limit: '100'
        });

        const libraryId = dialog.querySelector('#jfr-lib').value;
        if (libraryId) {
            query.set('libraryId', libraryId);
        }

        try {
            const data = await getJson('/Randomizer/Search?' + query);
            dialog.querySelector('#jfr-results').innerHTML =
                data.map(x => '<label><input type="checkbox" value="' + esc(x.id) + '"> ' +
                    esc(x.name) + '</label>').join('') || '<span>No matching titles.</span>';
        } catch (error) {
            console.error('Jellyfin Randomizer search failed', error);
            dialog.querySelector('#jfr-results').textContent = 'Search failed.';
        }
    }

    async function randomize(dialog) {
        const button = dialog.querySelector('.jfr-go');
        const ids = [...dialog.querySelectorAll('#jfr-results input[type="checkbox"]:checked')]
            .map(input => input.value);

        const mode = dialog.querySelector('[name="jfr-mode"]:checked')?.value ||
            (type === 'Series' ? 'RandomShow' : 'RandomMovie');

        const body = {
            mode,
            libraryId: dialog.querySelector('#jfr-lib').value || null,
            itemIds: ids,
            strategy: dialog.querySelector('#jfr-strategy')?.value || C.DefaultEpisodeStrategy || 'EqualEpisode',
            watched: dialog.querySelector('#jfr-watched').value,
            avoidRecent: Number(dialog.querySelector('#jfr-history').value)
        };

        button.disabled = true;

        try {
            const result = await postJson('/Randomizer/Randomize', body);
            showResult(dialog, result);
        } catch (error) {
            console.error('Jellyfin Randomizer request failed', error);
            const message = error?.responseJSON?.Message || error?.message || 'Randomization failed.';
            dialog.querySelector('.jfr-modal').insertAdjacentHTML(
                'beforeend',
                '<p class="jfr-error">' + esc(message) + '</p>'
            );
            button.disabled = false;
        }
    }

    function nativePlayFromDetails(itemId) {
        let attempts = 0;
        const timer = window.setInterval(() => {
            const page = activePage();
            const button = page?.querySelector(
                '.btnPlay:not(.hide), .btnReplay:not(.hide)'
            );

            const hashQuery = location.hash.split('?')[1] || '';
            const currentId = new URLSearchParams(hashQuery).get('id');
            if (currentId !== itemId || page?.id !== 'itemDetailPage') {
                return;
            }

            if (button && !button.disabled) {
                window.clearInterval(timer);
                button.click();
                return;
            }

            attempts += 1;
            if (attempts >= 300) {
                window.clearInterval(timer);
                console.error('Jellyfin Randomizer could not find the native Jellyfin Play control.');
            }
        }, 100);
    }

    function showResult(dialog, result) {
        const episode = result.seriesName
            ? '<p>' + esc(result.seriesName) + ' · S' +
              String(result.seasonNumber ?? 0).padStart(2, '0') + 'E' +
              String(result.episodeNumber ?? 0).padStart(2, '0') + '</p>'
            : '';

        const render = () => {
            dialog.querySelector('.jfr-modal').innerHTML =
                '<div class="jfr-result">' +
                '<div class="jfr-die" aria-hidden="true">🎲</div>' +
                '<h2>' + esc(result.name) + '</h2>' +
                episode +
                '<p>' + esc(result.overview || '') + '</p>' +
                '<div class="jfr-actions">' +
                '<button id="jfr-close">Close</button>' +
                '<button id="jfr-details">Open Details</button>' +
                '<button id="jfr-play">▶ Play</button>' +
                '</div></div>';

            dialog.querySelector('#jfr-close').addEventListener('click', () => dialog.remove());
            dialog.querySelector('#jfr-details').addEventListener('click', () => {
                location.hash = '#/details?id=' + encodeURIComponent(result.itemId);
                dialog.remove();
            });
            dialog.querySelector('#jfr-play').addEventListener('click', () => {
                dialog.remove();

                if (location.hash === '#/details?id=' + encodeURIComponent(result.itemId)) {
                    nativePlayFromDetails(result.itemId);
                    return;
                }

                location.hash = '#/details?id=' + encodeURIComponent(result.itemId);
                nativePlayFromDetails(result.itemId);
            });
        };

        if (C.AnimationEnabled === false) {
            render();
        } else {
            dialog.querySelector('.jfr-die')?.remove();
            setTimeout(render, Math.max(500, Number(C.AnimationDurationMs) || 2200));
        }
    }

    function refresh() {
        window.requestAnimationFrame(addButton);
    }

    function start() {
        if (started) {
            return true;
        }

        API = window.ApiClient || null;
        if (!API) {
            return false;
        }

        started = true;

        API.getPluginConfiguration(CONFIG_ID)
            .then(config => Object.assign(C, {
                enabled: config.Enabled !== false,
                DefaultEpisodeStrategy: config.DefaultEpisodeStrategy,
                DefaultWatchedFilter: config.DefaultWatchedFilter,
                AnimationEnabled: config.AnimationEnabled,
                AnimationDurationMs: config.AnimationDurationMs,
                HistorySize: config.HistorySize
            }))
            .catch(() => undefined)
            .finally(refresh);

        new MutationObserver(refresh).observe(document.body, { childList: true, subtree: true });
        window.addEventListener('hashchange', refresh);
        window.setInterval(refresh, 1000);
        refresh();
        return true;
    }

    let attempts = 0;
    const timer = window.setInterval(() => {
        if (start()) {
            window.clearInterval(timer);
            return;
        }

        attempts += 1;
        if (attempts >= 300) {
            window.clearInterval(timer);
            console.warn('Jellyfin Randomizer waited 30 seconds for the Jellyfin Web ApiClient.');
            window.clearInterval(timer);
        }
    }, 100);
}());
