namespace MoyuTv
{
    // This code runs inside the document. The only host message describes the top document's layout.
    internal static class VideoPresentation
    {
        internal const string Api = "window[Symbol.for('MoyuTV.video.1')]";
        internal const string Install = @"(() => {
            const key = Symbol.for('MoyuTV.video.1');
            if (window[key]) return;
            const knownPlayers = '.html5-video-player,.bpx-player-container,.bilibili-player,' +
                '.txp_player,.tenvideo_player,.iqp-player,.iqp-player-videolayer-parent,#flashbox';
            const adRegion = /(?:^|\s)(?:ads?|advertisement|ad-showing|ad-interrupting|ytp-ad-player-overlay|txp_ad|iqp-ad-player)(?:$|\s)/i;
            const previewRegion = /(?:^|\s)(?:(?:video-|ytp-inline-)?preview|preroll|thumbnail|teaser|feed-card)(?:$|[\s_-])/i;
            const isSite = domain => location.hostname === domain || location.hostname.endsWith('.' + domain);
            const topLevel = window === window.top;
            let enabled = false, held = '', layout = null, pending = false, interaction = null;
            const area = video => {
                const box = video.getBoundingClientRect();
                return Math.max(0, Math.min(innerWidth, box.right) - Math.max(0, box.left)) *
                       Math.max(0, Math.min(innerHeight, box.bottom) - Math.max(0, box.top));
            };
            function playerOf(video) {
                const known = video.closest(knownPlayers);
                if (known) return known;
                const size = video.getBoundingClientRect();
                let node = video.parentElement;
                for (let i = 0; node && node !== document.body && i < 6; i++, node = node.parentElement) {
                    const bounds = node.getBoundingClientRect();
                    if (bounds.width > size.width * 1.6 || bounds.height > size.height * 1.7 + 55) break;
                    if (node.querySelector('button,[role=button],.controls,.vjs-control-bar')) return node;
                }
                return video;
            }
            function route() {
                const path = location.pathname;
                if (isSite('youtube.com') || isSite('youtube-nocookie.com'))
                    return /^(?:\/watch$|\/(?:live|shorts|embed)\/[^/]+)/.test(path);
                if (isSite('bilibili.com'))
                    return /^(?:\/video\/|\/bangumi\/play\/|\/cheese\/play\/|\/blackboard\/.*player)/.test(path)
                        || location.hostname === 'player.bilibili.com'
                        || (location.hostname === 'live.bilibili.com' && /^\/(?:blanc\/)?\d+/.test(path));
                if (isSite('iqiyi.com') || isSite('iq.com'))
                    return /^\/(?:v_|w_|play\/)/.test(path);
                if (isSite('v.qq.com')) return /^\/(?:x\/(?:cover|page)\/|txp\/iframe\/player|live\/)/.test(path);
                return null;
            }
            function mediaKey(video) {
                const url = new URL(location.href);
                const episode = ['v','p','bvid','aid','cid','vid'].map(k => url.searchParams.get(k) || '').join('|');
                const src = video.currentSrc || video.getAttribute('src') || '';
                return url.origin + url.pathname + '|' + episode +
                    (route() === null ? '|' + src : '');
            }
            function mainVideo(video) {
                if (video.paused || video.ended || !video.videoWidth || !video.videoHeight || document.hidden) return false;
                const surface = area(video), viewport = Math.max(innerWidth * innerHeight, 1);
                if (surface < Math.min(22000, viewport * .22) || video.closest('a[href]')) return false;
                for (let node = video; node && node !== document.body; node = node.parentElement) {
                    const names = (node.id || '') + ' ' + (node.getAttribute('class') || '');
                    if (adRegion.test(names) || previewRegion.test(names)) return false;
                }
                const match = route();
                if (match !== null) return match;
                const player = playerOf(video);
                const clicked = interaction && performance.now() - interaction.time < 8000 && player.contains(interaction.node);
                return clicked || (surface / viewport >= .28 && (video.controls || player !== video)
                    && Array.from(document.querySelectorAll('video')).filter(v => area(v) > viewport * .12).length === 1);
            }
            function candidates() {
                return Array.from(document.querySelectorAll('video')).map(video => ({
                    video, score: (document.fullscreenElement && document.fullscreenElement.contains(video) ? 2e9 : 0)
                        + (!video.paused && !video.ended ? 1e9 : 0) + area(video)
                })).sort((a,b) => b.score - a.score);
            }
            function signal(active) {
                if (topLevel && window.chrome && window.chrome.webview)
                    window.chrome.webview.postMessage(active ? 'moyu.layout.on' : 'moyu.layout.off');
            }
            function clean() {
                const old = layout;
                if (!old) return;
                layout = null;
                old.observer.disconnect();
                old.style.remove();
                for (const node of old.path) node.removeAttribute(old.marker);
                if (old.controls) old.video.controls = false;
                if (old.kind === 'theater') signal(false);
            }
            function createLayout(video, kind) {
                if (layout) clean();
                const target = playerOf(video);
                const marker = 'data-moyu-' + Math.random().toString(36).slice(2);
                const path = [];
                for (let node = video; node; node = node.parentElement) {
                    node.setAttribute(marker, node === target ? 'player' : 'surface');
                    path.push(node);
                    if (node === target) break;
                }
                // Keep site-managed players in their original DOM and display state.
                // A transformed ancestor otherwise makes position:fixed relative to the page.
                if (kind === 'theater') for (let node = target.parentElement; node; node = node.parentElement) {
                    node.setAttribute(marker, 'ancestor');
                    path.push(node);
                }
                const style = document.createElement('style');
                const player = '[' + marker + '=player]';
                style.textContent = player + '{position:fixed!important;inset:0!important;width:100vw!important;height:100vh!important;' +
                    'min-width:0!important;min-height:0!important;max-width:none!important;max-height:none!important;z-index:2147483646!important;' +
                    'margin:0!important;padding:0!important;border:0!important;background:black!important;box-sizing:border-box!important;}' +
                    '[' + marker + '=surface]{width:100%!important;height:100%!important;max-width:none!important;max-height:none!important;flex-shrink:0!important;}' +
                    '.bpx-player-video-area[' + marker + '=surface]{height:100vh!important;min-height:100vh!important;flex:0 0 100vh!important;}' +
                    '[' + marker + '=ancestor]{transform:none!important;filter:none!important;perspective:none!important;contain:none!important;' +
                    'content-visibility:visible!important;will-change:auto!important;isolation:auto!important;z-index:auto!important;}' +
                    'html[' + marker + '=ancestor]{overflow:hidden!important;}' +
                    'video[' + marker + ']{object-fit:contain!important;left:0!important;top:0!important;margin:0!important;}';
                (document.head || document.documentElement).appendChild(style);
                const controls = target === video && !video.controls;
                if (controls) video.controls = true;
                const state = layout = { video, target, marker, path, style, kind, controls,
                    observer: new MutationObserver(() => {
                        if (!target.isConnected || !video.isConnected) { clean(); schedule(); }
                    }) };
                // Track player replacement without observing attributes, text or media time updates.
                for (let node = video.parentNode; node && node !== document; node = node.parentNode)
                    state.observer.observe(node, {childList:true});
                if (kind === 'theater') signal(true);
                return state;
            }
            function nativeFill(video, fallback) {
                const target = playerOf(video);
                if (!document.fullscreenEnabled || !target.requestFullscreen) {
                    if (fallback && topLevel) createLayout(video,'theater');
                    return;
                }
                target.requestFullscreen().then(() => {
                    if (document.fullscreenElement === target) createLayout(video,'native');
                }).catch(() => {
                    if (fallback && topLevel && video.isConnected && !document.fullscreenElement)
                        createLayout(video,'theater');
                });
            }
            function tryAuto() {
                if (!enabled || document.hidden || document.fullscreenElement) return;
                if (layout && layout.video.isConnected && mediaKey(layout.video) === held) return;
                if (layout) clean();
                const choice = candidates().find(c => mainVideo(c.video));
                if (!choice) return;
                const video = choice.video, id = mediaKey(video);
                if (id === held) return;
                held = id;
                if (topLevel) createLayout(video,'theater');
                else nativeFill(video,false);
            }
            function schedule() {
                if (pending || !enabled) return;
                pending = true;
                requestAnimationFrame(() => { pending = false; tryAuto(); });
            }
            function leave() {
                if (layout) held = mediaKey(layout.video);
                if (document.fullscreenElement) document.exitFullscreen().catch(()=>{});
                clean();
            }
            function manual() {
                if (layout || document.fullscreenElement) { leave(); return true; }
                const best = candidates()[0];
                if (!best || best.score <= 0) return false;
                held = mediaKey(best.video);
                nativeFill(best.video,true);
                return true;
            }
            function setEnabled(value) {
                if (enabled === value) return;
                enabled = value;
                if (value) { held = ''; schedule(); }
                else if (layout && layout.kind === 'theater') leave();
            }
            document.addEventListener('pointerdown',e => {
                if (e.isTrusted) interaction = {node:e.target,time:performance.now()};
            },true);
            for (const name of ['playing','loadeddata','resize','durationchange'])
                document.addEventListener(name,e => { if (e.target instanceof HTMLVideoElement) schedule(); },true);
            document.addEventListener('visibilitychange',schedule);
            window.addEventListener('pageshow',schedule);
            window.addEventListener('popstate',schedule);
            document.addEventListener('yt-navigate-finish',schedule);
            document.addEventListener('fullscreenchange',() => {
                if (!document.fullscreenElement && layout) { held = mediaKey(layout.video); clean(); }
                else if (document.fullscreenElement) {
                    const video = candidates()[0];
                    if (video) held = mediaKey(video.video);
                }
            });
            document.addEventListener('keydown',e => {
                if (e.key === 'Escape' && layout && !document.fullscreenElement) {
                    e.preventDefault(); e.stopPropagation(); leave();
                }
            },true);
            Object.defineProperty(window,key,{value:{
                setEnabled, manual, leave, schedule,
                score:() => {const c = candidates()[0]; return c ? c.score : 0;},
                quality:() => {const c = candidates()[0];return c && c.video.videoWidth && c.video.videoHeight
                    ? c.video.videoWidth + 'x' + c.video.videoHeight : null;}
            }});
        })();";
    }
}
