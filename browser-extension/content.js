(() => {
  'use strict';
  let lastSent = '', scheduled = false;

  function clean(value) {
    return (value || '').replace(/\s+/g, ' ').trim();
  }

  function inspectPlayer() {
    const player = document.querySelector('ytmusic-player-bar');
    if (!player) return;
    const image = player.querySelector('yt-img-shadow img[src], img.image[src], img[src]');
    const title = clean(player.querySelector('.title')?.textContent);
    const artist = clean(player.querySelector('.byline')?.textContent || player.querySelector('.subtitle')?.textContent);
    const url = PixelCompanionArtwork.largeArtworkUrl(image?.currentSrc || image?.src);
    if (!title || !url) return;
    const key = `${title}\n${url}`;
    if (key === lastSent) return;
    lastSent = key;
    chrome.runtime.sendMessage({type: 'pixel-companion-artwork', title, artist, url}, () => void chrome.runtime.lastError);
  }

  function queue() {
    const all = [...document.querySelectorAll('ytmusic-player-queue-item')];
    const activeIndex = Math.max(0, all.findIndex(item => item.hasAttribute('selected')));
    const start = Math.max(0, Math.min(activeIndex - 5, all.length - 40));
    return all.slice(start, start + 40).map(item => {
      if (!item.dataset.pixelCompanionId) item.dataset.pixelCompanionId = crypto.randomUUID();
      return {
        id: item.dataset.pixelCompanionId,
        title: clean(item.querySelector('.song-title, #song-title, .title')?.textContent),
        artist: clean(item.querySelector('.byline, #byline, .subtitle')?.textContent),
        active: item.hasAttribute('selected')
      };
    }).filter(item => item.title);
  }

  function play(command) {
    if (command?.name !== 'play-track' || !command.trackId) return;
    const item = document.querySelector(`[data-pixel-companion-id="${CSS.escape(command.trackId)}"]`);
    if (item) item.click();
  }

  function synchronize() {
    inspectPlayer();
    chrome.runtime.sendMessage({type: 'pixel-companion-state', tracks: queue()}, response => {
      if (!chrome.runtime.lastError) play(response?.command);
    });
  }

  function schedule() {
    if (scheduled) return;
    scheduled = true;
    setTimeout(() => { scheduled = false; synchronize(); }, 250);
  }

  new MutationObserver(schedule).observe(document.documentElement, {subtree: true, childList: true, attributes: true, attributeFilter: ['src', 'selected']});
  setInterval(synchronize, 2000);
  synchronize();
})();
