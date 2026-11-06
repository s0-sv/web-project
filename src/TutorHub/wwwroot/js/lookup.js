$('select[data-lookup-url]').each(function () {
    const select = $(this);

    select.select2({
        width: '100%',
        allowClear: true,
        placeholder: select.data('lookup-placeholder'),
        minimumInputLength: Number(select.data('lookup-min-length')),
        ajax: {
            url: select.data('lookup-url'),
            delay: Number(select.data('lookup-delay')),
            dataType: 'json',
            data: (params) => ({ q: params.term }),
            processResults: (data) => ({ results: data.results })
        },
        language: {
            inputTooShort: (args) => `Введіть ще символів: ${args.minimum - args.input.length}`,
            noResults: () => 'Нічого не знайдено',
            searching: () => 'Пошук…',
            errorLoading: () => 'Не вдалося завантажити результати',
            removeAllItems: () => 'Очистити'
        }
    });

    select.on('change', () => {
        if (select.closest('form').data('validator')) {
            select.valid();
        }
    });
});
