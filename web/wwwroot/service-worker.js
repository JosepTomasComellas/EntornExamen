// EntornExamen Service Worker — v2.0.0
// Blazor Server requereix connexió al servidor, però podem:
//   1. Instal·lar l'app com a PWA (standalone)
//   2. Cachear assets estàtics per càrrega més ràpida
//   3. Mostrar pàgina offline quan no hi ha xarxa
// IMPORTANT: Actualitzar CACHE_NAME en cada desplegament per forçar neteja de caché vella.

const CACHE_NAME = 'entornexamen-v2.8.3';

const STATIC_ASSETS = [
    '/offline.html',
    '/css/site.css',
    '/js/app.js',
    '/js/charts.js',
    '/favicon.ico',
    '/images/logo2.png',
];

// ── Instal·lació: pre-cache assets estàtics ──────────────────────────────────
self.addEventListener('install', event => {
    event.waitUntil(
        caches.open(CACHE_NAME).then(cache => cache.addAll(STATIC_ASSETS))
    );
    self.skipWaiting();
});

// ── Activació: elimina caches antigues ───────────────────────────────────────
self.addEventListener('activate', event => {
    event.waitUntil(
        caches.keys().then(keys =>
            Promise.all(keys.filter(k => k !== CACHE_NAME).map(k => caches.delete(k)))
        )
    );
    self.clients.claim();
});

// ── Fetch: network-first per a tot excepte assets estàtics ───────────────────
self.addEventListener('fetch', event => {
    const { request } = event;
    const url = new URL(request.url);

    // Ignora peticions no GET i cross-origin (SignalR WebSocket, etc.)
    if (request.method !== 'GET' || url.origin !== self.location.origin) return;

    // Ignora Blazor i SignalR (requereixen xarxa sempre)
    if (url.pathname.startsWith('/_blazor') ||
        url.pathname.startsWith('/_framework') ||
        url.pathname.startsWith('/_content') ||
        url.pathname.startsWith('/api/')) return;

    // Assets estàtics: cache-first
    if (STATIC_ASSETS.includes(url.pathname)) {
        event.respondWith(
            caches.match(request).then(cached => cached || fetch(request))
        );
        return;
    }

    // Resta (pàgines Blazor): network-first, fallback offline
    event.respondWith(
        fetch(request).catch(() => caches.match('/offline.html'))
    );
});
