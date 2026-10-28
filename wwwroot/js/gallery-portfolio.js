(() => {
    'use strict';
    const dialog = document.getElementById('portfolioViewer');
    const photos = [...document.querySelectorAll('[data-portfolio-src]')];
    if (!dialog || !photos.length) return;
    const stage = dialog.querySelector('[data-portfolio-stage]');
    const status = dialog.querySelector('[data-portfolio-status]');
    const title = dialog.querySelector('#portfolioTitle');
    const credit = dialog.querySelector('[data-portfolio-credit]');
    const counter = dialog.querySelector('[data-portfolio-counter]');
    let selected = 0, opener, revision = 0, pointerStart;

    const show = index => {
        selected = (index + photos.length) % photos.length;
        const photo = photos[selected], current = ++revision;
        const previous = stage.querySelector('img');
        const image = new Image();
        image.alt = photo.dataset.portfolioAlt;
        image.hidden = true;
        image.decoding = 'async';
        // Replace the image node so a late load/error from an earlier selection cannot change this one.
        previous.replaceWith(image);
        title.textContent = photo.dataset.portfolioTitle;
        counter.textContent = `${selected + 1} / ${photos.length}`;
        credit.textContent = photo.dataset.portfolioCredit ? `Photograph: ${photo.dataset.portfolioCredit}` : '';
        credit.hidden = !photo.dataset.portfolioCredit;
        stage.setAttribute('aria-busy', 'true');
        status.textContent = 'Loading photograph…';
        image.onload = () => {
            if (current !== revision || !dialog.open) return;
            image.hidden = false;
            stage.setAttribute('aria-busy', 'false');
            status.textContent = `Photograph ${selected + 1} of ${photos.length}: ${photo.dataset.portfolioTitle}`;
            status.classList.add('wg-status-ready');
        };
        image.onerror = () => {
            if (current !== revision || !dialog.open) return;
            stage.setAttribute('aria-busy', 'false');
            status.textContent = 'This photograph could not be loaded. Choose another photograph or close the viewer and try again.';
        };
        status.classList.remove('wg-status-ready');
        image.src = photo.dataset.portfolioSrc;
    };
    photos.forEach((photo, index) => {
        const thumbnail = photo.querySelector('img');
        const unavailable = () => {
            thumbnail.hidden = true;
            photo.classList.add('wg-photo-unavailable');
            photo.querySelector('.wg-image-fallback').hidden = false;
        };
        thumbnail.addEventListener('error', unavailable);
        if (thumbnail.complete && !thumbnail.naturalWidth) unavailable();
        photo.addEventListener('click', () => {
            if (typeof dialog.showModal !== 'function') { window.location.assign(photo.dataset.portfolioSrc); return; }
            opener = photo;
            dialog.showModal();
            document.body.classList.add('wg-viewer-open');
            show(index);
        });
    });
    dialog.querySelector('[data-portfolio-prev]').addEventListener('click', () => show(selected - 1));
    dialog.querySelector('[data-portfolio-next]').addEventListener('click', () => show(selected + 1));
    dialog.querySelector('[data-portfolio-close]').addEventListener('click', () => dialog.close());
    dialog.addEventListener('keydown', event => {
        if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
            event.preventDefault(); show(selected + (event.key === 'ArrowRight' ? 1 : -1));
        }
        // showModal makes the document inert; keep Tab cycling among the three visible controls.
        if (event.key === 'Tab') {
            const controls = [...dialog.querySelectorAll('button')];
            const first = controls[0], last = controls[controls.length - 1];
            if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
            else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
        }
    });
    dialog.addEventListener('click', event => {
        if (event.target !== dialog) return;
        const bounds = dialog.getBoundingClientRect();
        if (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom) dialog.close();
    });
    stage.addEventListener('pointerdown', event => {
        if (event.isPrimary && event.pointerType === 'touch') pointerStart = {x: event.clientX, y: event.clientY};
    });
    stage.addEventListener('pointerup', event => {
        if (!pointerStart || event.pointerType !== 'touch') return;
        const dx = event.clientX - pointerStart.x, dy = event.clientY - pointerStart.y;
        pointerStart = null;
        if (Math.abs(dx) > 50 && Math.abs(dx) > Math.abs(dy) * 1.5) show(selected + (dx < 0 ? 1 : -1));
    });
    stage.addEventListener('pointercancel', () => { pointerStart = null; });
    dialog.addEventListener('close', () => {
        ++revision; pointerStart = null;
        document.body.classList.remove('wg-viewer-open');
        stage.querySelector('img').removeAttribute('src');
        stage.querySelector('img').hidden = true;
        stage.setAttribute('aria-busy', 'false');
        opener?.focus({preventScroll: true});
    });
})();
