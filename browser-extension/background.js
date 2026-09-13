'use strict';

let lastSequence = 0;

chrome.runtime.onMessage.addListener((message, sender, reply) => {
  if (!sender.url?.startsWith('https://music.youtube.com/')) return;
  if (message?.type === 'pixel-companion-artwork') {
    transfer(message).then(ok => reply({ok})).catch(() => reply({ok: false})); return true;
  }
  if (message?.type === 'pixel-companion-state') {
    synchronize(message.tracks).then(command => reply({command})).catch(() => reply({command: null})); return true;
  }
});

function headerText(value) {
  const bytes = new TextEncoder().encode(value || '');
  let binary = '';
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary);
}

async function transfer(message) {
  const url = new URL(message.url);
  if (!/^(?:lh[3-6]|yt3)\.googleusercontent\.com$/i.test(url.hostname) && url.hostname !== 'i.ytimg.com') return false;
  const image = await fetch(url.href, {cache: 'force-cache'});
  if (!image.ok || !/^image\/(?:jpeg|png|webp)$/i.test(image.headers.get('content-type') || '')) return false;
  const bytes = await image.arrayBuffer();
  if (bytes.byteLength < 12 || bytes.byteLength > 8 * 1024 * 1024) return false;
  const response = await fetch('http://127.0.0.1:8765/api/browser-artwork', {
    method: 'POST',
    headers: {
      'Content-Type': image.headers.get('content-type').split(';')[0],
      'X-Pixel-Companion': 'browser-artwork-v1',
      'X-Pixel-Companion-Title': headerText(message.title),
      'X-Pixel-Companion-Artist': headerText(message.artist)
    },
    body: bytes
  });
  return response.ok;
}

async function synchronize(tracks) {
  const response = await fetch('http://127.0.0.1:8765/api/browser-state', {
    method: 'POST',
    headers: {'Content-Type': 'application/json', 'X-Pixel-Companion': 'browser-state-v1'},
    body: JSON.stringify({afterSequence: lastSequence, tracks: Array.isArray(tracks) ? tracks.slice(0, 60) : []})
  });
  if (!response.ok) return null;
  const {command} = await response.json();
  if (!command || command.sequence <= lastSequence) return null;
  lastSequence = command.sequence;
  return command;
}
