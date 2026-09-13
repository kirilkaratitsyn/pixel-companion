const assert = require('node:assert/strict');
const {largeArtworkUrl} = require('../browser-extension/artwork-url.js');

const small = 'https://yt3.googleusercontent.com/example=w60-h60-l90-rj';
assert.equal(largeArtworkUrl(small), 'https://yt3.googleusercontent.com/example=w1200-h1200-l90-rj');
assert.equal(largeArtworkUrl('https://lh3.googleusercontent.com/art=w120-h120-p-l90-rj'), 'https://lh3.googleusercontent.com/art=w1200-h1200-l90-rj');
assert.equal(largeArtworkUrl('https://lh3.googleusercontent.com/art=s60-c-k-c0x00ffffff-no-rj'), 'https://lh3.googleusercontent.com/art=w1200-h1200-l90-rj');
assert.equal(largeArtworkUrl('https://yt3.googleusercontent.com/art'), 'https://yt3.googleusercontent.com/art=w1200-h1200-l90-rj');
assert.equal(largeArtworkUrl('https://i.ytimg.com/vi/id/hqdefault.jpg'), 'https://i.ytimg.com/vi/id/maxresdefault.jpg');
assert.equal(largeArtworkUrl('https://evil.invalid/art=w60-h60-l90-rj'), null);
assert.equal(largeArtworkUrl('http://yt3.googleusercontent.com/art=w60-h60-l90-rj'), null);
console.log('PASS browser artwork URL allowlist and 1200 px source selection');
