(() => {
    'use strict';
    if (!document.body.classList.contains('client-body')) return;
    const header = document.getElementById('siteHeader');
    const updateHeader = () => header?.classList.toggle('cx-scrolled', window.scrollY > 16);
    window.addEventListener('scroll', updateHeader, {passive:true});
    updateHeader();
    const navigation = document.getElementById('clientNavbar');
    navigation?.querySelectorAll('.nav-link').forEach(link => link.addEventListener('click', () => {
        if (window.matchMedia('(max-width: 991px)').matches && window.bootstrap?.Collapse) {
            window.bootstrap.Collapse.getOrCreateInstance(navigation, {toggle: false}).hide();
        }
    }));
    document.querySelectorAll('.cx-media img').forEach(image => {
        const unavailable = () => {
            image.hidden = true;
            image.closest('.cx-media').classList.add('cx-media-unavailable');
            image.closest('.cx-media').querySelector('.cx-media-fallback').hidden = false;
        };
        image.addEventListener('error', unavailable);
        if (image.complete && !image.naturalWidth) unavailable();
    });
    const photoRail = document.querySelector('.ws-gallery-grid');
    photoRail?.addEventListener('focusin', event => {
        const photo = event.target.closest('.ws-gallery-item');
        // A partly visible snap item can receive native focus without being revealed.
        if (photo && photoRail.scrollWidth > photoRail.clientWidth) {
            photo.scrollIntoView({block: 'nearest', inline: 'nearest', behavior: 'auto'});
        }
    });
    const dialog = document.getElementById('clientGallery');
    if (!dialog || typeof dialog.showModal !== 'function') return;
    let opener;
    const galleryImage = dialog.querySelector('img');
    const galleryStatus = dialog.querySelector('[data-gallery-status]');
    galleryImage.addEventListener('load', () => { galleryImage.hidden = false; if (galleryStatus) galleryStatus.hidden = true; });
    galleryImage.addEventListener('error', () => { if (galleryStatus) { galleryImage.hidden = true; galleryStatus.hidden = false; } });
    document.querySelectorAll('[data-gallery-src]').forEach(button => button.addEventListener('click', () => {
        opener = button;
        const image = dialog.querySelector('img');
        image.hidden = false;
        if (galleryStatus) galleryStatus.hidden = true;
        image.src = button.dataset.gallerySrc;
        image.alt = button.dataset.galleryAlt;
        dialog.showModal();
    }));
    dialog.querySelector('[data-gallery-close]').addEventListener('click', () => dialog.close());
    dialog.addEventListener('click', event => {
        if (event.target !== dialog) return;
        const r = dialog.getBoundingClientRect();
        if (event.clientX < r.left || event.clientX > r.right || event.clientY < r.top || event.clientY > r.bottom) dialog.close();
    });
    dialog.addEventListener('close', () => opener?.focus());
})();
