(function () {
    "use strict";

    if (!document.body.classList.contains("client-body")) return;

    var reduceMotion = window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;

    function initReveal() {
        var items = document.querySelectorAll("[data-magic-reveal]");
        if (!items.length) return;
        if (reduceMotion || !("IntersectionObserver" in window)) {
            items.forEach(function (item) { item.classList.add("is-magic-visible"); });
            return;
        }

        var observer = new IntersectionObserver(function (entries) {
            entries.forEach(function (entry) {
                if (entry.isIntersecting) {
                    entry.target.classList.add("is-magic-visible");
                    observer.unobserve(entry.target);
                }
            });
        }, { threshold: 0.1, rootMargin: "0px 0px -40px 0px" });

        items.forEach(function (item) { observer.observe(item); });
    }

    function initLoader() {
        window.addEventListener("load", function () {
            var loader = document.getElementById("ariesPageLoader");
            if (loader) loader.classList.add("am-loaded");
        });
    }

    function initHomeHashState() {
        var links = document.querySelectorAll(".client-nav-links .nav-link");
        if (!links.length) return;

        function updateFromHash() {
            var hash = window.location.hash;
            if (!hash) return;
            links.forEach(function (link) {
                var href = link.getAttribute("href") || "";
                if (href.indexOf(hash) !== -1) {
                    links.forEach(function (item) { item.classList.remove("active"); });
                    link.classList.add("active");
                }
            });
        }

        updateFromHash();
        window.addEventListener("hashchange", updateFromHash);
    }

    initLoader();
    document.addEventListener("DOMContentLoaded", function () {
        initReveal();
        initHomeHashState();
    });
})();
