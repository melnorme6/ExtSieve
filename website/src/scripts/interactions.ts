const root = document.documentElement;
const themeToggle = document.querySelector<HTMLButtonElement>('#theme-toggle');
const media = matchMedia('(prefers-color-scheme: dark)');

function updateTheme() {
  const effective = root.dataset.theme ?? (media.matches ? 'dark' : 'light');
  root.dataset.effectiveTheme = effective;
  const label = effective === 'light' ? themeToggle?.dataset.labelDark : themeToggle?.dataset.labelLight;
  if (label) {
    themeToggle?.setAttribute('aria-label', label);
    themeToggle?.setAttribute('title', label);
  }
  if (viewer?.open && opener?.closest('.step-card')) {
    const matching = opener.closest('.step-card')?.querySelector<HTMLAnchorElement>(`.step-images.theme-${effective}`);
    if (matching) { opener = matching; updateViewerImage(matching); }
  }
}

themeToggle?.removeAttribute('hidden');
themeToggle?.addEventListener('click', () => {
  root.dataset.theme = root.dataset.effectiveTheme === 'dark' ? 'light' : 'dark';
  try { localStorage.setItem('extsieve-theme', root.dataset.theme); } catch { /* Manual choice still works for this page. */ }
  updateTheme();
  closeNavigation();
});
media.addEventListener('change', updateTheme);

const navigation = document.querySelector<HTMLElement>('#main-navigation');
const navigationToggle = document.querySelector<HTMLButtonElement>('#navigation-toggle');
const compact = matchMedia('(max-width: 42rem)');
let lastNavigationFocus: Element | null = null;
function closeNavigation(restoreFocus = false) {
  navigation?.classList.remove('is-open');
  navigationToggle?.setAttribute('aria-expanded', 'false');
  if (restoreFocus) navigationToggle?.focus();
}
root.classList.add('has-interactions');
navigationToggle?.removeAttribute('hidden');
navigationToggle?.addEventListener('click', () => {
  const open = !navigation?.classList.contains('is-open');
  navigation?.classList.toggle('is-open', open);
  navigationToggle.setAttribute('aria-expanded', String(open));
});
navigation?.addEventListener('click', event => {
  const link = (event.target as Element).closest<HTMLAnchorElement>('a');
  if (!link) return;
  closeNavigation();
  if (compact.matches && link.hash && link.origin === location.origin && link.pathname === location.pathname) {
    const destination = document.getElementById(link.hash.slice(1));
    destination?.setAttribute('tabindex', '-1');
    destination?.focus({ preventScroll: true });
  }
});
navigation?.addEventListener('keydown', event => { if (event.key === 'Escape') { event.preventDefault(); closeNavigation(true); } });
compact.addEventListener('change', () => {
  // CSS can hide a control and move focus to body before the media event arrives.
  const previous = document.activeElement === document.body ? lastNavigationFocus : document.activeElement;
  const focusInNavigation = navigation?.contains(previous);
  const focusOnToggle = previous === navigationToggle;
  closeNavigation();
  if (compact.matches && focusInNavigation) navigationToggle?.focus();
  else if (!compact.matches && focusOnToggle) navigation?.querySelector<HTMLAnchorElement>('a')?.focus();
});
document.addEventListener('pointerdown', event => {
  const target = event.target as Element;
  if (!target.closest('#main-navigation, #navigation-toggle')) { lastNavigationFocus = null; closeNavigation(); }
});
document.addEventListener('focusin', event => {
  const target = event.target as Element;
  lastNavigationFocus = target.closest('#main-navigation, #navigation-toggle') ? target : null;
  if (!target.closest('#main-navigation, #navigation-toggle')) closeNavigation();
});

const viewer = document.querySelector<HTMLDialogElement>('#image-viewer');
const image = document.querySelector<HTMLImageElement>('#viewer-image');
const zoom = document.querySelector<HTMLButtonElement>('#image-zoom');
let opener: HTMLAnchorElement | undefined;
function updateViewerImage(link: HTMLAnchorElement) {
  if (!image) return;
  image.src = link.href;
  image.alt = link.dataset.imageAlt ?? '';
  const title = document.querySelector('#image-viewer-title');
  if (title) title.textContent = link.dataset.imageTitle ?? '';
  const original = document.querySelector<HTMLAnchorElement>('#image-original');
  if (original) original.href = link.href;
}
if (viewer && image && typeof viewer.showModal === 'function') {
  for (const link of document.querySelectorAll<HTMLAnchorElement>('a.image-open')) {
    link.addEventListener('click', event => {
      if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || event.button !== 0) return;
      const url = new URL(link.href);
      if (url.origin !== location.origin || !url.pathname.startsWith(import.meta.env.BASE_URL)) return;
      event.preventDefault();
      opener = link;
      updateViewerImage(link);
      viewer.classList.remove('actual-size');
      zoom?.setAttribute('aria-pressed', 'false');
      document.body.classList.add('viewer-open');
      viewer.showModal();
      const scroll = viewer.querySelector('.viewer-scroll');
      scroll?.scrollTo(0, 0);
    });
  }
  document.querySelector('#image-close')?.addEventListener('click', () => viewer.close());
  viewer.addEventListener('close', () => {
    document.body.classList.remove('viewer-open');
    opener?.focus({ preventScroll: true });
  });
  viewer.addEventListener('click', event => {
    const bounds = viewer.getBoundingClientRect();
    if (event.target === viewer && (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom)) viewer.close();
  });
  zoom?.addEventListener('click', () => {
    const actual = viewer.classList.toggle('actual-size');
    zoom.setAttribute('aria-pressed', String(actual));
  });
  viewer.addEventListener('keydown', event => {
    if (event.key !== 'Tab') return;
    const controls = [...viewer.querySelectorAll<HTMLElement>('button, a[href], [tabindex="0"]')];
    const first = controls[0], last = controls.at(-1);
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus(); }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus(); }
  });
}
updateTheme();
