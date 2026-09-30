// Quill-based rich text for the back office. Quill is loaded on /admin pages only (App.razor).
const editors = new WeakMap();
// Changes are sent after a short pause; flush() sends any pending one straight away (on dispose).
const pending = new WeakMap();

export function create(element, html, dotnet, readOnly, canUpload) {
    const quill = new window.Quill(element, {
        theme: 'snow',
        readOnly,
        placeholder: readOnly ? '' : 'Insert text here...',
        modules: {
            toolbar: readOnly ? false : [
                // Same controls as the legacy editor (MudEx rich text, also Quill).
                [{ header: [1, 2, 3, false] }],
                ['bold', 'italic', 'underline', 'strike'],
                [{ color: [] }, { background: [] }],
                [{ list: 'ordered' }, { list: 'bullet' }, { indent: '-1' }, { indent: '+1' }, { align: [] }],
                ['blockquote', 'code-block'],
                ['link', 'image', 'video'],
                ['clean'],
            ],
        },
    });
    // With an upload, the image button stores the picture and inserts its URL (as legacy travel guides
    // did) instead of Quill's default, which inlines it as base64 into the HTML.
    if (canUpload && !readOnly) {
        quill.getModule('toolbar').addHandler('image', () => pickAndUpload(quill, dotnet));
    }
    quill.clipboard.dangerouslyPasteHTML(html || '');
    const send = () => {
        pending.delete(element);
        return dotnet.invokeMethodAsync('Changed', quill.getSemanticHTML());
    };
    quill.on('text-change', () => {
        clearTimeout(pending.get(element)?.timer);
        pending.set(element, { timer: setTimeout(send, 250), send });
    });
    editors.set(element, quill);
}

export function setHtml(element, html) {
    const quill = editors.get(element);
    if (quill && quill.getSemanticHTML() !== html) quill.clipboard.dangerouslyPasteHTML(html || '');
}

export async function flush(element) {
    const change = pending.get(element);
    if (change) {
        clearTimeout(change.timer);
        await change.send();
    }
}

function pickAndUpload(quill, dotnet) {
    const input = document.createElement('input');
    input.type = 'file';
    input.accept = 'image/*';
    input.onchange = async () => {
        const file = input.files?.[0];
        if (!file) return;
        const range = quill.getSelection(true);
        const base64 = await new Promise((resolve, reject) => {
            const reader = new FileReader();
            reader.onload = () => resolve(String(reader.result).split(',')[1] ?? '');
            reader.onerror = () => reject(reader.error);
            reader.readAsDataURL(file);
        });
        const url = await dotnet.invokeMethodAsync('UploadImage', file.name, file.type, base64);
        if (url) {
            quill.insertEmbed(range?.index ?? quill.getLength(), 'image', url, 'user');
            quill.setSelection((range?.index ?? quill.getLength()) + 1, 0, 'silent');
        }
    };
    input.click();
}
