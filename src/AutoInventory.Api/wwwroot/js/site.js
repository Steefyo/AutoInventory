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

  document.querySelector(".input-validation-error")?.focus();
})();
