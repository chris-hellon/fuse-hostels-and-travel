// Saves a generated file (e.g. an XLSX export) in the browser.
export function saveFile(fileName, contentType, base64) {
    const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
    const url = URL.createObjectURL(new Blob([bytes], { type: contentType }));
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName ?? '';
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
}
