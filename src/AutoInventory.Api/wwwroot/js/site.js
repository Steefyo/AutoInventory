(() => {
  document.querySelectorAll("form[data-autosubmit]").forEach(form => {
    form.querySelectorAll(".js-hide").forEach(el => (el.hidden = true));
    form.addEventListener("change", () => {
      form.querySelectorAll("select, input").forEach(el => (el.disabled = !el.value));
      form.submit();
    });
  });

  document.querySelectorAll("[data-refresh]").forEach(input => {
    const button = document.querySelector(input.dataset.refresh);
    if (!button) return;
    button.hidden = true;
    input.addEventListener("change", () => button.click());
  });

  document.querySelectorAll("[data-invalidates]").forEach(input => {
    const original = input.value;
    input.addEventListener("input", () => {
      const target = document.querySelector(input.dataset.invalidates);
      if (target) target.disabled = input.value !== original;
    });
  });

  document.querySelectorAll("form[data-confirm]").forEach(form => {
    form.addEventListener("submit", e => {
      if (!confirm(form.dataset.confirm)) e.preventDefault();
    });
  });

  document.addEventListener("submit", e => {
    if (e.defaultPrevented) return;
    const form = e.target;
    const submitter = e.submitter;
    setTimeout(() => {
      form.setAttribute("aria-busy", "true");
      form.querySelectorAll("button").forEach(button => {
        if (button.disabled) return;
        button.disabled = true;
        button.dataset.busyDisabled = "";
        if (button === submitter) {
          button.dataset.label = button.textContent;
          button.textContent = button.dataset.busy || "Bezig…";
        }
      });
    });
  });

  window.addEventListener("pageshow", e => {
    if (!e.persisted) return;
    document.querySelectorAll("form[aria-busy]").forEach(form => {
      form.removeAttribute("aria-busy");
      form.querySelectorAll("button[data-busy-disabled]").forEach(button => {
        button.disabled = false;
        delete button.dataset.busyDisabled;
        if (button.dataset.label !== undefined) button.textContent = button.dataset.label;
      });
    });
  });

  document.querySelector(".input-validation-error")?.focus();
})();
