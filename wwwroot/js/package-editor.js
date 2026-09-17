(() => {
    const form = document.getElementById('packageEditorForm');
    if (!form) return;
    const container = document.getElementById('inclusions-container');
    const initial = new URLSearchParams(new FormData(form)).toString();
    let submitting = false;
    const feedback = document.getElementById('inclusionFeedback');
    function renumber() {
        container.querySelectorAll('.package-inclusion-row').forEach((row, index) => {
            row.querySelectorAll('[name], [id], [for], [data-valmsg-for]').forEach(el => {
                ['name','id','for','data-valmsg-for'].forEach(attr => {
                    if (!el.hasAttribute(attr)) return;
                    el.setAttribute(attr, el.getAttribute(attr).replace(/Inclusions\[(?:\d+|__index__)\]/g, 'Inclusions[' + index + ']').replace(/Inclusions_(?:\d+|__index__)__/g, 'Inclusions_' + index + '__'));
                });
            });
        });
        if (window.jQuery?.validator?.unobtrusive) {
            jQuery(form).removeData('validator').removeData('unobtrusiveValidation');
            jQuery.validator.unobtrusive.parse(form);
        }
    }
    function preview() {
        document.getElementById('previewPackageName').textContent = form.elements.Name.value.trim() || 'Package Name';
        const price = Number(form.elements.Price.value);
        document.getElementById('previewPackagePrice').textContent = Number.isFinite(price) ? new Intl.NumberFormat('en-PH',{style:'currency',currency:'PHP'}).format(price) : 'Enter a price';
        document.getElementById('previewPackageDuration').textContent = (form.elements.DurationInHours.value || '0') + ' hour(s)';
        const list = document.getElementById('previewPackageInclusions');
        list.replaceChildren();
        const names = [...container.querySelectorAll('input[name$=".Name"]')].map(el=>el.value.trim()).filter(Boolean);
        names.slice(0,3).forEach(name=>{const li=document.createElement('li');li.textContent=name;list.append(li);});
        if (names.length > 3) { const li=document.createElement('li');li.textContent='+ '+(names.length-3)+' more';list.append(li); }
    }
    document.getElementById('addInclusion').addEventListener('click', () => {
        container.append(document.getElementById('inclusionTemplate').content.cloneNode(true));
        renumber();
        container.lastElementChild.querySelector('input:not([type=hidden])').focus();
        feedback.textContent = 'Inclusion added.';
        preview();
    });
    container.addEventListener('click', event => {
        const remove = event.target.closest('[data-remove-inclusion]');
        if (!remove) return;
        remove.closest('.package-inclusion-row').remove();
        renumber();
        document.getElementById('addInclusion').focus();
        feedback.textContent = 'Inclusion removed.';
        preview();
    });
    form.addEventListener('input', preview);
    form.addEventListener('submit', event => {
        if (!container.children.length) {
            event.preventDefault();
            document.getElementById('inclusionsError').textContent='Add at least one inclusion.';
            document.getElementById('addInclusion').focus();
            return;
        }
        submitting = form.checkValidity() && (!window.jQuery?.validator || jQuery(form).valid());
    });
    window.addEventListener('beforeunload', event => {
        if (!submitting && new URLSearchParams(new FormData(form)).toString() !== initial) {
            event.preventDefault(); event.returnValue = '';
        }
    });
    preview();
})();
