// =============================================================================
// ARIES THE MAGIC ARTIST — CLIENT EXPERIENCE MICROINTERACTIONS
// -----------------------------------------------------------------------------
// Progressive enhancement only: every feature here guards for missing
// elements so it is safe to load on every client page, and none of it
// touches routing, forms submission, or model binding. Only runs when
// document.body has the "client-body" class, so it never runs on the
// Staff/Admin/Owner dashboard pages.
// =============================================================================
(function () {
  "use strict";

  if (!document.body.classList.contains("client-body")) {
    return;
  }

  var reduceMotion = window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  document.addEventListener("DOMContentLoaded", function () {
    initNavbarScrollState();
    initRipple();
    initScrollReveal();
    initHeroParticles();
    initFormShake();
    initSparkleOnSuccess();
    initSmoothAnchors();
  });

  // ---------------------------------------------------------------------
  // Navbar: subtle elevation once the page scrolls
  // ---------------------------------------------------------------------
  function initNavbarScrollState() {
    var navWrap = document.querySelector(".client-navbar-wrap");
    if (!navWrap) return;

    function update() {
      if (window.scrollY > 12) {
        navWrap.classList.add("is-scrolled");
      } else {
        navWrap.classList.remove("is-scrolled");
      }
    }

    update();
    window.addEventListener("scroll", update, { passive: true });
  }

  // ---------------------------------------------------------------------
  // Buttons: soft ripple burst on click (GPU-friendly, transform/opacity)
  // ---------------------------------------------------------------------
  function initRipple() {
    document.addEventListener("click", function (event) {
      var btn = event.target.closest(".btn");
      if (!btn || btn.disabled) return;

      var rect = btn.getBoundingClientRect();
      var size = Math.max(rect.width, rect.height);
      var ripple = document.createElement("span");
      ripple.className = "am-ripple";
      ripple.style.width = ripple.style.height = size + "px";
      ripple.style.left = (event.clientX - rect.left - size / 2) + "px";
      ripple.style.top = (event.clientY - rect.top - size / 2) + "px";

      btn.appendChild(ripple);
      ripple.addEventListener("animationend", function () {
        ripple.remove();
      });

      // track pointer position for the soft hover glow (::before radial gradient)
      btn.style.setProperty("--am-x", (event.clientX - rect.left) + "px");
      btn.style.setProperty("--am-y", (event.clientY - rect.top) + "px");
    });
  }

  // ---------------------------------------------------------------------
  // Gentle scroll reveal for landing sections / cards
  // ---------------------------------------------------------------------
  function initScrollReveal() {
    var targets = document.querySelectorAll(
      ".landing-feature-card, .package-landing-card, .gallery-card, " +
      ".testimonial-card, .how-card, .event-type-card, .client-booking-card, " +
      ".client-portal-stat-card, .section-heading"
    );

    if (!targets.length) return;

    targets.forEach(function (el) {
      el.setAttribute("data-am-reveal", "");
    });

    if (reduceMotion || !("IntersectionObserver" in window)) {
      targets.forEach(function (el) { el.classList.add("am-in-view"); });
      return;
    }

    var observer = new IntersectionObserver(
      function (entries) {
        entries.forEach(function (entry, index) {
          if (entry.isIntersecting) {
            var el = entry.target;
            setTimeout(function () {
              el.classList.add("am-in-view");
            }, (index % 6) * 60);
            observer.unobserve(el);
          }
        });
      },
      { threshold: 0.12, rootMargin: "0px 0px -40px 0px" }
    );

    targets.forEach(function (el) { observer.observe(el); });
  }

  // ---------------------------------------------------------------------
  // Hero: a handful of drifting light particles behind the headline
  // ---------------------------------------------------------------------
  function initHeroParticles() {
    var hero = document.querySelector(".client-hero-section, .auth-left, .cta-banner");
    if (!hero || reduceMotion) return;

    var canvas = document.createElement("canvas");
    canvas.className = "am-hero-particles";
    canvas.setAttribute("aria-hidden", "true");
    hero.appendChild(canvas);

    var ctx = canvas.getContext("2d");
    var particles = [];
    var particleCount = Math.max(18, Math.min(36, Math.floor(hero.offsetWidth / 40)));
    var raf;
    var running = true;

    function resize() {
      canvas.width = hero.offsetWidth;
      canvas.height = hero.offsetHeight;
    }

    function makeParticle() {
      return {
        x: Math.random() * canvas.width,
        y: Math.random() * canvas.height,
        r: Math.random() * 1.6 + 0.4,
        vy: -(Math.random() * 0.25 + 0.05),
        vx: (Math.random() - 0.5) * 0.12,
        alpha: Math.random() * 0.5 + 0.15,
        hue: Math.random() > 0.75 ? "228,201,135" : "253,248,244"
      };
    }

    function init() {
      resize();
      particles = [];
      for (var i = 0; i < particleCount; i++) {
        particles.push(makeParticle());
      }
    }

    function draw() {
      if (!running) return;
      ctx.clearRect(0, 0, canvas.width, canvas.height);

      particles.forEach(function (p) {
        p.x += p.vx;
        p.y += p.vy;

        if (p.y < -10) { p.y = canvas.height + 10; p.x = Math.random() * canvas.width; }
        if (p.x < -10) p.x = canvas.width + 10;
        if (p.x > canvas.width + 10) p.x = -10;

        ctx.beginPath();
        ctx.arc(p.x, p.y, p.r, 0, Math.PI * 2);
        ctx.fillStyle = "rgba(" + p.hue + "," + p.alpha + ")";
        ctx.fill();
      });

      raf = window.requestAnimationFrame(draw);
    }

    init();
    draw();

    window.addEventListener("resize", debounce(function () {
      resize();
    }, 200));

    // pause when off-screen to save battery/CPU
    if ("IntersectionObserver" in window) {
      var io = new IntersectionObserver(function (entries) {
        entries.forEach(function (entry) {
          running = entry.isIntersecting;
          if (running && !raf) draw();
        });
      });
      io.observe(hero);
    }
  }

  // ---------------------------------------------------------------------
  // Forms: gentle shake + focus on the first invalid field
  // (works alongside jQuery Unobtrusive Validation, doesn't replace it)
  // ---------------------------------------------------------------------
  function initFormShake() {
    document.querySelectorAll("form").forEach(function (form) {
      form.addEventListener("invalid-form-am", function () {}); // reserved hook

      form.addEventListener("submit", function () {
        setTimeout(function () {
          var invalidField = form.querySelector(".input-validation-error");
          if (invalidField) {
            var wrap = invalidField.closest(".col-12, .col-md-6, .col-md-4, .col-md-3, .mb-3, .mb-4") || invalidField;
            wrap.classList.add("am-shake");
            setTimeout(function () { wrap.classList.remove("am-shake"); }, 500);
          }
        }, 50);
      });
    });
  }

  // ---------------------------------------------------------------------
  // Success banners: a tiny sparkle burst for delight
  // ---------------------------------------------------------------------
  function initSparkleOnSuccess() {
    var banner = document.querySelector(".client-success-banner");
    if (!banner || reduceMotion) return;

    for (var i = 0; i < 5; i++) {
      var sparkle = document.createElement("span");
      sparkle.className = "am-sparkle-burst";
      sparkle.style.left = (10 + Math.random() * 30) + "px";
      sparkle.style.top = (Math.random() * 20) + "px";
      sparkle.style.animationDelay = (i * 0.08) + "s";
      banner.style.position = "relative";
      banner.appendChild(sparkle);
    }
  }

  // ---------------------------------------------------------------------
  // Smooth-scroll for in-page anchors (Packages / How It Works)
  // ---------------------------------------------------------------------
  function initSmoothAnchors() {
    document.querySelectorAll('a[href^="#"]').forEach(function (link) {
      link.addEventListener("click", function (event) {
        var id = link.getAttribute("href");
        if (!id || id === "#") return;
        var target = document.querySelector(id);
        if (!target) return;

        event.preventDefault();
        target.scrollIntoView({ behavior: reduceMotion ? "auto" : "smooth", block: "start" });
      });
    });
  }

  function debounce(fn, wait) {
    var t;
    return function () {
      clearTimeout(t);
      var args = arguments;
      t = setTimeout(function () { fn.apply(null, args); }, wait);
    };
  }
})();
