document.querySelectorAll('input[type="file"][data-max-bytes]').forEach((input) => {
    input.addEventListener('change', () => {
        const file = input.files[0];
        const tooLarge = file !== undefined && file.size > Number(input.dataset.maxBytes);
        input.setCustomValidity(tooLarge ? input.dataset.maxMessage : '');
        input.reportValidity();
    });
});

(() => {
    const prefix = 'tutorhub:summary:';
    const userId = document.body.dataset.userId;
    const logoutForm = document.getElementById('logout-form');

    const draftKeys = () => {
        try {
            return Object.keys(localStorage).filter((key) => key.startsWith(prefix));
        } catch {
            return [];
        }
    };

    const removeDrafts = (keys) => {
        try {
            keys.forEach((key) => localStorage.removeItem(key));
        } catch {
            return;
        }
    };

    if (userId) {
        removeDrafts(draftKeys().filter((key) => !key.startsWith(`${prefix}${userId}:`)));
    }

    if (!logoutForm) {
        return;
    }

    const dialog = document.getElementById('logout-drafts-dialog');

    logoutForm.addEventListener('submit', (event) => {
        if (draftKeys().length > 0) {
            event.preventDefault();
            bootstrap.Modal.getOrCreateInstance(dialog).show();
        }
    });

    document.getElementById('logout-discard-drafts').addEventListener('click', () => {
        removeDrafts(draftKeys());
        logoutForm.submit();
    });
})();
