(() => {
  try {
    const saved = localStorage.getItem('extsieve-theme');
    if (saved === 'light' || saved === 'dark') document.documentElement.dataset.theme = saved;
  } catch { /* System remains usable when storage is unavailable. */ }
})();
