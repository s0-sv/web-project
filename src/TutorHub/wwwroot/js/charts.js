(() => {
    const seriesColor = '#2a78d6';
    const statusColors = ['#2a78d6', '#eb6834', '#1baf7a'];
    const surface = '#ffffff';
    const inkSecondary = '#52514e';
    const gridline = '#e1e0d9';
    const baseline = '#c3c2b7';
    const maxLabelLength = 24;

    Chart.defaults.font.family = getComputedStyle(document.body).fontFamily;
    Chart.defaults.color = inkSecondary;
    if (matchMedia('(prefers-reduced-motion: reduce)').matches) {
        Chart.defaults.animation = false;
    }

    const valueAxis = {
        beginAtZero: true,
        grid: { color: gridline },
        border: { display: false },
        ticks: { precision: 0 }
    };

    const categoryAxis = {
        grid: { display: false },
        border: { color: baseline }
    };

    const shortLabel = (text) =>
        text.length > maxLabelLength ? `${text.slice(0, maxLabelLength - 1)}…` : text;

    const labelledCategoryAxis = {
        ...categoryAxis,
        ticks: {
            callback(value) {
                return shortLabel(this.getLabelForValue(value));
            }
        }
    };

    const singleSeries = (labels, values, label) => ({
        labels,
        datasets: [{
            label,
            data: values,
            backgroundColor: seriesColor,
            maxBarThickness: 24,
            borderRadius: 4,
            borderSkipped: 'start'
        }]
    });

    const describe = (canvas, title, labels, values) => {
        const parts = labels.map((label, index) => `${label}: ${values[index]}`).join(', ');
        canvas.setAttribute('aria-label', `${title}. ${parts}`);
    };

    const barChart = (canvas, { title, label, labels, values }, indexAxis) => {
        describe(canvas, title, labels, values);
        return new Chart(canvas, {
            type: 'bar',
            data: singleSeries(labels, values, label),
            options: {
                indexAxis,
                maintainAspectRatio: false,
                plugins: { legend: { display: false } },
                scales: indexAxis === 'y'
                    ? { x: valueAxis, y: labelledCategoryAxis }
                    : { x: categoryAxis, y: valueAxis }
            }
        });
    };

    const segmentedChart = (canvas, { title, labels, values }) => {
        describe(canvas, title, labels, values);
        return new Chart(canvas, {
            type: 'bar',
            data: {
                labels: [title],
                datasets: labels.map((label, index) => ({
                    label: `${label}: ${values[index]}`,
                    data: [values[index]],
                    backgroundColor: statusColors[index],
                    borderColor: surface,
                    borderWidth: 2,
                    maxBarThickness: 48
                }))
            },
            options: {
                indexAxis: 'y',
                maintainAspectRatio: false,
                plugins: {
                    legend: { position: 'bottom', labels: { boxWidth: 12, boxHeight: 12 } },
                    tooltip: { callbacks: { title: () => '', label: (item) => item.dataset.label } }
                },
                scales: {
                    x: { stacked: true, display: false, beginAtZero: true, max: values.reduce((sum, value) => sum + value, 0) },
                    y: { stacked: true, display: false }
                }
            }
        });
    };

    const builders = {
        column: (canvas, data) => barChart(canvas, data, 'x'),
        bars: (canvas, data) => barChart(canvas, data, 'y'),
        segmented: segmentedChart
    };

    document.querySelectorAll('canvas[data-chart]').forEach((canvas) => {
        const data = JSON.parse(canvas.dataset.chart);
        builders[data.kind](canvas, data);
    });

    const mapCanvas = document.getElementById('map-subjects-chart');
    if (mapCanvas) {
        const empty = document.getElementById('map-subjects-empty');
        const title = 'Репетитори за предметами';
        let chart = null;

        document.addEventListener('tutors-loaded', ({ detail }) => {
            const labels = detail.subjects.map((subject) => subject.name);
            const values = detail.subjects.map((subject) => subject.tutors);

            mapCanvas.closest('.chart-box').classList.toggle('d-none', labels.length === 0);
            empty.classList.toggle('d-none', labels.length > 0);
            describe(mapCanvas, title, labels, values);

            if (chart) {
                chart.data.labels = labels;
                chart.data.datasets[0].data = values;
                chart.update();
            } else if (labels.length > 0) {
                chart = barChart(mapCanvas, { title, label: 'Репетиторів', labels, values }, 'y');
            }
        });
    }
})();
