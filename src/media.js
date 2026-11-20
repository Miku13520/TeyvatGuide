// Evaluated in the selected media document, including the official player iframe.
(function (action, argument, cleanPicture) {
  function findMedia(root) {
    const candidates = Array.from(root.querySelectorAll('video,bwp-video'))
      .filter(el => typeof el.play === 'function' && el.getBoundingClientRect().width > 0);
    if (candidates.length) return candidates.sort((a, b) => b.clientWidth - a.clientWidth)[0];
    for (const el of root.querySelectorAll('*')) {
      if (el.shadowRoot) {
        const nested = findMedia(el.shadowRoot);
        if (nested) return nested;
      }
    }
    return null;
  }
  let state = window.__teyvatGuideMedia;
  let video = state && state.video;
  if (!video || !video.isConnected || !video.clientWidth) video = findMedia(document);
  if (!video) return null;
  if (typeof cleanPicture === 'boolean') {
    const container = document.querySelector('.bpx-player-container.bpx-player-embedded');
    if (container) {
      const ready = video.readyState >= 2 ||
        (state && state.video === video && container.hasAttribute('data-teyvat-clean') && video.readyState > 0);
      const hideChrome = cleanPicture && ready && !video.error;
      if (hideChrome && !window.__teyvatGuideChrome) {
        const sheet = new CSSStyleSheet();
        sheet.replaceSync(`
          [data-teyvat-clean] :is(
            .bpx-player-top-wrap,
            .bpx-player-control-wrap,
            .bpx-player-ending-wrap,
            .bpx-player-enter-button,
            .bpx-player-relation-wrap
          ) { display: none !important; }
        `);
        document.adoptedStyleSheets = [...document.adoptedStyleSheets, sheet];
        window.__teyvatGuideChrome = sheet;
      }
      container.toggleAttribute('data-teyvat-clean', hideChrome);
    } else if (location.protocol === 'file:' && video.localName === 'video') {
      if (cleanPicture && video.controls && video.readyState >= 2) {
        video.setAttribute('data-teyvat-restore-controls', '');
        video.controls = false;
      } else if (!cleanPicture && video.hasAttribute('data-teyvat-restore-controls')) {
        video.controls = true;
        video.removeAttribute('data-teyvat-restore-controls');
      }
    }
  }
  if (!state || state.video !== video) {
    state = {video, issue: ''};
    window.__teyvatGuideMedia = state;
    video.addEventListener('loadedmetadata', () => { state.issue = ''; });
    video.addEventListener('playing', () => { state.issue = ''; });
  }
  const rates = [0.5, 0.75, 1, 1.25, 1.5, 1.75, 2];
  const duration = Number.isFinite(video.duration) ? video.duration : 0;
  function play() {
    if (video.ended) video.currentTime = 0;
    const started = video.play();
    if (started && started.catch) started.catch(error => {
      if (error.name === 'AbortError') return;
      state.issue = error.name === 'NotAllowedError' ? '请先解锁悬浮窗，点击播放器内的播放按钮。'
        : '视频暂时无法播放，请检查网络或查看播放器提示。';
    });
  }
  try {
    if (action !== 'read') state.issue = '';
    switch (action) {
      case 'toggle': if (video.paused || video.ended) play(); else video.pause(); break;
      case 'play': play(); break;
      case 'pause': video.pause(); break;
      case 'forward': case 'back': case 'seek':
        if (duration > 0) {
          const target = action === 'seek' ? argument : video.currentTime + (action === 'back' ? -argument : argument);
          video.currentTime = Math.max(0, Math.min(duration, target));
        }
        break;
      case 'faster': video.playbackRate = rates.find(r => r > video.playbackRate + 0.01) || 2; break;
      case 'slower': video.playbackRate = rates.slice().reverse().find(r => r < video.playbackRate - 0.01) || 0.5; break;
    }
  } catch (error) { state.issue = '播放器暂时无法执行操作，请在悬浮窗中检查播放状态。'; }
  return {
    time: video.currentTime || 0,
    duration,
    rate: video.playbackRate || 1,
    ready: video.readyState >= 2,
    seeking: video.seeking,
    paused: video.paused || video.ended,
    issue: video.error ? '当前视频无法解码或加载，请检查网络或视频格式。' : state.issue
  };
})
