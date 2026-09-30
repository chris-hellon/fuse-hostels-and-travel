// The Fuse site's small browser helpers: the scroll and layout effects legacy did with jQuery
// (travaloud.js / site.js). Blazor owns the markup; these only measure, animate, and report back.

const mobile = () => window.innerWidth <= 992;

// ---- Site-wide: header shading, back-to-top, scroll reveals ------------------------------------

let site = null;

/** Reports when the page scrolls past the header (86px) and back-to-top (20px) thresholds; keeps field outlines' label gaps sized. */
export function watchScroll(dotnet) {
    unwatchScroll();
    let last = null;
    const report = () => {
        const y = window.scrollY;
        const state = `${y > 86}|${y > 20}`;
        if (state !== last) {
            last = state;
            dotnet.invokeMethodAsync('OnScrolled', y > 86, y > 20);
        }
        reveal();
    };
    const resize = () => { padMain(); sizeCircles(); sizeLightboxCards(); staggerCards(); report(); };
    window.addEventListener('scroll', report, { passive: true });
    window.addEventListener('resize', resize, { passive: true });
    // Content that appears later (a page shown once its data loads, a form, a new guest row…) needs sizing, its
    // field labels' gaps, and revealing if it's in view, whenever that is.
    let queued = false;
    const outlines = new MutationObserver(() => {
        if (queued) return;
        queued = true;
        // A timer, not requestAnimationFrame, which doesn't run while the tab is in the background.
        setTimeout(() => { queued = false; notch(); settle(); }, 30);
    });
    outlines.observe(document.body, { childList: true, subtree: true });
    site = { report, resize, outlines };
    resize();
}

export function unwatchScroll() {
    if (!site) return;
    window.removeEventListener('scroll', site.report);
    window.removeEventListener('resize', site.resize);
    site.outlines.disconnect();
    site = null;
}

/** Pad under the header, size circles and cards, and reveal what's in view (legacy did all this on load). */
function settle() { padMain(); sizeCircles(); sizeLightboxCards(); staggerCards(); reveal(); }

/** After moving to another page. The header re-rendering for it, and the page appearing once its data loads, are
 *  DOM changes, which settle again (watchScroll). */
export function pageChanged() {
    settle();
}

/** Legacy .has-parallax-scroll: fades in once its top is inside the window. */
export function reveal() {
    const bottom = window.scrollY + window.innerHeight;
    document.querySelectorAll('.has-parallax-scroll:not(.fade-in)').forEach(el => {
        if (el.offsetParent !== null && el.getBoundingClientRect().top + window.scrollY < bottom)
            el.classList.add('fade-in');
    });
}

/**
 * Legacy adjustNavbar: under the always-white header (every page but home) the page starts below it.
 * Blazor never sets main's style, so this is safe to own here. CSS reserves the same space before this runs
 * (_components.scss), so the prerendered page doesn't jump; this keeps it exact if the header's height differs.
 */
export function padMain() {
    const nav = document.querySelector('nav.navbar');
    const main = document.querySelector('main');
    if (!nav || !main) return;
    // !important, as legacy: main's .p-0 is !important too.
    if (nav.classList.contains('always-shadow'))
        main.style.setProperty('padding-top', `${nav.offsetHeight}px`, 'important');
    else
        main.style.removeProperty('padding-top');
}

/** Legacy nav pills: scroll to a section, leaving room for the fixed header. */
export function scrollToSection(id) {
    const target = document.getElementById(id);
    const nav = document.querySelector('nav.navbar');
    if (!target) return;
    window.scrollTo({ top: target.getBoundingClientRect().top + window.scrollY - (nav?.offsetHeight ?? 0), behavior: 'smooth' });
}

export function scrollToTop() {
    window.scrollTo({ top: 0 });
}

/** Stops the page scrolling behind an open modal (MDB's body.modal-open). */
export function lockScroll(locked) {
    document.body.classList.toggle('modal-open', locked);
    document.body.style.overflow = locked ? 'hidden' : '';
}

// ---- Click outside (select dropdowns) ---------------------------------------------------------------

const outside = new Map();

/** Calls back OnClickOutside when a pointer goes down outside the element (MDB closed its select this way). */
export function watchClickOutside(element, dotnet) {
    unwatchClickOutside(element);
    const handler = e => {
        if (!element.contains(e.target))
            dotnet.invokeMethodAsync('OnClickOutside');
    };
    document.addEventListener('pointerdown', handler, true);
    outside.set(element, handler);
}

export function unwatchClickOutside(element) {
    const handler = outside.get(element);
    if (!handler) return;
    document.removeEventListener('pointerdown', handler, true);
    outside.delete(element);
}

// ---- Cookies ------------------------------------------------------------------------------------

export function getCookie(name) {
    return document.cookie.split('; ').some(c => c.startsWith(`${encodeURIComponent(name)}=`));
}

export function setCookie(name, days) {
    const expires = new Date(Date.now() + days * 864e5).toUTCString();
    document.cookie = `${encodeURIComponent(name)}=${encodeURIComponent(new Date().toString())}; expires=${expires}; path=/`;
}

// ---- Form outlines (MDB input): the label notch --------------------------------------------------

/** Sizes each outline's notch to its label, as MDB's input.js does (label width × 0.8 + 8). */
export function notch(root) {
    (root ?? document).querySelectorAll('.form-outline').forEach(outline => {
        const label = outline.querySelector('.form-label:not(.select-fake-value)');
        const middle = outline.querySelector('.form-notch-middle');
        const leading = outline.querySelector('.form-notch-leading');
        if (!label || !middle) return;
        middle.style.width = `${label.clientWidth * 0.8 + 8}px`;
        if (leading) leading.style.width = '9px';
    });
}

// ---- Home page ------------------------------------------------------------------------------------

let home = null;

export function initHome() {
    disposeHome();
    const marquee = startMarquee();
    home = { marquee };
    staggerCards();
    sizeCircles();
    reveal();
    // Legacy revealed what was in view shortly after load, once layout had settled.
    setTimeout(reveal, 800);
}

export function disposeHome() {
    if (!home) return;
    home.marquee?.();
    home = null;
}

/** Legacy adjustCardMargain: every second card in .alternating-card-height is pulled up (desktop). Runs after
 *  every page change and resize, for any page with such a grid (home, tours). */
function staggerCards() {
    document.querySelectorAll('.alternating-card-height').forEach(row => {
        const cards = [...row.querySelectorAll('.card')];
        let marginBottom = 0;
        cards.forEach((card, i) => {
            if (i % 2 === 0) return;
            const column = card.parentElement;
            if (mobile()) {
                column.style.removeProperty('top');
                column.style.removeProperty('margin-bottom');
                return;
            }
            if (marginBottom === 0) {
                column.style.removeProperty('margin-bottom');
                marginBottom = parseFloat(getComputedStyle(column).marginBottom) || 0;
            }
            const offset = 315 + marginBottom;
            column.style.setProperty('top', `-${offset}px`, 'important');
            if (i + 1 === cards.length)
                column.style.setProperty('margin-bottom', `-${offset}px`, 'important');
        });
    });
}

/** Legacy adjustCircularImages: a .full-circle is as tall as its column's content is wide (jQuery .width()). */
function sizeCircles() {
    document.querySelectorAll('.full-circle').forEach(circle => {
        const column = circle.parentElement;
        const style = getComputedStyle(column);
        const width = column.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
        circle.style.width = `${width}px`;
        circle.style.height = `${width}px`;
    });
}

/**
 * Legacy adjustCircularImages / adjustCardMargain for room cards (.lightbox-card): square images, and on
 * desktop every second card is pulled up beside the one before.
 */
function sizeLightboxCards() {
    const cards = [...document.querySelectorAll('.lightbox-card')];
    cards.forEach((card, i) => {
        const img = card.querySelector('img');
        if (img) {
            img.style.removeProperty('width');
            img.style.removeProperty('height');
            const width = img.offsetWidth;
            img.style.width = `${width}px`;
            img.style.height = `${width}px`;
        }
        if (i % 2 === 0) return;
        card.style.removeProperty('top');
        card.style.removeProperty('margin-bottom');
        if (mobile()) return;
        const marginBottom = parseFloat(getComputedStyle(card).marginBottom) || 0;
        card.style.setProperty('top', `${marginBottom}px`, 'important');
        // Legacy's last card also set an invalid padding, so only its top applied.
        if (i + 1 !== cards.length)
            card.style.setProperty('margin-bottom', `-${315 + marginBottom}px`, 'important');
    });
}

/** Legacy setParallaxMarquee: the three rows slide sideways as the page scrolls. */
function startMarquee() {
    const rows = [...document.querySelectorAll('#parallaxBanner .marquee')];
    if (rows.length === 0) return null;
    const settings = [
        { direction: 'left', factor: 7 },
        { direction: 'right', factor: 8 },
        { direction: 'left', factor: 9 },
    ];
    const banners = rows.slice(0, 3).map((element, i) => ({
        element,
        ...settings[i],
        offset: element.getBoundingClientRect().left + window.scrollX,
        width: element.offsetWidth,
    }));

    banners.forEach(b => {
        b.element.style.transform = b.direction === 'left'
            ? `translateX(${-b.width}px)`
            : `translateX(${window.innerWidth - b.offset}px)`;
    });

    let frame = 0;
    const move = () => {
        cancelAnimationFrame(frame);
        frame = requestAnimationFrame(() => {
            const scroll = window.scrollY;
            banners.forEach(b => {
                let position;
                if (b.direction === 'left') {
                    position = -scroll / b.factor - b.offset;
                    if (position < -b.width) position += b.width;
                } else {
                    position = scroll / b.factor + (window.innerWidth - b.offset);
                    if (position > window.innerWidth) position -= b.width;
                }
                b.element.style.transform = `translateX(${position}px)`;
            });
        });
    };
    window.addEventListener('scroll', move, { passive: true });
    return () => {
        cancelAnimationFrame(frame);
        window.removeEventListener('scroll', move);
    };
}

/** Legacy videoplayer.js: the introduction video plays and pauses on click. */
export function playVideo(video, play) {
    if (play) video.play(); else video.pause();
}

// ---- Card carousels (replace Owl Carousel) ---------------------------------------------------------

const carousels = new Map();

/** Watches a carousel's track and reports which page is showing (for the dots). */
export function watchCarousel(track, dotnet) {
    unwatchCarousel(track);
    let page = -1;
    const report = () => {
        const current = Math.round(track.scrollLeft / pageWidth(track));
        if (current !== page) {
            page = current;
            dotnet.invokeMethodAsync('OnPage', current, pages(track));
        }
    };
    const resize = () => { page = -1; report(); };
    track.addEventListener('scroll', report, { passive: true });
    window.addEventListener('resize', resize, { passive: true });
    carousels.set(track, { report, resize });
    report();
}

export function unwatchCarousel(track) {
    const watcher = carousels.get(track);
    if (!watcher) return;
    track.removeEventListener('scroll', watcher.report);
    window.removeEventListener('resize', watcher.resize);
    carousels.delete(track);
}

export function showCarouselPage(track, page) {
    track.scrollTo({ left: page * pageWidth(track), behavior: 'smooth' });
}

/** One page is the visible width plus the gap before the next card. */
function pageWidth(track) {
    return Math.max(1, track.clientWidth + (parseFloat(getComputedStyle(track).columnGap) || 0));
}

function pages(track) {
    const gap = parseFloat(getComputedStyle(track).columnGap) || 0;
    return Math.max(1, Math.round((track.scrollWidth + gap) / pageWidth(track)));
}

// ---- Events calendar (FullCalendar, as legacy) -------------------------------------------------------------

const FullCalendarScript = 'https://cdn.jsdelivr.net/npm/fullcalendar@6.1.15/index.global.min.js';
let fullCalendarLoading = null;
const calendars = new Map();

function loadFullCalendar() {
    if (window.FullCalendar) return Promise.resolve();
    return fullCalendarLoading ??= new Promise((resolve, reject) => {
        const script = document.createElement('script');
        script.src = FullCalendarScript;
        script.onload = resolve;
        script.onerror = reject;
        document.head.appendChild(script);
    });
}

/** Legacy's hostel calendar: month and list views; Blazor supplies each range's events and handles clicks. */
export async function mountCalendar(element, dotnet) {
    await loadFullCalendar();
    unmountCalendar(element);
    const calendar = new FullCalendar.Calendar(element, {
        initialView: 'dayGridMonth',
        firstDay: 1,
        height: 'auto',
        headerToolbar: { left: 'prev,next today', center: 'title', right: 'dayGridMonth,listMonth' },
        buttonText: { today: 'Today', month: 'Month', list: 'List' },
        displayEventTime: true,
        events: (info, success, failure) => {
            dotnet.invokeMethodAsync('LoadEvents', info.startStr, info.endStr).then(success, failure);
        },
        eventClick: info => {
            info.jsEvent.preventDefault();
            dotnet.invokeMethodAsync('ShowEvent', info.event.extendedProps.key);
        },
    });
    calendar.render();
    calendars.set(element, calendar);
}

export function unmountCalendar(element) {
    calendars.get(element)?.destroy();
    calendars.delete(element);
}

/** Legacy's Share button: the device's share sheet, else copy the link, else open it. */
export async function share(title, url) {
    if (navigator.share) {
        try { await navigator.share({ title, text: title, url }); } catch { /* cancelled */ }
    } else if (navigator.clipboard) {
        await navigator.clipboard.writeText(url);
        alert('Link copied to clipboard');
    } else {
        window.open(url, '_blank');
    }
}

// ---- Browser storage (the basket) -----------------------------------------------------------------

/** The stored text, or null when there's none or storage is unavailable (private mode, blocked). */
export function readStorage(key) {
    try { return localStorage.getItem(key); } catch { return null; }
}

export function writeStorage(key, value) {
    try {
        if (value === null) localStorage.removeItem(key);
        else localStorage.setItem(key, value);
    } catch { /* storage unavailable: the basket lasts for this visit only */ }
}

// ---- Cloudbeds booking engine (Immersive Experience 2.0) -----------------------------------------

/**
 * Cloudbeds' loader, which defines <cb-immersive-experience>. Added from here, not the page's markup, because a
 * script Blazor renders never runs. Loaded as a normal script: Cloudbeds' snippet marks it type="text/plain" for
 * sites whose cookie tool switches it on, which this site doesn't have, so as pasted it would never run. No
 * integrity hash: it's pinned to the loader as of today, and "latest" changes, which would block it.
 */
export function loadCloudbeds() {
    const existing = document.querySelector('script[data-entry="immersive"]');
    if (existing) return existing;
    const script = document.createElement('script');
    script.dataset.entry = 'immersive';
    script.src = 'https://static1.cloudbeds.com/booking-engine/latest/loader.js';
    script.crossOrigin = 'anonymous';
    document.head.appendChild(script);
    return script;
}

/**
 * Loads the engine into `element` and resolves true once it has drawn itself (its header appears), or false when
 * it shows its error page (e.g. this domain isn't whitelisted), its script won't load, or it hasn't drawn within `ms`
 * (Cloudbeds down) — so the page can fall back to Cloudbeds' own booking page. The element gets is-ready or
 * is-leaving, which swap the loading panel for the engine or a "taking you there" note.
 */
export function watchCloudbeds(element, ms) {
    return new Promise(resolve => {
        const drawn = () => element.querySelector('.cb-header') !== null;
        const failed = () => element.querySelector('[data-testid="not-found-page"]') !== null;
        let done = false;
        const finish = ok => {
            if (done) return;
            done = true;
            observer.disconnect();
            clearTimeout(timer);
            element.classList.add(ok ? 'is-ready' : 'is-leaving');
            resolve(ok);
        };
        const check = () => { if (drawn()) finish(true); else if (failed()) finish(false); };
        const observer = new MutationObserver(check);
        observer.observe(element, { childList: true, subtree: true });
        const timer = setTimeout(() => finish(drawn()), ms);
        loadCloudbeds().addEventListener('error', () => finish(false));
        check();
    });
}

// ---- reCAPTCHA (the enquiry forms) ---------------------------------------------------------------

let recaptcha;

/** Google's reCAPTCHA v2 script, loaded once when a form first needs it. */
function loadRecaptcha() {
    recaptcha ??= new Promise((resolve, reject) => {
        window.fuseRecaptchaLoaded = () => resolve(window.grecaptcha);
        const script = document.createElement('script');
        script.src = 'https://www.google.com/recaptcha/api.js?onload=fuseRecaptchaLoaded&render=explicit';
        script.async = true;
        script.onerror = () => {
            recaptcha = null;
            reject(new Error('reCAPTCHA did not load'));
        };
        document.head.appendChild(script);
    });
    return recaptcha;
}

/** Draws the "I'm not a robot" box in `element`; returns its id for the two below. */
export async function renderCaptcha(element, siteKey) {
    const grecaptcha = await loadRecaptcha();
    return grecaptcha.render(element, { sitekey: siteKey });
}

/** The box's answer for the server to check (empty until ticked). */
export function captchaResponse(id) {
    return window.grecaptcha?.getResponse(id) ?? '';
}

/** A fresh box (each answer can be checked only once). */
export function resetCaptcha(id) {
    window.grecaptcha?.reset(id);
}

/** Saves `text` as a file called `name` (e.g. My Account's data download). */
export function downloadFile(name, text, type) {
    const url = URL.createObjectURL(new Blob([text], { type }));
    const link = Object.assign(document.createElement('a'), { href: url, download: name });
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}

/** A Google Ads conversion (`send_to`), when the tags are on the page (production only). */
export function trackConversion(sendTo) {
    if (typeof window.gtag === 'function')
        window.gtag('event', 'conversion', { send_to: sendTo });
}
