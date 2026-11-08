// Local browser notifications for the Alva risk bell (without a push server).
const supported = () => typeof window !== "undefined" && "Notification" in window;

export function permission() {
    return supported() ? Notification.permission : "unsupported";
}

export async function request() {
    if (!supported()) return "unsupported";
    if (Notification.permission !== "default") return Notification.permission;
    return await Notification.requestPermission();
}

export function show(title, body, tag) {
    if (!supported() || Notification.permission !== "granted") return;
    const notification = new Notification(title, { body, tag, icon: "icon-192.png" });
    notification.onclick = () => { window.focus(); window.location.assign(new URL("analytics", document.baseURI).href); notification.close(); };
}
