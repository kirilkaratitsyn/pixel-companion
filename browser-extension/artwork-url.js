((root, factory) => {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PixelCompanionArtwork = api;
})(globalThis, () => {
  'use strict';
  function largeArtworkUrl(raw) {
    if (!raw || !raw.startsWith('https://')) return null;
    try {
      const url = new URL(raw);
      if (!/^(?:lh[3-6]|yt3)\.googleusercontent\.com$/i.test(url.hostname) && url.hostname !== 'i.ytimg.com') return null;
      if (/^(?:lh[3-6]|yt3)\.googleusercontent\.com$/i.test(url.hostname)) {
        // YouTube currently uses both =w60-h60-... and =s60-... suffixes.
        // Replacing the complete transform avoids silently keeping a tiny source.
        url.pathname = /=([^/?]+)$/i.test(url.pathname)
          ? url.pathname.replace(/=[^/?]+$/i, '=w1200-h1200-l90-rj')
          : `${url.pathname}=w1200-h1200-l90-rj`;
      } else {
        url.pathname = url.pathname.replace(/\/(?:default|mqdefault|hqdefault|sddefault|maxresdefault)\.(?:jpg|webp)$/i, '/maxresdefault.jpg');
      }
      return url.href;
    } catch { return null; }
  }
  return Object.freeze({largeArtworkUrl});
});
