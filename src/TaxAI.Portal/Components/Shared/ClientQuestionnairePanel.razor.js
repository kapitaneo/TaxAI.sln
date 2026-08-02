const antiforgeryHeader = "X-TaxAI-Antiforgery";
const uploadEndpoint = "/client-intake/documents";

function mediaKind(file) {
    switch (file.type) {
        case "application/pdf":
            return "Pdf";
        case "image/jpeg":
            return "Jpeg";
        case "image/png":
            return "Png";
        default:
            return "";
    }
}

async function upload(form) {
    const input = form.querySelector('input[type="file"]');
    const token = form.querySelector(
        'input[name="__RequestVerificationToken"]');
    const message = form.querySelector("[data-upload-message]");
    const button = form.querySelector("[data-upload-button]");
    const file = input?.files?.[0];

    if (!file || !token || !message || !button) {
        return;
    }

    button.disabled = true;
    message.textContent = "Uploading…";

    const body = new FormData();
    body.append("category", form.elements.category.value);
    body.append("ordinal", form.elements.ordinal.value);
    body.append("declaredMediaKind", mediaKind(file));
    body.append("declaredLength", String(file.size));
    body.append("document", file, "upload.bin");

    try {
        const response = await fetch(uploadEndpoint, {
            method: "POST",
            credentials: "same-origin",
            headers: { [antiforgeryHeader]: token.value },
            body
        });

        if (response.ok) {
            message.textContent = "Upload evaluated. Refreshing status…";
            window.location.assign("/client-intake");
        } else if (response.status === 409) {
            message.textContent = "Upload already received.";
        } else if (response.status === 429) {
            message.textContent = "Too many requests. Try again later.";
        } else {
            message.textContent = "Upload was not accepted.";
        }
    } catch {
        message.textContent = "Upload was not accepted.";
    } finally {
        button.disabled = false;
    }
}

document.addEventListener("click", event => {
    const button = event.target instanceof Element
        ? event.target.closest("[data-upload-button]")
        : null;
    const form = button?.closest("form[data-intake-upload]");

    if (form) {
        upload(form);
    }
});
