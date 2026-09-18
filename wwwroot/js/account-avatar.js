document.querySelectorAll('[data-avatar-image]').forEach(image => {
    const fallback = () => { image.hidden = true; image.nextElementSibling.hidden = false; };
    image.addEventListener('error', fallback);
    if (image.complete && image.naturalWidth === 0) fallback();
});
