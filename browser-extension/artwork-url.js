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
        url.pathname = url.pathname.replace(/=w\d+-h\d+[^/?]*/i, '=w1200-h1200-l90-rj');
      }
      return url.href;
    } catch { return null; }
  }
  return Object.freeze({largeArtworkUrl});
});
