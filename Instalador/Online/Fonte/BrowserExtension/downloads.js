const host = "com.iinask.modloader.downloads";

function fromNexus(value) {
    if (!value) return false;
    try {
        const url = new URL(value);
        return url.protocol === "https:" && ["nexusmods.com", "nexus-cdn.com"].some(
            domain => url.hostname === domain || url.hostname.endsWith("." + domain)
        );
    } catch {
        return false;
    }
}

chrome.downloads.onChanged.addListener(async change => {
    if (change.state?.current !== "complete") return;
    try {
        const [download] = await chrome.downloads.search({ id: change.id });
        if (!download?.exists || download.state !== "complete" || download.incognito ||
            !/\.(zip|rar|7z)$/i.test(download.filename) ||
            ![download.url, download.finalUrl].some(fromNexus)) return;
        await chrome.runtime.sendNativeMessage(host, { path: download.filename });
    } catch {
        // The Mod Loader bridge is optional. The browser retains the download.
    }
});
