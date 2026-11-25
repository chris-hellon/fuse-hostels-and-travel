// The enquiry dialog's thread: opens (and stays, after a reply or new email) scrolled to the newest email.
export function scrollToEnd(element) {
    if (element) element.scrollTop = element.scrollHeight;
}
