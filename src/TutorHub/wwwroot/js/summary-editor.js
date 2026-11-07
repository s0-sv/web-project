(() => {
    const root = document.getElementById('summary-editor');
    if (!root) {
        return;
    }

    const { parse, serialize } = globalThis.SummaryMarkdown;

    const labels = {
        paragraph: 'Абзац',
        h1: 'Заголовок 1',
        h2: 'Заголовок 2',
        h3: 'Заголовок 3',
        bullet: 'Елемент маркованого списку',
        numbered: 'Елемент нумерованого списку'
    };

    const renderReadOnly = () => {
        const blocks = parse(root.dataset.markdown);
        root.classList.add('summary-read');

        if (blocks.length === 0) {
            const empty = document.createElement('p');
            empty.className = 'text-muted mb-0';
            empty.textContent = 'Конспект ще порожній.';
            root.append(empty);
            return;
        }

        const tags = { paragraph: 'p', h1: 'h2', h2: 'h3', h3: 'h4' };
        const listTags = { bullet: 'ul', numbered: 'ol' };
        let list = null;
        let listType = null;

        for (const block of blocks) {
            if (listTags[block.type]) {
                if (listType !== block.type) {
                    list = document.createElement(listTags[block.type]);
                    listType = block.type;
                    root.append(list);
                }
                const item = document.createElement('li');
                item.textContent = block.text;
                list.append(item);
            } else {
                list = null;
                listType = null;
                const node = document.createElement(tags[block.type]);
                node.textContent = block.text;
                root.append(node);
            }
        }
    };

    if (root.dataset.editable !== 'true') {
        renderReadOnly();
        return;
    }

    const statusLine = document.getElementById('summary-status');
    const toolbar = document.getElementById('summary-toolbar');
    const conflictBox = document.getElementById('summary-conflict');
    const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
    const saveDelay = 1000;
    const maxRetryDelay = 30000;
    const maxLength = Number(root.dataset.maxLength);
    const draftKey = `tutorhub:summary:${document.body.dataset.userId}:${root.dataset.lessonId}`;

    let version = root.dataset.version;
    let lastSaved = root.dataset.markdown;
    let saving = false;
    let queued = false;
    let saveTimer = null;
    let retryTimer = null;
    let retryDelay = 2000;
    let conflict = null;
    let activeBlock = null;

    const setStatus = (text) => {
        statusLine.textContent = text;
    };

    const readDraft = () => {
        try {
            return JSON.parse(localStorage.getItem(draftKey));
        } catch {
            return null;
        }
    };

    const removeDraft = () => {
        try {
            localStorage.removeItem(draftKey);
        } catch {
            return;
        }
    };

    const writeDraft = (markdown) => {
        try {
            localStorage.setItem(draftKey, JSON.stringify({ markdown, version }));
            return true;
        } catch {
            return false;
        }
    };

    const textOf = (input) => input.textContent.replace(/ /g, ' ').replace(/\n/g, ' ');
    const inputOf = (block) => block.querySelector('.summary-text');
    const blocksInOrder = () => [...root.querySelectorAll('.summary-block')];
    const isList = (type) => type === 'bullet' || type === 'numbered';

    const current = () => serialize(blocksInOrder().map((block) => ({
        type: block.dataset.type,
        text: textOf(inputOf(block))
    })));

    const refreshMarkers = () => {
        let number = 0;
        for (const block of blocksInOrder()) {
            number = block.dataset.type === 'numbered' ? number + 1 : 0;
            if (block.dataset.type === 'numbered') {
                block.dataset.marker = `${number}.`;
            } else if (block.dataset.type === 'bullet') {
                block.dataset.marker = '•';
            } else {
                delete block.dataset.marker;
            }
            inputOf(block).setAttribute('aria-label', labels[block.dataset.type]);
        }
    };

    const refreshToolbar = () => {
        for (const button of toolbar.querySelectorAll('button')) {
            const pressed = activeBlock?.dataset.type === button.dataset.type;
            button.classList.toggle('active', pressed);
            button.setAttribute('aria-pressed', String(pressed));
        }
    };

    const makeEditable = (input) => {
        try {
            input.contentEditable = 'plaintext-only';
        } catch {
            input.contentEditable = 'true';
        }
    };

    const createBlock = (type, text = '') => {
        const block = document.createElement('div');
        block.className = 'summary-block';
        block.dataset.type = type;

        const input = document.createElement('div');
        input.className = 'summary-text';
        input.setAttribute('role', 'textbox');
        input.dataset.placeholder = 'Почніть писати…';
        input.textContent = text;
        makeEditable(input);

        block.append(input);
        return block;
    };

    const offsetIn = (input) => {
        const selection = getSelection();
        if (!selection.rangeCount || !input.contains(selection.anchorNode)) {
            return 0;
        }
        const range = selection.getRangeAt(0).cloneRange();
        range.selectNodeContents(input);
        range.setEnd(selection.getRangeAt(0).startContainer, selection.getRangeAt(0).startOffset);
        return range.toString().length;
    };

    const placeCaret = (input, offset) => {
        input.focus();
        input.normalize();
        const node = input.firstChild;
        const range = document.createRange();
        if (node) {
            range.setStart(node, Math.min(offset, node.length));
        } else {
            range.setStart(input, 0);
        }
        range.collapse(true);
        const selection = getSelection();
        selection.removeAllRanges();
        selection.addRange(range);
    };

    const render = (markdown) => {
        const blocks = parse(markdown);
        root.replaceChildren(...(blocks.length > 0 ? blocks : [{ type: 'paragraph', text: '' }])
            .map((block) => createBlock(block.type, block.text)));
        activeBlock = null;
        refreshMarkers();
        refreshToolbar();
    };

    const showConflict = (server) => {
        conflict = server;
        clearTimeout(saveTimer);
        clearTimeout(retryTimer);
        conflictBox.classList.remove('d-none');
        setStatus('Є конфлікт версій. Зміни лишаються в цьому браузері.');
    };

    const scheduleRetry = () => {
        clearTimeout(retryTimer);
        retryTimer = setTimeout(save, retryDelay);
        retryDelay = Math.min(retryDelay * 2, maxRetryDelay);
    };

    async function save() {
        clearTimeout(saveTimer);
        clearTimeout(retryTimer);

        if (conflict) {
            return;
        }

        if (saving) {
            queued = true;
            return;
        }

        const markdown = current();
        if (markdown === lastSaved) {
            removeDraft();
            setStatus('Збережено');
            return;
        }

        if (markdown.length > maxLength) {
            setStatus(`Конспект завеликий: максимум ${maxLength} символів. На сервер не збережено.`);
            return;
        }

        saving = true;
        setStatus('Зберігається…');

        try {
            const response = await fetch(root.dataset.saveUrl, {
                method: 'POST',
                redirect: 'manual',
                headers: { 'Content-Type': 'application/json', RequestVerificationToken: token },
                body: JSON.stringify({ markdown, version })
            });

            if (response.type === 'opaqueredirect') {
                setStatus('Сесія завершилася. Увійдіть знову: зміни лишаються в цьому браузері.');
            } else if (response.ok) {
                const saved = await response.json();
                version = saved.version;
                lastSaved = markdown;
                retryDelay = 2000;
                if (current() === markdown) {
                    removeDraft();
                    setStatus('Збережено');
                } else {
                    writeDraft(current());
                }
            } else if (response.status === 409) {
                showConflict(await response.json());
            } else if (response.status === 404) {
                setStatus('Заняття більше не існує. Зміни лишаються в цьому браузері.');
            } else if (response.status >= 500) {
                setStatus('Сервер недоступний. Зміни лишаються в цьому браузері, спробуємо ще раз.');
                scheduleRetry();
            } else {
                setStatus('Не вдалося зберегти. Оновіть сторінку: зміни лишаються в цьому браузері.');
            }
        } catch {
            setStatus('Немає з\'єднання. Зміни лишаються в цьому браузері, спробуємо ще раз.');
            scheduleRetry();
        } finally {
            saving = false;
            if (queued) {
                queued = false;
                save();
            }
        }
    }

    const changed = () => {
        refreshMarkers();
        const markdown = current();

        if (markdown === lastSaved && !conflict) {
            removeDraft();
        } else if (!writeDraft(markdown)) {
            setStatus('Не вдалося записати чернетку в браузер.');
        }

        if (conflict) {
            return;
        }

        clearTimeout(saveTimer);
        if (markdown === lastSaved) {
            setStatus('Збережено');
            return;
        }

        setStatus('Є незбережені зміни…');
        saveTimer = setTimeout(save, saveDelay);
    };

    const setType = (block, type) => {
        block.dataset.type = type;
        refreshToolbar();
        changed();
    };

    const deleteSelection = () => {
        const selection = getSelection();
        if (selection.rangeCount && !selection.isCollapsed) {
            selection.deleteFromDocument();
        }
    };

    const splitBlock = (block) => {
        const input = inputOf(block);
        const { type } = block.dataset;
        deleteSelection();
        const text = textOf(input);

        if (isList(type) && !text) {
            setType(block, 'paragraph');
            return;
        }

        const offset = offsetIn(input);
        input.textContent = text.slice(0, offset);
        const next = createBlock(isList(type) ? type : 'paragraph', text.slice(offset));
        block.after(next);
        placeCaret(inputOf(next), 0);
        changed();
    };

    const mergeWithPrevious = (block) => {
        const previous = block.previousElementSibling;
        if (!previous) {
            return;
        }
        const target = inputOf(previous);
        const joint = textOf(target).length;
        target.textContent = textOf(target) + textOf(inputOf(block));
        block.remove();
        placeCaret(target, joint);
        changed();
    };

    const moveCaretAcross = (block, direction) => {
        const input = inputOf(block);
        const before = offsetIn(input);

        requestAnimationFrame(() => {
            if (document.activeElement !== input || offsetIn(input) !== before) {
                return;
            }
            const neighbour = direction < 0 ? block.previousElementSibling : block.nextElementSibling;
            if (neighbour) {
                const target = inputOf(neighbour);
                placeCaret(target, direction < 0 ? textOf(target).length : 0);
            }
        });
    };

    const insertPlainText = (input, text) => {
        deleteSelection();
        const value = textOf(input);
        const offset = offsetIn(input);
        input.textContent = value.slice(0, offset) + text + value.slice(offset);
        placeCaret(input, offset + text.length);
    };

    const pasteText = (block, pasted) => {
        const lines = parse(pasted);
        const input = inputOf(block);

        if (!/\n/.test(pasted.trim()) || lines.length < 2) {
            insertPlainText(input, pasted.replace(/\s*\r?\n\s*/g, ' '));
            changed();
            return;
        }

        deleteSelection();
        const value = textOf(input);
        const offset = offsetIn(input);
        const tail = value.slice(offset);
        const [first, ...rest] = lines;

        input.textContent = value.slice(0, offset) + first.text;
        if (!value && block.dataset.type === 'paragraph') {
            block.dataset.type = first.type;
        }

        let last = block;
        for (const line of rest) {
            const next = createBlock(line.type, line.text);
            last.after(next);
            last = next;
        }

        const target = inputOf(last);
        const caret = textOf(target).length;
        target.textContent = textOf(target) + tail;
        placeCaret(target, caret);
        changed();
    };

    root.addEventListener('keydown', (event) => {
        const block = event.target.closest('.summary-block');
        if (!block || event.isComposing) {
            return;
        }

        const input = inputOf(block);

        if ((event.ctrlKey || event.metaKey) && ['b', 'i', 'u'].includes(event.key.toLowerCase())) {
            event.preventDefault();
        } else if (event.key === 'Enter') {
            event.preventDefault();
            splitBlock(block);
        } else if (event.key === 'Backspace' && getSelection().isCollapsed && offsetIn(input) === 0) {
            event.preventDefault();
            if (block.dataset.type !== 'paragraph') {
                setType(block, 'paragraph');
            } else {
                mergeWithPrevious(block);
            }
        } else if (event.key === 'Delete' && getSelection().isCollapsed
            && offsetIn(input) === textOf(input).length && block.nextElementSibling) {
            event.preventDefault();
            const next = block.nextElementSibling;
            const joint = textOf(input).length;
            input.textContent = textOf(input) + textOf(inputOf(next));
            next.remove();
            placeCaret(input, joint);
            changed();
        } else if (event.key === 'ArrowUp') {
            moveCaretAcross(block, -1);
        } else if (event.key === 'ArrowDown') {
            moveCaretAcross(block, 1);
        }
    });

    root.addEventListener('input', (event) => {
        const block = event.target.closest('.summary-block');
        if (!block || event.isComposing) {
            return;
        }

        const input = inputOf(block);
        const text = textOf(input);

        if (!text) {
            input.replaceChildren();
        }

        if (block.dataset.type === 'paragraph') {
            const prefix = text.match(/^(#{1,3}|[-*]|\d+\.) /);
            if (prefix && offsetIn(input) === prefix[0].length) {
                const marker = prefix[1];
                input.textContent = text.slice(prefix[0].length);
                block.dataset.type = marker.startsWith('#') ? `h${marker.length}` : /\d/.test(marker) ? 'numbered' : 'bullet';
                placeCaret(input, 0);
                refreshToolbar();
            }
        }

        changed();
    });

    root.addEventListener('compositionend', changed);

    root.addEventListener('paste', (event) => {
        const block = event.target.closest('.summary-block');
        if (!block) {
            return;
        }
        event.preventDefault();
        pasteText(block, event.clipboardData.getData('text/plain'));
    });

    root.addEventListener('focusin', (event) => {
        activeBlock = event.target.closest('.summary-block');
        refreshToolbar();
    });

    root.addEventListener('mousedown', (event) => {
        if (event.target === root) {
            event.preventDefault();
            const input = inputOf(root.lastElementChild);
            placeCaret(input, textOf(input).length);
        }
    });

    toolbar.addEventListener('mousedown', (event) => event.preventDefault());

    toolbar.addEventListener('click', (event) => {
        const button = event.target.closest('button');
        if (!button) {
            return;
        }
        const block = activeBlock ?? root.lastElementChild;
        setType(block, button.dataset.type);
        const input = inputOf(block);
        if (document.activeElement !== input) {
            placeCaret(input, textOf(input).length);
        }
    });

    document.getElementById('summary-keep-mine').addEventListener('click', () => {
        version = conflict.version;
        lastSaved = conflict.markdown;
        conflict = null;
        conflictBox.classList.add('d-none');
        writeDraft(current());
        save();
    });

    document.getElementById('summary-take-server').addEventListener('click', () => {
        version = conflict.version;
        lastSaved = conflict.markdown;
        conflict = null;
        conflictBox.classList.add('d-none');
        removeDraft();
        render(lastSaved);
        setStatus('Збережено');
    });

    window.addEventListener('online', () => {
        if (!conflict && current() !== lastSaved) {
            retryDelay = 2000;
            save();
        }
    });

    const draft = readDraft();
    const server = { markdown: lastSaved, version };

    if (draft && typeof draft.markdown === 'string' && draft.markdown !== lastSaved) {
        render(draft.markdown);
        if (draft.version === version) {
            setStatus('Відновлено незбережені зміни…');
            saveTimer = setTimeout(save, saveDelay);
        } else {
            version = draft.version;
            showConflict(server);
        }
    } else {
        removeDraft();
        render(lastSaved);
        setStatus('Збережено');
    }
})();
