// File download via object URL (Blazor Server pattern for DotNetStreamReference).
export async function downloadFileFromStream(fileName, streamReference) {
    const arrayBuffer = await streamReference.arrayBuffer();
    const blob = new Blob([arrayBuffer]);
    const url = URL.createObjectURL(blob);

    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = fileName;
    anchor.click();
    anchor.remove();

    URL.revokeObjectURL(url);
}
