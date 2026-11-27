namespace MoyuTv
{
    internal static class ReaderPresentation
    {
        internal const string Api = "window[Symbol.for('MoyuTV.reader.1')]";
        internal const string Install = @"(() => {
    const key = Symbol.for('MoyuTV.reader.1');
    if (window[key]) return true;
    let session = null, size = 18;
    const chapter = /^(?:上一[章节節頁页回篇]|前一[章节節頁页回篇]|下一[章节節頁页回篇]|后一[章节節頁页回篇]|previous(?: chapter| page)?|next(?: chapter| page)?)(?:\s*[:：].*)?$/i;
    const ignored = 'script,style,noscript,template,iframe,object,embed,canvas,video,audio,svg,button,input,select,textarea,form,nav,aside,footer,menu,[role=navigation],[role=banner],[role=complementary],[role=dialog],[aria-hidden=true],[inert],[hidden]';
    const clutter = /(?:^|[\s_-])(?:advert(?:isement)?|ad-container|comments?|related(?:-posts)?|recommendations?|breadcrumbs?|social-share|page-tools|sidebar)(?:$|[\s_-])/i;
    const gates = 'dialog[open],[aria-modal=true],[role=alertdialog],[class*=paywall i],[id*=paywall i],[class*=pay-mask i],[class*=vip-mask i],[class*=login-mask i],[class*=chapter-lock i],[class*=subscribe-wall i],[class*=regwall i]';
    const safeTags = new Set('p h1 h2 h3 h4 h5 h6 ul ol li blockquote pre code strong em i b sup sub figure figcaption hr br table thead tbody tr th td'.split(' '));
    const css = [
        ':host{color-scheme:light}.sheet{height:100%;box-sizing:border-box;overflow:auto;overscroll-behavior:contain;overflow-wrap:anywhere;background:#faf8f3;color:#252a29;font:var(--size)/1.85 Georgia,Microsoft YaHei UI,serif;scrollbar-gutter:stable;outline:none}',
        '.page{max-width:38em;margin:0 auto;padding:1em clamp(16px,5vw,48px) 2.5em}.tools,.pages{display:flex;align-items:center;gap:.8em;flex-wrap:wrap}.tools{margin-bottom:1.1em}.pages{justify-content:space-between;margin-top:1.5em}',
        'button,.pages a{font:500 14px/1.4 Microsoft YaHei UI,sans-serif;color:#285d55;border:1px solid #aebdb5;border-radius:5px;background:transparent;padding:6px 10px;text-decoration:none;cursor:pointer}button:focus-visible,a:focus-visible{outline:2px solid #346e68;outline-offset:2px}',
        'h1,h2,h3,h4,h5,h6{line-height:1.4;margin:.95em 0 .5em}h1{font-size:1.5em}h2{font-size:1.3em}h3{font-size:1.14em}p,blockquote,figure,pre,ul,ol{margin:.65em 0}.flow div+div{margin-top:.55em}.flow a{color:#215c5c;text-underline-offset:.12em}img{display:block;max-width:100%;height:auto;margin:.6em auto}pre{white-space:pre-wrap;tab-size:4}ul,ol{padding-inline-start:1.6em}blockquote{border-inline-start:3px solid #bbc7bc;padding-inline-start:.85em}table{display:block;overflow:auto;border-collapse:collapse}td,th{padding:.25em .5em;border:1px solid #cdd1ca}figcaption{font-size:.85em}'
    ].join('');
    function notify(event) {
        if (window === window.top) try { window.chrome?.webview?.postMessage('moyu.reader.' + event); } catch (_) {}
    }
    function urlOf(value) {
        if (!value || !value.trim()) return null;
        try {
            const url = new URL(value, document.baseURI);
            return /^https?:$/.test(url.protocol) && !url.username && !url.password ? url : null;
        } catch (_) { return null; }
    }
    function inspect() {
        const memo = new WeakMap();
        function view(el) {
            if (!el) return { visible: true, clips: [] };
            if (memo.has(el)) return memo.get(el);
            const above = view(el.parentElement);
            let state = { visible: false, clips: above.clips };
            if (above.visible && !el.matches('[hidden],[inert],[aria-hidden=true]')) {
                const s = getComputedStyle(el);
                if (s.display !== 'none' && s.visibility === 'visible' && Number(s.opacity) > .05 &&
                    s.contentVisibility !== 'hidden' && s.clipPath === 'none' && (s.clip === 'auto' || !s.clip) &&
                    (!s.maskImage || s.maskImage === 'none') && !/blur\(/i.test(s.filter) &&
                    !(Number(s.webkitLineClamp) > 0)) {
                    state = { visible: true, clips: above.clips, display: s.display };
                    if (el !== document.body && el !== document.documentElement) {
                        const x = /^(hidden|clip)$/.test(s.overflowX), y = /^(hidden|clip)$/.test(s.overflowY);
                        if (x || y) state.clips = above.clips.concat([{ r: el.getBoundingClientRect(), x, y }]);
                    }
                }
            }
            memo.set(el, state);
            return state;
        }
        function within(r, state) {
            return r.width > 0 && r.height > 0 && r.right > 0 &&
                state.clips.every(c => (!c.x || (r.left >= c.r.left - 2 && r.right <= c.r.right + 2)) &&
                    (!c.y || (r.top >= c.r.top - 2 && r.bottom <= c.r.bottom + 2)));
        }
        function shown(el) {
            const state = view(el);
            return state.visible && within(el.getBoundingClientRect(), state);
        }
        function textVisible(node, state) {
            const range = document.createRange();
            range.selectNodeContents(node);
            const rects = Array.from(range.getClientRects()).filter(r => r.width > 0 && r.height > 0);
            return rects.length > 0 && rects.every(r => within(r, state));
        }
        return { view, shown, textVisible };
    }
    function candidates(check) {
        const pool = new Map();
        function add(el, priority, vote) {
            if (!el || !el.matches('article,main,section,div,td,[role=main]') || el.closest(ignored)) return;
            const old = pool.get(el) || { el, priority: 0, votes: 0 };
            old.priority = Math.max(old.priority, priority);
            old.votes += vote;
            pool.set(el, old);
        }
        for (const [selectors, priority] of [
            ['[itemprop=articleBody],#chaptercontent,#chapterContent,#chapter_content,#nr1,.chapter-content,.chapterContent,.read-content,.reading-content,.article-content,.article-body,.entry-content,.post-content',95],
            ['article',70],['main,[role=main]',42],['#content,#contents,.content',25]]) {
            for (const el of document.querySelectorAll(selectors)) add(el, priority, 0);
        }
        for (const p of document.querySelectorAll('p,br,pre,blockquote')) {
            if (!check.view(p).visible) continue;
            let el = p.parentElement;
            for (let depth = 0; el && depth < 3; depth++, el = el.parentElement) add(el, 0, 3 - depth);
        }
        return Array.from(pool.values()).sort((a,b) =>
            (b.priority + Math.min(b.votes,35)) - (a.priority + Math.min(a.votes,35))).map(item => item.el);
    }
    function barrier(root, check) {
        if (Array.from(document.querySelectorAll(gates)).some(el => check.shown(el))) return true;
        // Some consent or subscription screens use an unlabelled fixed backdrop.
        for (let el = document.elementFromPoint(innerWidth / 2, innerHeight / 2); el && el !== document.body; el = el.parentElement) {
            if (el.contains(root)) break;
            const r = el.getBoundingClientRect();
            if (getComputedStyle(el).position === 'fixed' && r.width >= innerWidth * .65 && r.height >= innerHeight * .55) return true;
        }
        return false;
    }
    function extract(root, check) {
        const markers = [];
        function copy(node) {
            if (node.nodeType === Node.TEXT_NODE) {
                const state = check.view(node.parentElement);
                if (!state.visible || !check.textVisible(node, state)) return null;
                const value = node.nodeValue;
                if (value.trim().length >= 18 && markers.length < 6) markers.push({ node, value });
                return document.createTextNode(value);
            }
            if (node.nodeType !== Node.ELEMENT_NODE || node.matches(ignored) || clutter.test(node.className || '')) return null;
            const state = check.view(node);
            if (!state.visible) return null;
            const tag = node.localName;
            if (tag === 'img') {
                if (!check.shown(node) || !node.complete || node.naturalWidth < 64 || node.naturalHeight < 32) return null;
                const src = node.currentSrc;
                if (!urlOf(src) && !/^blob:|^data:image\/(?:png|webp|jpeg|gif|avif);/i.test(src)) return null;
                const img = document.createElement('img');
                img.alt = node.alt; img.src = src; return img;
            }
            let out;
            if (tag === 'a') {
                const url = urlOf(node.getAttribute('href'));
                out = document.createElement(url ? 'a' : 'span');
                if (url) out.href = url.href;
            } else {
                out = document.createElement(safeTags.has(tag) ? tag : /^(inline|contents)/.test(state.display) ? 'span' : 'div');
            }
            for (const child of node.childNodes) {
                const item = copy(child);
                if (item) out.appendChild(item);
            }
            if (!out.hasChildNodes() && !['br','hr'].includes(tag)) return null;
            return out;
        }
        return { body: copy(root), markers };
    }
    function chapterLinks(check) {
        const links = new Map();
        for (const link of document.querySelectorAll('a[href]')) {
            if (!check.shown(link)) continue;
            const url = urlOf(link.getAttribute('href'));
            if (!url || url.origin !== location.origin || url.href === location.href) continue;
            const walker = document.createTreeWalker(link, NodeFilter.SHOW_TEXT);
            let text = '', node;
            while ((node = walker.nextNode()) && text.length < 100) {
                const state = check.view(node.parentElement);
                if (state.visible && check.textVisible(node,state)) text += node.nodeValue;
            }
            const label = text.replace(/^[\s←‹«◀◁▶▷]+|[\s→›»◀◁▶▷]+$/g,''), rel = link.relList;
            if (!chapter.test(label) && !rel.contains('prev') && !rel.contains('next')) continue;
            const previous = rel.contains('prev') || /上|前|previous/i.test(label);
            const title = chapter.test(label) ? label : previous ? '上一页' : '下一页';
            if (!links.has(previous)) links.set(previous, { title, href: url.href });
        }
        return links;
    }
    function close(stale) {
        if (!session) return false;
        const s = session;
        session = null; clearInterval(s.timer);
        s.host.remove();
        if (!stale && location.href === s.url) {
            if (s.focus?.isConnected) s.focus.focus?.({ preventScroll: true });
            window.scrollTo({ left: s.x, top: s.y, behavior: 'instant' });
        }
        notify(stale ? 'stale' : 'off');
        return true;
    }
    function setSize(px) {
        if (Number.isFinite(Number(px)) && px != null) size = Math.min(36, Math.max(12, Number(px)));
        if (session) session.sheet.style.setProperty('--size', size + 'px');
        return size;
    }
    function open(px) {
        setSize(px);
        if (session) return true;
        if (!document.body) return false;
        const check = inspect();
        let content = null, source = null;
        for (const root of candidates(check).slice(0,12)) {
            if (!check.view(root).visible || barrier(root, check)) continue;
            const result = extract(root, check);
            if (!result.body) continue;
            const total = result.body.textContent.replace(/\s/g,'').length;
            const linked = Array.from(result.body.querySelectorAll('a')).reduce((sum,a) => sum + a.textContent.replace(/\s/g,'').length,0);
            if (total - linked >= 70 && linked < total * .65) { content = result; source = root; break; }
        }
        if (!content) return false;
        const host = document.createElement('div'), shadow = host.attachShadow({ mode: 'open' });
        for (const [k,v] of Object.entries({all:'initial',position:'fixed',inset:'0',display:'block','z-index':'2147483647',opacity:'1',visibility:'visible'}))
            host.style.setProperty(k,v,'important');
        const rules = new CSSStyleSheet(); rules.replaceSync(css); shadow.adoptedStyleSheets = [rules];
        const sheet = document.createElement('div'); sheet.className = 'sheet'; sheet.tabIndex = -1;
        sheet.setAttribute('role','dialog'); sheet.setAttribute('aria-modal','true'); sheet.setAttribute('aria-label','正文阅读');
        const page = document.createElement('div'); page.className = 'page';
        const tools = document.createElement('div'); tools.className = 'tools';
        const back = document.createElement('button'); back.textContent = '返回原网页'; back.onclick = () => close(false);
        tools.appendChild(back); page.appendChild(tools);
        if (!content.body.querySelector('h1')) {
            const h = Array.from(document.querySelectorAll('h1')).reverse().find(el =>
                (source.compareDocumentPosition(el) & Node.DOCUMENT_POSITION_PRECEDING) && check.shown(el));
            if (h) {
                const heading = extract(h,check).body;
                if (heading) page.appendChild(heading);
            }
        }
        content.body.classList.add('flow'); page.appendChild(content.body);
        const nav = document.createElement('nav'); nav.className = 'pages'; nav.setAttribute('aria-label','章节导航');
        for (const {title, href} of chapterLinks(check).values()) {
            const a = document.createElement('a'); a.href = href; a.textContent = title; nav.appendChild(a);
        }
        if (nav.childElementCount) page.appendChild(nav);
        sheet.appendChild(page); shadow.appendChild(sheet);
        sheet.addEventListener('click', e => {
            const a = e.target.closest('a[href]');
            if (!a || e.button !== 0 || e.ctrlKey || e.metaKey || e.shiftKey || e.altKey) return;
            e.preventDefault(); const href = a.href; close(true); location.assign(href);
        });
        sheet.addEventListener('keydown', e => { if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); close(false); } });
        const s = { host,sheet,url:location.href,focus:document.activeElement,x:scrollX,y:scrollY,timer:0 };
        session = s; setSize(size); document.documentElement.appendChild(host); sheet.focus({ preventScroll:true });
        s.timer = setInterval(() => {
            const now = inspect();
            if (location.href !== s.url || !host.isConnected || !source.isConnected ||
                content.markers.some(m => !m.node.isConnected || !now.view(m.node.parentElement).visible || m.node.nodeValue !== m.value) ||
                Array.from(document.querySelectorAll(gates)).some(el => now.shown(el))) close(true);
        }, 500);
        notify('on'); return true;
    }
    Object.defineProperty(window, key, { value: { open, close: () => close(false), isOpen: () => !!session, setSize } });
    return true;
})()";
    }
}
