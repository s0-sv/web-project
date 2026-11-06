(() => {
    const form = document.getElementById('map-filters');
    const status = document.getElementById('map-status');
    const results = document.getElementById('map-results');
    const debounceMilliseconds = 300;

    const map = L.map('tutor-map').setView([49, 31.4], 6);
    map.attributionControl.setPrefix('<a href="https://leafletjs.com">Leaflet</a>');
    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
    }).addTo(map);

    const cluster = L.markerClusterGroup({ maxClusterRadius: 50 });
    map.addLayer(cluster);

    const profileUrl = (tutor) => `/tutor/${encodeURIComponent(tutor.slug)}`;

    const rate = (tutor) => `${Number(tutor.hourlyRate).toLocaleString('uk-UA')} грн/год`;

    const element = (tag, text, className) => {
        const node = document.createElement(tag);
        if (text) {
            node.textContent = text;
        }
        if (className) {
            node.className = className;
        }
        return node;
    };

    const popup = (tutor) => {
        const content = element('div');
        const name = element('a', tutor.name, 'fw-semibold');
        name.href = profileUrl(tutor);
        content.append(name);
        if (tutor.headline) {
            content.append(element('div', tutor.headline));
        }
        content.append(element('div', `${tutor.city}, ${rate(tutor)}`, 'text-muted'));
        if (tutor.subjects.length > 0) {
            content.append(element('div', tutor.subjects.join(', ')));
        }
        return content;
    };

    const listItem = (tutor) => {
        const item = element('li', null, 'list-group-item');
        const name = element('a', tutor.name, 'fw-semibold');
        name.href = profileUrl(tutor);
        item.append(name);
        const details = [tutor.city, rate(tutor), ...tutor.subjects].join(' · ');
        item.append(element('div', details, 'small text-muted'));
        return item;
    };

    const render = (tutors) => {
        cluster.clearLayers();
        cluster.addLayers(tutors.map((tutor) =>
            L.marker([tutor.latitude, tutor.longitude], { title: tutor.name, alt: tutor.name }).bindPopup(popup(tutor))));
        results.replaceChildren(...tutors.map(listItem));

        if (tutors.length > 0) {
            map.fitBounds(cluster.getBounds(), { maxZoom: 11, padding: [30, 30] });
            status.textContent = `Знайдено репетиторів: ${tutors.length}`;
        } else {
            status.textContent = 'Нікого не знайдено. Змініть фільтри.';
        }
    };

    let current = null;

    const load = async () => {
        current?.abort();
        const request = new AbortController();
        current = request;

        const query = new URLSearchParams();
        for (const [key, value] of new FormData(form)) {
            if (value.trim()) {
                query.set(key, value.trim());
            }
        }

        try {
            const response = await fetch(`${form.dataset.tutorsUrl}?${query}`, { signal: request.signal });
            if (!response.ok) {
                throw new Error(String(response.status));
            }
            const data = await response.json();
            render(data.tutors);
            document.dispatchEvent(new CustomEvent('tutors-loaded', { detail: data }));
        } catch (error) {
            if (error.name !== 'AbortError') {
                status.textContent = 'Не вдалося завантажити репетиторів. Спробуйте ще раз.';
            }
        }
    };

    let timer = null;
    const loadSoon = () => {
        clearTimeout(timer);
        timer = setTimeout(load, debounceMilliseconds);
    };

    form.addEventListener('input', loadSoon);
    form.addEventListener('submit', (event) => {
        event.preventDefault();
        clearTimeout(timer);
        load();
    });
    form.addEventListener('reset', () => setTimeout(load));

    load();
})();
