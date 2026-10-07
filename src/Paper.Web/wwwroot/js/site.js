(() => {
    const sidebar = document.querySelector("#sidebar");
    const toggle = document.querySelector("[data-sidebar-toggle]");
    if (sidebar && toggle) {
        toggle.addEventListener("click", () => sidebar.classList.toggle("is-open"));
        document.querySelectorAll(".sidebar a").forEach(link => link.addEventListener("click", () => sidebar.classList.remove("is-open")));
    }

    const dialog = document.querySelector("#upload-dialog");
    document.querySelectorAll("[data-open-upload]").forEach(button => button.addEventListener("click", () => dialog?.showModal()));
    document.querySelectorAll("[data-close-upload]").forEach(button => button.addEventListener("click", () => dialog?.close()));
    dialog?.addEventListener("click", event => {
        if (event.target === dialog) dialog.close();
    });

    document.querySelectorAll("img[data-thumbnail]").forEach(image => {
        image.addEventListener("error", () => {
            image.hidden = true;
            image.closest(".document-card-preview, .queue-thumb")?.classList.add("thumbnail-missing");
        });
    });
})();
