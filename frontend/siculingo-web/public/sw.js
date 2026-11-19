const CACHE_NAME = 'siculingo-static-v1'
const PRECACHE_URLS = [
  '/manifest.webmanifest',
  '/assets/brand/siculingo-logo.png',
  '/assets/mascot/ficodi-front.png',
  '/assets/pwa/icon-192.png',
  '/assets/pwa/icon-512.png',
]

self.addEventListener('install', event => {
  event.waitUntil(
    caches
      .open(CACHE_NAME)
      .then(cache => cache.addAll(PRECACHE_URLS))
      .then(() => self.skipWaiting()),
  )
})

self.addEventListener('activate', event => {
  event.waitUntil(
    caches
      .keys()
      .then(keys => Promise.all(keys.filter(key => key !== CACHE_NAME).map(key => caches.delete(key))))
      .then(() => self.clients.claim()),
  )
})

self.addEventListener('fetch', event => {
  const { request } = event
  if (request.method !== 'GET') return

  const url = new URL(request.url)
  if (url.origin !== self.location.origin) return

  // Never cache API, health checks, OpenAPI, or Vite dev-server module endpoints
  if (
    url.pathname.startsWith('/api/') ||
    url.pathname.startsWith('/health/') ||
    url.pathname.startsWith('/openapi/') ||
    url.pathname.startsWith('/@') ||
    url.pathname.startsWith('/src/') ||
    url.pathname.startsWith('/node_modules/')
  ) {
    return
  }

  // Static public assets (/assets/*, manifest, built hashed /assets/* bundles): stale-while-revalidate
  if (url.pathname.startsWith('/assets/') || url.pathname === '/manifest.webmanifest') {
    event.respondWith(
      caches.open(CACHE_NAME).then(async cache => {
        const cached = await cache.match(request)
        const networkPromise = fetch(request)
          .then(response => {
            if (response.ok && response.type === 'basic') {
              void cache.put(request, response.clone())
            }
            return response
          })
          .catch(() => cached)

        return cached || (await networkPromise) || Response.error()
      }),
    )
  }
})
