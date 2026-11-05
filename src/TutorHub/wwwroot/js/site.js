document.querySelectorAll('input[type="file"][data-max-bytes]').forEach((input) => {
    input.addEventListener('change', () => {
        const file = input.files[0];
        const tooLarge = file !== undefined && file.size > Number(input.dataset.maxBytes);
        input.setCustomValidity(tooLarge ? input.dataset.maxMessage : '');
        input.reportValidity();
    });
});
