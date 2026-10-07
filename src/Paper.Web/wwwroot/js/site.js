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

    const folderDialog = document.querySelector("#folder-dialog");
    document.querySelectorAll("[data-open-folder]").forEach(button => button.addEventListener("click", () => folderDialog?.showModal()));
    document.querySelectorAll("[data-close-folder]").forEach(button => button.addEventListener("click", () => folderDialog?.close()));

    document.querySelectorAll("img[data-thumbnail]").forEach(image => {
        image.addEventListener("error", () => {
            image.hidden = true;
            image.closest(".document-card-preview, .queue-thumb")?.classList.add("thumbnail-missing");
        });
    });

    const providerInputs = document.querySelectorAll("[data-storage-provider]");
    const storageFields = document.querySelectorAll("[data-storage-fields]");
    const updateStorageFields = () => {
        const selected = document.querySelector("[data-storage-provider]:checked")?.dataset.storageProvider;
        storageFields.forEach(field => {
            field.hidden = field.dataset.storageFields !== selected;
        });
    };
    providerInputs.forEach(input => input.addEventListener("change", updateStorageFields));
    if (providerInputs.length > 0) updateStorageFields();
})();
