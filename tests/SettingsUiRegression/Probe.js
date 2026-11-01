(() => {
  const send = () => {
    if (innerWidth < window.__verificationMinimum || innerWidth > window.__verificationMaximum) return false;
    const buttons = Array.from(document.querySelectorAll('.window-controls-polished .window-button-polished'));
    const main = document.querySelector('.settings-main');
    const toggle = document.getElementById('setting-trayIconShowsDeviceType');
    if (buttons.length !== 2 || !main || !toggle) return false;
    if (Array.from(document.querySelectorAll('main')).some(node => Number(getComputedStyle(node).opacity) < 0.999)) return false;
    if (main.scrollHeight > main.clientHeight + 2 && !main.classList.contains('fade-bottom')) return false;
    const rects = buttons.map(node => {
      const rect = node.getBoundingClientRect();
      return { label: node.getAttribute('aria-label') || '', left: rect.left, right: rect.right, width: rect.width, height: rect.height, radius: getComputedStyle(node).borderRadius };
    });
    if (rects.some(rect => rect.width === 0 || rect.height === 0)) return false;
    window.chrome.webview.postMessage({ type: 'verificationSnapshot', url: location.href, width: innerWidth, height: innerHeight, buttons: rects, hasDeviceToggle: true, horizontalOverflow: document.documentElement.scrollWidth > innerWidth, mask: getComputedStyle(main).maskImage, scripts: Array.from(document.scripts, node => node.src).filter(Boolean), readyAtMs: performance.now() });
    return true;
  };
  document.fonts.ready.then(() => {
    if (send()) return;
    const inspect = () => { if (send()) { observer.disconnect(); window.removeEventListener('resize', inspect); } };
    const observer = new MutationObserver(inspect);
    observer.observe(document.documentElement, { subtree: true, childList: true, attributes: true });
    window.addEventListener('resize', inspect);
  });
})();
