namespace MoyuTv
{
    internal static class PlatformQuality
    {
        // The host retries 'pending' while the site's own menu animates.
        // Only open menus here. Selecting a resolution belongs to the viewer.
        public const string OpenMenuScript = @"(() => {
            const host = location.hostname.toLowerCase();
            const youtube = /(^|\.)youtube(?:-nocookie)?\.com$/.test(host);
            const bilibili = /(^|\.)bilibili\.com$/.test(host);
            if (!youtube && !bilibili) return false;
            const player = document.querySelector(youtube ? '.html5-video-player' : '.bpx-player-container');
            if (!player) return false;

            function reachable(el) {
                if (!el || el.disabled || el.getAttribute('aria-disabled') === 'true') return false;
                const box = el.getBoundingClientRect();
                const left = Math.max(0, box.left), top = Math.max(0, box.top);
                const right = Math.min(innerWidth, box.right), bottom = Math.min(innerHeight, box.bottom);
                if (right <= left || bottom <= top) return false;
                const atPoint = document.elementFromPoint((left + right) / 2, (top + bottom) / 2);
                return atPoint && el.contains(atPoint);
            }
            function moveTo(el, type) {
                const box = el.getBoundingClientRect();
                el.dispatchEvent(new MouseEvent(type, {bubbles: type !== 'mouseenter',
                    clientX: box.left + box.width / 2, clientY: box.top + box.height / 2, view: window}));
            }
            function revealControl(el) {
                const box = el.getBoundingClientRect();
                if (box.right > innerWidth || box.bottom > innerHeight || box.left < 0 || box.top < 0)
                    el.scrollIntoView({block: 'nearest', inline: 'nearest', behavior: 'instant'});
                moveTo(player, 'mouseenter');
                moveTo(player, 'mousemove');
            }
            if (youtube) {
                const settings = player.querySelector('.ytp-settings-button');
                if (!settings) return false;
                const menu = player.querySelector('.ytp-settings-menu');
                if (menu) {
                    const options = menu.querySelectorAll('[role=menuitemradio]');
                    if (Array.from(options).some(el => reachable(el) && /\d{3,4}[pPiI]|[48]K/i.test(el.textContent)))
                        return true;
                    const quality = Array.from(menu.querySelectorAll('[role=menuitem]')).find(el => {
                        const label = el.querySelector('.ytp-menuitem-label');
                        return label && /^(Quality|画质|畫質|清晰度|畫面品質|影片畫質|视频画质|解析度)$/i.test(label.textContent.trim());
                    });
                    if (quality && settings.getAttribute('aria-expanded') === 'true' && !reachable(quality)) {
                        quality.scrollIntoView({block: 'nearest', inline: 'nearest', behavior: 'instant'});
                    }
                    if (reachable(quality)) {
                        quality.click();
                        return 'pending';
                    }
                }
                if (settings.getAttribute('aria-expanded') === 'true') return 'pending';
                revealControl(settings);
                if (reachable(settings)) settings.click();
                return 'pending';
            }

            const button = player.querySelector('.bpx-player-ctrl-quality');
            if (!button) return false;
            const choices = player.querySelectorAll('.bpx-player-ctrl-quality-menu-item');
            if (Array.from(choices).some(reachable)) return true;
            const label = button.querySelector('.bpx-player-ctrl-quality-result') || button;
            revealControl(label);
            if (reachable(label)) {
                moveTo(button, 'mouseenter');
                moveTo(label, 'mouseover');
            }
            return 'pending';
        })()";
    }
}
