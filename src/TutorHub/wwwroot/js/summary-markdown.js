(() => {
    const heading = /^(#{1,3}) (.*)$/;
    const bullet = /^[-*] (.*)$/;
    const numbered = /^\d+\. (.*)$/;
    const listTypes = new Set(['bullet', 'numbered']);

    const parse = (markdown) => {
        const blocks = [];
        for (const line of markdown.replace(/\r\n?/g, '\n').split('\n')) {
            if (!line.trim()) {
                continue;
            }

            const headingMatch = line.match(heading);
            const bulletMatch = line.match(bullet);
            const numberedMatch = line.match(numbered);

            if (headingMatch) {
                blocks.push({ type: `h${headingMatch[1].length}`, text: headingMatch[2] });
            } else if (bulletMatch) {
                blocks.push({ type: 'bullet', text: bulletMatch[1] });
            } else if (numberedMatch) {
                blocks.push({ type: 'numbered', text: numberedMatch[1] });
            } else {
                blocks.push({ type: 'paragraph', text: line.startsWith('\\') ? line.slice(1) : line });
            }
        }

        return blocks.filter((block) => block.text.trim());
    };

    const escapeParagraph = (text) =>
        heading.test(text) || bullet.test(text) || numbered.test(text) || text.startsWith('\\') ? `\\${text}` : text;

    const serialize = (blocks) => {
        const filled = blocks
            .map((block) => ({ type: block.type, text: block.text.replace(/\r?\n/g, ' ') }))
            .filter((block) => block.text.trim());

        let markdown = '';
        let number = 0;

        filled.forEach((block, index) => {
            const previous = filled[index - 1];
            number = block.type === 'numbered' && previous?.type === 'numbered' ? number + 1 : 1;

            if (previous) {
                markdown += listTypes.has(block.type) && previous.type === block.type ? '\n' : '\n\n';
            }

            if (block.type === 'paragraph') {
                markdown += escapeParagraph(block.text);
            } else if (block.type === 'bullet') {
                markdown += `- ${block.text}`;
            } else if (block.type === 'numbered') {
                markdown += `${number}. ${block.text}`;
            } else {
                markdown += `${'#'.repeat(Number(block.type[1]))} ${block.text}`;
            }
        });

        return markdown.trimEnd();
    };

    globalThis.SummaryMarkdown = { parse, serialize };
})();
