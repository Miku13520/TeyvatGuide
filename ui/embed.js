const args = new URLSearchParams(location.search);
const player = new URL('https://player.bilibili.com/player.html');
for (const key of ['bvid', 'aid', 'p', 't']) {
  if (args.has(key)) player.searchParams.set(key, args.get(key));
}
player.searchParams.set('autoplay', '0');
player.searchParams.set('danmaku', '0');
document.querySelector('iframe').src = player.href;
