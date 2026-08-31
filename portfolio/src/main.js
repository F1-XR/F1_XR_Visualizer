document.documentElement.classList.add("js");

const revealElements = [...document.querySelectorAll(".reveal")];
const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

if (reducedMotion || !("IntersectionObserver" in window)) {
  revealElements.forEach((element) => element.classList.add("is-visible"));
} else {
  const revealObserver = new IntersectionObserver(
    (entries, observer) => {
      entries.forEach((entry) => {
        if (!entry.isIntersecting) return;
        entry.target.classList.add("is-visible");
        observer.unobserve(entry.target);
      });
    },
    { rootMargin: "0px 0px -8%", threshold: 0.08 },
  );

  revealElements.forEach((element) => revealObserver.observe(element));
}

const navigationLinks = [...document.querySelectorAll(".site-nav a[href^='#']")];
const sectionById = new Map(
  navigationLinks
    .map((link) => document.querySelector(link.getAttribute("href")))
    .filter(Boolean)
    .map((section) => [section.id, section]),
);

if ("IntersectionObserver" in window && sectionById.size > 0) {
  const sectionObserver = new IntersectionObserver(
    (entries) => {
      const activeEntry = entries
        .filter((entry) => entry.isIntersecting)
        .sort((a, b) => b.intersectionRatio - a.intersectionRatio)[0];

      if (!activeEntry) return;

      navigationLinks.forEach((link) => {
        const isCurrent = link.getAttribute("href") === `#${activeEntry.target.id}`;
        if (isCurrent) link.setAttribute("aria-current", "true");
        else link.removeAttribute("aria-current");
      });
    },
    { rootMargin: "-25% 0px -55%", threshold: [0, 0.2, 0.5] },
  );

  sectionById.forEach((section) => sectionObserver.observe(section));
}

document.querySelectorAll("[data-current-year]").forEach((element) => {
  element.textContent = new Date().getFullYear().toString();
});
