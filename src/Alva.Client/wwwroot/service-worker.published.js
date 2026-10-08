self.importScripts('./service-worker-assets.js');
const cacheName = 'alva-shell-' + self.assetsManifest.version;
const assets = self.assetsManifest.assets.filter(a => !a.url.startsWith('api/') && !a.url.endsWith('.br') && !a.url.endsWith('.gz')).map(a => new Request(a.url, { integrity: a.hash, cache: 'no-cache' }));
self.addEventListener('install', event => event.waitUntil(caches.open(cacheName).then(cache => cache.addAll(assets))));
self.addEventListener('activate', event => event.waitUntil(caches.keys().then(keys => Promise.all(keys.filter(key => key.startsWith('alva-shell-') && key !== cacheName).map(key => caches.delete(key)))).then(() => self.clients.claim())));
self.addEventListener('fetch', event => {
  const url = new URL(event.request.url);
  if (event.request.method !== 'GET' || url.origin !== self.location.origin || url.pathname.startsWith('/api/')) return;
  event.respondWith(caches.open(cacheName).then(async cache => {
    if (event.request.mode === 'navigate') return (await cache.match('index.html')) || fetch(event.request);
    return (await cache.match(event.request)) || fetch(event.request);
  }));
});
