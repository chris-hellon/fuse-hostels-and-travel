// Picks how much of the wizard's step list fits in the dialog's title row:
// full (numbers + titles) > compact (numbers, current title only) > minimal ("Step 3 of 7", own line).
// Measured, not breakpoint-based, so it works for any number of steps and any dialog width.
const watched = new WeakMap();

// The list is right-aligned, so it overflows to the left, which scrollWidth doesn't count:
// compare the list's own (unshrunk) width with the space it has instead.
function overflows(el) {
    const list = el.querySelector('.wizard-steps__list');
    return list && list.offsetWidth > el.clientWidth + 1;
}

function fit(el) {
    el.classList.remove('is-compact', 'is-minimal');
    if (!overflows(el)) return;
    el.classList.add('is-compact');
    if (!overflows(el)) return;
    el.classList.remove('is-compact');
    el.classList.add('is-minimal');
}

export function watch(el) {
    fit(el);
    // The row's width changes with the window; the steps' own size changes are ours, so don't watch those.
    const observer = new ResizeObserver(() => fit(el));
    observer.observe(el.parentElement);
    watched.set(el, observer);
}

export function refit(el) {
    if (el) fit(el);
}

export function unwatch(el) {
    watched.get(el)?.disconnect();
    watched.delete(el);
}
